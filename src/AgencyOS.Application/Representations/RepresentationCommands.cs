using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Representations;

// ------------------------------------------------------------------ prospects

public sealed record CreateProspectCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    UserId OwnerUserId,
    DateOnly IdentifiedOn,
    string? Source,
    string? StrategyNotes,
    DateOnly? NextFollowUpOn);

/// <param name="Stage">Stage to move to. Must be legal from the current one.</param>
/// <param name="OccurredOn">When it actually happened, which is not always today.</param>
/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record AdvanceProspectCommand(
    OrganizationId OrganizationId,
    ProspectId ProspectId,
    ProspectStage Stage,
    DateOnly OccurredOn,
    string? Reason,
    int ExpectedVersion);

/// <param name="StartsOn">When the representation takes effect.</param>
/// <param name="LeadUserId">Who will lead the relationship.</param>
/// <param name="Scopes">Areas the agency will represent.</param>
public sealed record ConvertProspectCommand(
    OrganizationId OrganizationId,
    ProspectId ProspectId,
    DateOnly StartsOn,
    UserId LeadUserId,
    IReadOnlyList<RepresentationScopeArea> Scopes,
    bool? IsExclusive,
    string? Territory,
    string? Notes,
    int ExpectedVersion);

/// <summary>Starts pursuing somebody.</summary>
/// <remarks>
/// Refused if the agency already represents them or is already pursuing them. Two
/// open pursuits of one person means two agents working without knowing about each
/// other, which is precisely what this system exists to prevent.
/// </remarks>
public sealed class CreateProspectHandler
{
    private readonly IProspectRepository _prospects;
    private readonly IRepresentationRepository _representations;
    private readonly IPersonRepository _people;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProspectHandler(
        IProspectRepository prospects,
        IRepresentationRepository representations,
        IPersonRepository people,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _prospects = prospects;
        _representations = representations;
        _people = people;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProspectId> HandleAsync(
        CreateProspectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProspectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool personExists = await _people
            .ExistsActiveAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (!personExists)
        {
            throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());
        }

        await RequireTenantMemberAsync(
            _memberships,
            command.OrganizationId,
            command.OwnerUserId,
            cancellationToken).ConfigureAwait(false);

        Prospect? open = await _prospects
            .FindOpenForPersonAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (open is not null)
        {
            throw new AlreadyExistsException(
                "This person is already being pursued. Continue that prospect rather than starting a second.");
        }

        Domain.Representations.Representation? represented = await _representations
            .FindNonTerminalForPersonAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (represented is not null)
        {
            throw new AlreadyExistsException(
                "This person is already represented. End that representation before pursuing them again.");
        }

        Prospect prospect = Prospect.Create(
            command.OrganizationId,
            command.PersonId,
            command.OwnerUserId,
            command.IdentifiedOn,
            actor,
            _clock.UtcNow,
            command.Source,
            command.StrategyNotes,
            command.NextFollowUpOn);

        _prospects.Add(prospect);

        _audit.Record(
            AuditAction.ProspectCreated,
            entityType: nameof(Prospect),
            entityId: prospect.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProspectsWrite,
            semanticDelta: new
            {
                PersonId = command.PersonId.ToString(),
                Owner = command.OwnerUserId.ToString(),
                Stage = prospect.Stage.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return prospect.Id;
    }

    /// <summary>
    /// Confirms a user actually works for this tenant.
    /// </summary>
    /// <remarks>
    /// Checked in the application rather than by a foreign key, because membership
    /// is temporal: a key would forbid keeping the record after somebody's
    /// membership was revoked, which is exactly the history worth keeping.
    /// </remarks>
    internal static async Task RequireTenantMemberAsync(
        IMembershipRepository memberships,
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        Domain.Memberships.Membership? membership = await memberships
            .FindActiveAsync(organizationId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (membership is null)
        {
            throw new EntityNotFoundException(
                "Membership",
                $"{userId} is not an active member of this organization.");
        }
    }
}

/// <summary>Moves a pursuit to a new stage.</summary>
public sealed class AdvanceProspectHandler
{
    private readonly IProspectRepository _prospects;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AdvanceProspectHandler(
        IProspectRepository prospects,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _prospects = prospects;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(AdvanceProspectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProspectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Stage == ProspectStage.Converted)
        {
            // Conversion creates a representation. Allowing it here would mark the
            // prospect converted with nothing to show for it.
            throw new Domain.Common.DomainException(
                "Converting a prospect is a separate command, because it must create the representation "
                    + "in the same transaction.");
        }

        Prospect prospect =
            await _prospects.FindAsync(command.OrganizationId, command.ProspectId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Prospect), command.ProspectId.ToString());

        prospect.RequireVersion(command.ExpectedVersion);

        ProspectStage from = prospect.Stage;

        prospect.TransitionTo(command.Stage, command.OccurredOn, actor, _clock.UtcNow, command.Reason);

        _audit.Record(
            AuditAction.ProspectStageChanged,
            entityType: nameof(Prospect),
            entityId: prospect.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProspectsWrite,
            semanticDelta: new { From = from.ToString(), To = prospect.Stage.ToString(), command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Turns a pursuit into a representation, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// The most consequential command in M4, and the one a retry could most easily
/// corrupt. Three things stop a duplicate active representation: the command is
/// idempotent when routed with a key, the domain refuses a second non-terminal
/// representation, and a partial unique index refuses it in the database. The last
/// one holds even if the first two are bypassed entirely.
/// </para>
/// <para>
/// The prospect is marked converted and the representation created together, so
/// there is no state in which a prospect claims to have converted into nothing.
/// </para>
/// </remarks>
public sealed class ConvertProspectHandler
{
    private readonly IProspectRepository _prospects;
    private readonly IRepresentationRepository _representations;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ConvertProspectHandler(
        IProspectRepository prospects,
        IRepresentationRepository representations,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _prospects = prospects;
        _representations = representations;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RepresentationId> HandleAsync(
        ConvertProspectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Converting creates a representation, so it needs the permission to do so
        // as well as the permission to close the pursuit.
        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProspectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Prospect prospect =
            await _prospects.FindAsync(command.OrganizationId, command.ProspectId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Prospect), command.ProspectId.ToString());

        prospect.RequireVersion(command.ExpectedVersion);

        await CreateProspectHandler.RequireTenantMemberAsync(
            _memberships,
            command.OrganizationId,
            command.LeadUserId,
            cancellationToken).ConfigureAwait(false);

        Domain.Representations.Representation? existing = await _representations
            .FindNonTerminalForPersonAsync(command.OrganizationId, prospect.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new AlreadyExistsException(
                "This person already has a live representation. Converting again would create a second.");
        }

        DateTimeOffset now = _clock.UtcNow;

        Domain.Representations.Representation representation = Domain.Representations.Representation.Create(
            command.OrganizationId,
            prospect.PersonId,
            command.StartsOn,
            actor,
            now,
            endsOn: null,
            command.IsExclusive,
            command.Territory,
            command.Notes);

        representation.AssignTeamMember(
            command.LeadUserId,
            RepresentationTeamRole.Lead,
            command.StartsOn,
            now);

        foreach (RepresentationScopeArea area in command.Scopes ?? [])
        {
            representation.AddScope(area, command.StartsOn, now);
        }

        // A converted prospect becomes a client immediately. Leaving it Pending
        // would mean the conversion produced somebody who is represented on paper
        // and not a client in any list.
        representation.TransitionTo(
            RepresentationStatus.Active,
            command.StartsOn,
            actor,
            now,
            reason: "Converted from prospect.");

        _representations.Add(representation);

        prospect.MarkConverted(representation.Id, command.StartsOn, actor, now);

        _audit.Record(
            AuditAction.ProspectConverted,
            entityType: nameof(Prospect),
            entityId: prospect.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new
            {
                RepresentationId = representation.Id.ToString(),
                PersonId = prospect.PersonId.ToString(),
                Lead = command.LeadUserId.ToString(),
                Scopes = representation.CurrentScopes.Select(x => x.Area.ToString()).ToArray(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return representation.Id;
    }
}

// ------------------------------------------------------------ representations

public sealed record CreateRepresentationCommand(
    OrganizationId OrganizationId,
    PersonId PersonId,
    DateOnly StartsOn,
    UserId LeadUserId,
    IReadOnlyList<RepresentationScopeArea> Scopes,
    DateOnly? EndsOn,
    bool? IsExclusive,
    string? Territory,
    string? Notes);

public sealed record TransitionRepresentationCommand(
    OrganizationId OrganizationId,
    RepresentationId RepresentationId,
    RepresentationStatus Status,
    DateOnly OccurredOn,
    string? Reason,
    int ExpectedVersion);

public sealed record ChangeRepresentationScopeCommand(
    OrganizationId OrganizationId,
    RepresentationId RepresentationId,
    RepresentationScopeArea Area,
    DateOnly OccurredOn,
    int ExpectedVersion);

public sealed record AssignRepresentationTeamMemberCommand(
    OrganizationId OrganizationId,
    RepresentationId RepresentationId,
    UserId UserId,
    RepresentationTeamRole Role,
    DateOnly OccurredOn,
    int ExpectedVersion);

public sealed record RemoveRepresentationTeamMemberCommand(
    OrganizationId OrganizationId,
    RepresentationId RepresentationId,
    UserId UserId,
    DateOnly OccurredOn,
    int ExpectedVersion);

/// <summary>Creates a representation directly, without a preceding pursuit.</summary>
/// <remarks>
/// Some clients arrive already decided. This is the same result as conversion
/// without inventing a prospect record for a pursuit that never happened.
/// </remarks>
public sealed class CreateRepresentationHandler
{
    private readonly IRepresentationRepository _representations;
    private readonly IPersonRepository _people;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRepresentationHandler(
        IRepresentationRepository representations,
        IPersonRepository people,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _representations = representations;
        _people = people;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RepresentationId> HandleAsync(
        CreateRepresentationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool personExists = await _people
            .ExistsActiveAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (!personExists)
        {
            throw new EntityNotFoundException(nameof(Person), command.PersonId.ToString());
        }

        await CreateProspectHandler.RequireTenantMemberAsync(
            _memberships,
            command.OrganizationId,
            command.LeadUserId,
            cancellationToken).ConfigureAwait(false);

        Domain.Representations.Representation? existing = await _representations
            .FindNonTerminalForPersonAsync(command.OrganizationId, command.PersonId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new AlreadyExistsException("This person already has a live representation.");
        }

        DateTimeOffset now = _clock.UtcNow;

        Domain.Representations.Representation representation = Domain.Representations.Representation.Create(
            command.OrganizationId,
            command.PersonId,
            command.StartsOn,
            actor,
            now,
            command.EndsOn,
            command.IsExclusive,
            command.Territory,
            command.Notes);

        representation.AssignTeamMember(command.LeadUserId, RepresentationTeamRole.Lead, command.StartsOn, now);

        foreach (RepresentationScopeArea area in command.Scopes ?? [])
        {
            representation.AddScope(area, command.StartsOn, now);
        }

        _representations.Add(representation);

        _audit.Record(
            AuditAction.RepresentationCreated,
            entityType: nameof(Domain.Representations.Representation),
            entityId: representation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new
            {
                PersonId = command.PersonId.ToString(),
                representation.StartsOn,
                Lead = command.LeadUserId.ToString(),
                Scopes = representation.CurrentScopes.Select(x => x.Area.ToString()).ToArray(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return representation.Id;
    }
}

/// <summary>
/// Moves a representation to a new status.
/// </summary>
/// <remarks>
/// One handler for activate, suspend, resume, terminate and expire. They differ
/// only in the target status, and the legality question is answered once by the
/// domain's transition table rather than five times here.
/// </remarks>
public sealed class TransitionRepresentationHandler
{
    private readonly IRepresentationRepository _representations;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public TransitionRepresentationHandler(
        IRepresentationRepository representations,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _representations = representations;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        TransitionRepresentationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Domain.Representations.Representation representation =
            await _representations.FindAsync(command.OrganizationId, command.RepresentationId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Domain.Representations.Representation),
                command.RepresentationId.ToString());

        representation.RequireVersion(command.ExpectedVersion);

        RepresentationStatus from = representation.Status;

        representation.TransitionTo(
            command.Status,
            command.OccurredOn,
            actor,
            _clock.UtcNow,
            command.Reason);

        _audit.Record(
            AuditAction.RepresentationStatusChanged,
            entityType: nameof(Domain.Representations.Representation),
            entityId: representation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new
            {
                From = from.ToString(),
                To = representation.Status.ToString(),
                command.OccurredOn,
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Adds or ends a representation scope.</summary>
public sealed class ChangeRepresentationScopeHandler
{
    private readonly IRepresentationRepository _representations;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeRepresentationScopeHandler(
        IRepresentationRepository representations,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _representations = representations;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        ChangeRepresentationScopeCommand command,
        bool add,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Domain.Representations.Representation representation =
            await _representations.FindAsync(command.OrganizationId, command.RepresentationId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Domain.Representations.Representation),
                command.RepresentationId.ToString());

        representation.RequireVersion(command.ExpectedVersion);

        if (add)
        {
            representation.AddScope(command.Area, command.OccurredOn, _clock.UtcNow);
        }
        else
        {
            representation.EndScope(command.Area, command.OccurredOn, _clock.UtcNow);
        }

        _audit.Record(
            add ? AuditAction.RepresentationScopeAdded : AuditAction.RepresentationScopeEnded,
            entityType: nameof(Domain.Representations.Representation),
            entityId: representation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new { Area = command.Area.ToString(), command.OccurredOn });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Assigns somebody internal to a representation team, or changes their role.
/// </summary>
/// <remarks>
/// Assigning the lead role is how the primary representative changes: the previous
/// lead's assignment ends and a new one begins, so the record shows both who leads
/// now and who led before.
/// </remarks>
public sealed class AssignRepresentationTeamMemberHandler
{
    private readonly IRepresentationRepository _representations;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AssignRepresentationTeamMemberHandler(
        IRepresentationRepository representations,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _representations = representations;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        AssignRepresentationTeamMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        // The team is internal staff. Assigning somebody who does not work for this
        // tenant would be a cross-tenant reference with a friendly name on it.
        await CreateProspectHandler.RequireTenantMemberAsync(
            _memberships,
            command.OrganizationId,
            command.UserId,
            cancellationToken).ConfigureAwait(false);

        Domain.Representations.Representation representation =
            await _representations.FindAsync(command.OrganizationId, command.RepresentationId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Domain.Representations.Representation),
                command.RepresentationId.ToString());

        representation.RequireVersion(command.ExpectedVersion);

        representation.AssignTeamMember(command.UserId, command.Role, command.OccurredOn, _clock.UtcNow);

        _audit.Record(
            AuditAction.RepresentationTeamAssigned,
            entityType: nameof(Domain.Representations.Representation),
            entityId: representation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new
            {
                User = command.UserId.ToString(),
                Role = command.Role.ToString(),
                command.OccurredOn,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Takes somebody off a representation team, keeping the record that they were on it.</summary>
public sealed class RemoveRepresentationTeamMemberHandler
{
    private readonly IRepresentationRepository _representations;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveRepresentationTeamMemberHandler(
        IRepresentationRepository representations,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _representations = representations;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        RemoveRepresentationTeamMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.RepresentationWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Domain.Representations.Representation representation =
            await _representations.FindAsync(command.OrganizationId, command.RepresentationId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Domain.Representations.Representation),
                command.RepresentationId.ToString());

        representation.RequireVersion(command.ExpectedVersion);

        representation.RemoveTeamMember(command.UserId, command.OccurredOn, _clock.UtcNow);

        _audit.Record(
            AuditAction.RepresentationTeamRemoved,
            entityType: nameof(Domain.Representations.Representation),
            entityId: representation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RepresentationWrite,
            semanticDelta: new { User = command.UserId.ToString(), command.OccurredOn });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
