using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Application.Provisioning;

/// <summary>Raised when initialization is attempted on a system that already has it.</summary>
public sealed class SystemAlreadyInitializedException : Exception
{
    public SystemAlreadyInitializedException()
        : base("The system has already been initialized. Bootstrap is available only once.")
    {
    }
}

/// <param name="OrganizationName">Display name of the initial organization.</param>
/// <param name="OrganizationLegalName">Registered legal name, when it differs.</param>
/// <param name="OrganizationType">Classification of the initial organization.</param>
/// <param name="OwnerSubject">Identity-provider subject of the first owner.</param>
/// <param name="OwnerDisplayName">Display name of the first owner.</param>
/// <param name="OwnerEmail">Email of the first owner.</param>
public sealed record BootstrapSystemCommand(
    string OrganizationName,
    string? OrganizationLegalName,
    OrganizationType OrganizationType,
    string OwnerSubject,
    string OwnerDisplayName,
    string OwnerEmail);

/// <param name="OrganizationId">Identifier of the created organization.</param>
/// <param name="OwnerUserId">Identifier of the created owner.</param>
/// <param name="MembershipId">Identifier of the ownership membership.</param>
/// <param name="InitializedAt">When initialization completed.</param>
/// <param name="ReleasePolicyPlatform">Platform the initial release policy governs.</param>
/// <param name="ReleasePolicyRing">Ring the initial release policy governs.</param>
/// <param name="ReleasePolicyVersion">The single client version the initial policy admits.</param>
public sealed record BootstrapSystemResult(
    OrganizationId OrganizationId,
    UserId OwnerUserId,
    MembershipId MembershipId,
    DateTimeOffset InitializedAt,
    string ReleasePolicyPlatform,
    string ReleasePolicyRing,
    string ReleasePolicyVersion);

