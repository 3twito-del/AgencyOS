using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Provisioning;

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
public sealed record BootstrapSystemResult(
    OrganizationId OrganizationId,
    UserId OwnerUserId,
    MembershipId MembershipId,
    DateTimeOffset InitializedAt);

/// <summary>
/// Creates the first organization and its owner, once, on an uninitialized system.
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
/// </remarks>
public sealed class BootstrapSystemHandler
{
    private readonly ISystemInitializationRepository _initialization;
    private readonly IOrganizationRepository _organizations;
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public BootstrapSystemHandler(
        ISystemInitializationRepository initialization,
        IOrganizationRepository organizations,
        IMembershipRepository memberships,
        IUserRepository users,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _initialization = initialization;
        _organizations = organizations;
        _memberships = memberships;
        _users = users;
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

        Membership ownership = Membership.Grant(
            organization.Id,
            owner.Id,
            AgencyRole.Owner,
            owner.Id,
            now);

        _memberships.Add(ownership);

        _initialization.Add(SystemInitialization.Record(organization.Id, owner.Id, now));

        // Bootstrap is the most consequential single operation the system has: it
        // is where authority comes from. It is audited as three records so the
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

        // One transaction. Either the system is initialized with a complete,
        // audited starting state, or it is untouched.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BootstrapSystemResult(organization.Id, owner.Id, ownership.Id, now);
    }
}