/// <summary>
/// Creates the first organization, its owner, and the release policy that makes
/// the instance usable - once, on an uninitialized system.
/// </summary>
/// <remarks>
/// <para>
/// This is the only path that produces authority without already having it, which
/// is why it is constrained from three directions rather than one:
/// </para>
/// <list type="number">
/// <item>the caller must present the out-of-band bootstrap token (enforced at the
/// API edge, so an unauthenticated caller cannot reach this handler);</item>
/// <item>the system must be uninitialized, checked here;</item>
/// <item>the initialization row is a database singleton, so two callers that both
/// pass the check still cannot both succeed.</item>
/// </list>
/// <para>
/// It grants no session and returns no credential. It creates a user whom the
/// configured identity provider must then authenticate normally. Every subsequent
/// operation, including creating a second organization, goes through the ordinary
/// permission checks - bootstrap widens nothing.
/// </para>
/// <para>
/// <strong>Atomicity.</strong> Every component - user, organization, membership,
/// initialization record, release policy and the audit records describing them -
/// is added to one unit of work and committed by a single
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. EF Core wraps a single save in one
/// transaction, so the outcome is all or nothing. A partially initialized system
/// is not a state this handler can produce.
/// </para>
/// </remarks>
public sealed class BootstrapSystemHandler
{
    private readonly ISystemInitializationRepository _initialization;
    private readonly IOrganizationRepository _organizations;
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IReleasePolicyRepository _releasePolicies;
    private readonly IExecutionContext _execution;
    private readonly IApiContractPolicy _apiContract;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public BootstrapSystemHandler(
        ISystemInitializationRepository initialization,
        IOrganizationRepository organizations,
        IMembershipRepository memberships,
        IUserRepository users,
        IReleasePolicyRepository releasePolicies,
        IExecutionContext execution,
        IApiContractPolicy apiContract,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _initialization = initialization;
        _organizations = organizations;
        _memberships = memberships;
        _users = users;
        _releasePolicies = releasePolicies;
        _execution = execution;
        _apiContract = apiContract;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<BootstrapSystemResult> HandleAsync(
        BootstrapSystemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await _initialization.IsInitializedAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SystemAlreadyInitializedException();
        }

        // The initial release policy is derived from the client performing the
        // bootstrap, because that is the one build we know is meant to use this
        // instance. Without an identity there is nothing to admit, and the
        // alternative - a permissive default - would leave the first system in the
        // least governed state of its life.
        ClientIdentity client = _execution.Client
            ?? throw new DomainException(
                "First-run initialization requires the calling client to present its identity headers "
                    + "(platform, channel, client version and API contract version). The initial release "
                    + "policy is derived from them.");

        if (client.ApiContractVersion < _apiContract.Minimum
            || client.ApiContractVersion > _apiContract.Maximum)
        {
            // Initializing here would publish a policy that immediately locks out
            // the client that just created the system.
            throw new DomainException(
                $"The bootstrapping client speaks API contract {client.ApiContractVersion}, outside this "
                    + $"server's supported range {_apiContract.Minimum}-{_apiContract.Maximum}.");
        }

        DateTimeOffset now = _clock.UtcNow;

        User owner = User.Register(
            command.OwnerSubject,
            command.OwnerDisplayName,
            command.OwnerEmail,
            now);

        _users.Add(owner);

        Organization organization = Organization.Create(
            command.OrganizationName,
            command.OrganizationLegalName,
            command.OrganizationType,
            owner.Id,
            now);

        _organizations.Add(organization);

        // The creator owns what they create, otherwise a new organization would be
        // immediately unadministrable.
        Membership ownership = Membership.Grant(
            organization.Id,
            owner.Id,
            AgencyRole.Owner,
            owner.Id,
            now);

        _memberships.Add(ownership);

        _initialization.Add(SystemInitialization.Record(organization.Id, owner.Id, now));

        // The narrowest policy that works: exactly one platform, one ring, and one
        // version - the bootstrapping build. Latest equals minimum, so nothing
        // older is admitted and there is no wildcard to widen later by accident.
        // Publishing a further version is an ordinary authorized operation.
        ReleasePolicy releasePolicy = ReleasePolicy.Create(
            platform: client.Platform,
            ring: client.Ring,
            latestVersion: client.Version.Text,
            minimumSupportedVersion: client.Version.Text,
            apiContractMinimum: _apiContract.Minimum,
            apiContractMaximum: _apiContract.Maximum,
            now: now,
            behindPolicy: UpdatePolicy.Recommended,
            securityEpoch: 1,
            killSwitch: false,
            revokedVersions: []);

        _releasePolicies.Add(releasePolicy);

        string ringName = ReleaseRingNames.ToWireName(client.Ring);

        // Bootstrap is the most consequential single operation the system has: it
        // is where authority comes from. It is audited as separate records so the
        // resulting state is explainable from the trail alone.
        _audit.Record(
            AuditAction.SystemBootstrapped,
            entityType: nameof(SystemInitialization),
            entityId: SystemInitialization.SingletonId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            organizationId: organization.Id,
            semanticDelta: new
            {
                OrganizationId = organization.Id.ToString(),
                OrganizationName = organization.Name,
                OwnerUserId = owner.Id.ToString(),
                OwnerSubject = owner.ExternalSubject,
                ReleasePolicyPlatform = client.Platform,
                ReleasePolicyRing = ringName,
                ReleasePolicyVersion = client.Version.Text,
            },
            reason: "First-run initialization.");

        _audit.Record(
            AuditAction.UserRegistered,
            entityType: nameof(User),
            entityId: owner.Id.ToString(),
            organizationId: organization.Id,
            semanticDelta: new { Subject = owner.ExternalSubject, owner.DisplayName });

        _audit.Record(
            AuditAction.OrganizationCreated,
            entityType: nameof(Organization),
            entityId: organization.Id.ToString(),
            organizationId: organization.Id,
            semanticDelta: new
            {
                organization.Name,
                organization.LegalName,
                Type = organization.Type.ToString(),
            });

        _audit.Record(
            AuditAction.MembershipGranted,
            entityType: nameof(Membership),
            entityId: ownership.Id.ToString(),
            organizationId: organization.Id,
            semanticDelta: new
            {
                UserId = owner.Id.ToString(),
                Role = ownership.Role.ToString(),
                Reason = "First-run owner.",
            });

        _audit.Record(
            AuditAction.ReleasePolicyPublished,
            entityType: nameof(ReleasePolicy),
            entityId: $"{client.Platform}/{ringName}",
            organizationId: organization.Id,
            semanticDelta: new
            {
                Platform = client.Platform,
                Ring = ringName,
                LatestVersion = releasePolicy.LatestVersion,
                MinimumSupportedVersion = releasePolicy.MinimumSupportedVersion,
                ApiContractMinimum = releasePolicy.ApiContractMinimum,
                ApiContractMaximum = releasePolicy.ApiContractMaximum,
            },
            reason: "First-run initialization: admits only the bootstrapping build.");

        // One transaction. Either the system is initialized with a complete,
        // audited, immediately usable starting state, or it is untouched.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BootstrapSystemResult(
            organization.Id,
            owner.Id,
            ownership.Id,
            now,
            client.Platform,
            ringName,
            client.Version.Text);
    }
}
