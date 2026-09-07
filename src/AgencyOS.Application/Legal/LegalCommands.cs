using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Legal;

// -------------------------------------------------------------------- commands

/// <param name="AnchorDate">
/// The date the deadline hangs off, when it is already known. Omitted, the
/// deadline stays unresolved rather than being invented.
/// </param>
public sealed record RecordRightsGrantCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    Guid GrantorPartyId,
    Guid GranteePartyId,
    RightType RightType,
    RightsMedium Medium,
    RightsTerritory Territory,
    GrantExclusivity Exclusivity,
    GrantPeriodKind PeriodKind,
    DateOnly? StartsOn = null,
    DateOnly? EndsOn = null,
    string? TerritoryDetail = null,
    string? ClauseReference = null,
    Guid? SourcePropertyId = null,
    Guid? ProjectId = null,
    string? Reservations = null,
    string? Notes = null,

    /// <summary>The grant this one replaces, when an amendment changed it.</summary>
    RightsGrantId? SupersedesGrantId = null,

    int? SupersededExpectedVersion = null);

/// <param name="EndedOn">The day the grant stopped running.</param>
public sealed record EndRightsGrantCommand(
    OrganizationId OrganizationId,
    RightsGrantId RightsGrantId,
    DateOnly EndedOn,
    int ExpectedVersion);

public sealed record RecordOptionCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    OptionKind Kind,
    Guid HolderPartyId,
    string Subject,
    DeadlineRule Deadline,
    DateOnly? AnchorDate = null,
    DateOnly? WindowOpensOn = null,
    string? ClauseReference = null,
    string? ExerciseMethod = null,
    Guid? ProjectId = null,
    Guid? SourcePropertyId = null,
    ContractTermId? EconomicsTermId = null,
    string? Notes = null);

/// <summary>What is being recorded about an option.</summary>
public enum OptionOutcome
{
    Exercised = 1,
    Declined = 2,
    Waived = 3,

    /// <summary>Its stated deadline passed. Never inferred from a clock.</summary>
    Expired = 4,

    Cancelled = 5,
}

/// <param name="OccurredOn">
/// The day it happened. Not needed for a cancellation or an expiry, which takes
/// the deadline's own date.
/// </param>
public sealed record ResolveOptionCommand(
    OrganizationId OrganizationId,
    ContractOptionId ContractOptionId,
    OptionOutcome Outcome,
    int ExpectedVersion,
    DateOnly? OccurredOn = null,
    string? Reason = null,
    LegalFollowUpInput? FollowUp = null);

public sealed record RecordObligationCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    Guid ObligorPartyId,
    Guid ObligeePartyId,
    ObligationKind Kind,
    string Description,
    DeadlineRule Due,
    DateOnly? AnchorDate = null,
    string? ClauseReference = null,
    ContractOptionId? RelatedOptionId = null,
    RightsGrantId? RelatedRightsGrantId = null,
    string? Notes = null,
    PrivilegeClass Privilege = PrivilegeClass.Ordinary);

/// <summary>What is being recorded about an obligation.</summary>
public enum ObligationOutcome
{
    Satisfied = 1,
    Waived = 2,

    /// <summary>A determination somebody made. Never inferred from a passed date.</summary>
    Breached = 3,

    Cancelled = 4,

    /// <summary>A breach determination reversed, or a waiver withdrawn.</summary>
    Reinstated = 5,
}

public sealed record ResolveObligationCommand(
    OrganizationId OrganizationId,
    ObligationId ObligationId,
    ObligationOutcome Outcome,
    int ExpectedVersion,
    DateOnly? OccurredOn = null,
    string? Reason = null,
    LegalFollowUpInput? FollowUp = null);

public sealed record RecordNoticeRequirementCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    Guid ObligorPartyId,
    Guid RecipientPartyId,
    string Description,
    DeadlineRule Due,
    NoticeMethod Method,
    DateOnly? AnchorDate = null,
    string? ClauseReference = null,
    string? AddressReference = null,
    ContractOptionId? RelatedOptionId = null,
    ObligationId? RelatedObligationId = null,
    string? Notes = null);

/// <summary>
/// Records that a notice was given or received.
/// </summary>
/// <remarks>
/// AgencyOS does not send notices. This is an assertion that one passed between
/// the parties, exactly as an M6 submission is an assertion that material went out.
/// </remarks>
public sealed record RecordNoticeCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    NoticeDirection Direction,
    Guid SenderPartyId,
    Guid RecipientPartyId,
    DateOnly OccurredOn,
    NoticeMethod Method,
    NoticeRequirementId? NoticeRequirementId = null,
    string? ExternalReference = null,
    string? Summary = null,
    string? Notes = null);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="AssignedTo">Who should do it. Defaults to the caller.</param>
public sealed record LegalFollowUpInput(
    string Title,
    DateTimeOffset? DueAt = null,
    UserId? AssignedTo = null,
    string? Notes = null);

/// <summary>Creates a task to manage an obligation or an option.</summary>
/// <remarks>
/// Explicit, never automatic. Turning every obligation into a task would fill the
/// list with things nobody has to do this week (ADR-0022).
/// </remarks>
public sealed record CreateContractTaskCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    LegalFollowUpInput FollowUp,
    ObligationId? ObligationId = null,
    ContractOptionId? ContractOptionId = null);

// -------------------------------------------------------------------- handlers

/// <summary>Records rights grants and their supersession.</summary>
public sealed class RightsHandler
{
    private readonly IContractRepository _contracts;
    private readonly IContractVersionRepository _versions;
    private readonly IRightsGrantRepository _grants;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RightsHandler(
        IContractRepository contracts,
        IContractVersionRepository versions,
        IRightsGrantRepository grants,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _versions = versions;
        _grants = grants;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records a grant, optionally superseding the one an amendment changed.
    /// </summary>
    /// <remarks>
    /// Supersession rather than overwrite. A grant that ran worldwide and became
    /// United States only is two rows, each pointing at the instrument that created
    /// it, so "what did we believe we had before the amendment" stays answerable
    /// (ADR-0022).
    /// </remarks>
    public async Task<RightsGrantId> HandleAsync(
        RecordRightsGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RightsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        LegalSupport.RequireParty(contract, command.GrantorPartyId, "grantor");
        LegalSupport.RequireParty(contract, command.GranteePartyId, "grantee");

        await LegalSupport
            .RequireVersionAsync(
                _versions, command.OrganizationId, contract.Id, command.ContractVersionId, cancellationToken)
            .ConfigureAwait(false);

        RightsGrant grant = RightsGrant.Record(
            command.OrganizationId,
            contract.Id,
            command.ContractVersionId,
            command.GrantorPartyId,
            command.GranteePartyId,
            command.RightType,
            command.Medium,
            command.Territory,
            command.Exclusivity,
            command.PeriodKind,
            actor,
            _clock.UtcNow,
            command.StartsOn,
            command.EndsOn,
            command.TerritoryDetail,
            command.ClauseReference,
            command.SourcePropertyId is { } property ? new SourcePropertyId(property) : null,
            command.ProjectId is { } project ? new ProjectId(project) : null,
            command.Reservations,
            command.Notes);

        _grants.Add(grant);

        if (command.SupersedesGrantId is { } supersededId)
        {
            RightsGrant superseded = await _grants
                .FindAsync(command.OrganizationId, supersededId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(RightsGrant), supersededId.ToString());

            superseded.Supersede(
                grant.Id,
                _clock.UtcNow,
                command.SupersededExpectedVersion ?? superseded.Version);

            _audit.Record(
                AuditAction.RightsGrantSuperseded,
                entityType: nameof(RightsGrant),
                entityId: superseded.Id.ToString(),
                organizationId: command.OrganizationId,
                permission: Permission.RightsWrite,
                semanticDelta: new { SupersededBy = grant.Id.ToString() });
        }

        _audit.Record(
            AuditAction.RightsGrantRecorded,
            entityType: nameof(RightsGrant),
            entityId: grant.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RightsWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                RightType = grant.RightType.ToString(),
                Medium = grant.Medium.ToString(),
                Territory = grant.Territory.ToString(),
                Exclusivity = grant.Exclusivity.ToString(),
                Supersedes = command.SupersedesGrantId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return grant.Id;
    }

    /// <summary>Records that a grant stopped running.</summary>
    public async Task HandleAsync(
        EndRightsGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.RightsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        RightsGrant grant = await _grants
            .FindAsync(command.OrganizationId, command.RightsGrantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(RightsGrant), command.RightsGrantId.ToString());

        grant.End(command.EndedOn, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.RightsGrantEnded,
            entityType: nameof(RightsGrant),
            entityId: grant.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RightsWrite,
            semanticDelta: new { EndedOn = command.EndedOn.ToString("O") });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records options and what became of them.</summary>
public sealed class OptionHandler
{
    private readonly IContractRepository _contracts;
    private readonly IContractVersionRepository _versions;
    private readonly IContractOptionRepository _options;
    private readonly ITaskRepository _tasks;
    private readonly IContractTaskLinkRepository _links;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OptionHandler(
        IContractRepository contracts,
        IContractVersionRepository versions,
        IContractOptionRepository options,
        ITaskRepository tasks,
        IContractTaskLinkRepository links,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _versions = versions;
        _options = options;
        _tasks = tasks;
        _links = links;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ContractOptionId> HandleAsync(
        RecordOptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RightsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        LegalSupport.RequireParty(contract, command.HolderPartyId, "holder");

        await LegalSupport
            .RequireVersionAsync(
                _versions, command.OrganizationId, contract.Id, command.ContractVersionId, cancellationToken)
            .ConfigureAwait(false);

        // The anchor the caller supplied, or the contract's own dates when the rule
        // hangs off execution or effectiveness and those are already known.
        DateOnly? anchor = command.AnchorDate ?? LegalSupport.AnchorFrom(contract, command.Deadline);

        ContractOption option = ContractOption.Record(
            command.OrganizationId,
            contract.Id,
            command.ContractVersionId,
            command.Kind,
            command.HolderPartyId,
            command.Subject,
            command.Deadline,
            actor,
            _clock.UtcNow,
            anchor,
            command.WindowOpensOn,
            command.ClauseReference,
            command.ExerciseMethod,
            command.ProjectId is { } project ? new ProjectId(project) : null,
            command.SourcePropertyId is { } property ? new SourcePropertyId(property) : null,
            command.EconomicsTermId,
            command.Notes);

        _options.Add(option);

        _audit.Record(
            AuditAction.OptionRecorded,
            entityType: nameof(ContractOption),
            entityId: option.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RightsWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                Kind = option.Kind.ToString(),
                DeadlineResolved = option.ResolvedDeadlineOn is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return option.Id;
    }

    /// <summary>Records the holder's election, or that the option lapsed.</summary>
    public async Task<OptionStatus> HandleAsync(
        ResolveOptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.RightsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractOption option = await _options
            .FindAsync(command.OrganizationId, command.ContractOptionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(ContractOption), command.ContractOptionId.ToString());

        string action;

        switch (command.Outcome)
        {
            case OptionOutcome.Exercised:
                option.Exercise(
                    RequireDate(command, "exercised"),
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason);
                action = AuditAction.OptionExercised;
                break;

            case OptionOutcome.Declined:
                option.Decline(
                    RequireDate(command, "declined"),
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason);
                action = AuditAction.OptionDeclined;
                break;

            case OptionOutcome.Waived:
                option.Waive(
                    RequireDate(command, "waived"),
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason);
                action = AuditAction.OptionWaived;
                break;

            case OptionOutcome.Expired:
                option.RecordExpiry(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.OptionExpired;
                break;

            case OptionOutcome.Cancelled:
                option.Cancel(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.OptionCancelled;
                break;

            default:
                throw new DomainException($"Unknown option outcome '{command.Outcome}'.");
        }

        await LegalSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                option.ContractId,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken,
                optionId: option.Id)
            .ConfigureAwait(false);

        _audit.Record(
            action,
            entityType: nameof(ContractOption),
            entityId: option.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.RightsWrite,
            semanticDelta: new
            {
                Status = option.Status.ToString(),
                ResolvedOn = option.ResolvedOn?.ToString("O"),
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return option.Status;
    }

    private static DateOnly RequireDate(ResolveOptionCommand command, string outcome) =>
        command.OccurredOn
        ?? throw new DomainException($"Recording an option as {outcome} needs the date it happened.");
}

/// <summary>Records obligations, notices and what became of them.</summary>
public sealed class ObligationHandler
{
    private readonly IContractRepository _contracts;
    private readonly IContractVersionRepository _versions;
    private readonly IObligationRepository _obligations;
    private readonly INoticeRepository _notices;
    private readonly ITaskRepository _tasks;
    private readonly IContractTaskLinkRepository _links;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ObligationHandler(
        IContractRepository contracts,
        IContractVersionRepository versions,
        IObligationRepository obligations,
        INoticeRepository notices,
        ITaskRepository tasks,
        IContractTaskLinkRepository links,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _versions = versions;
        _obligations = obligations;
        _notices = notices;
        _tasks = tasks;
        _links = links;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ObligationId> HandleAsync(
        RecordObligationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ObligationsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        LegalSupport.RequireParty(contract, command.ObligorPartyId, "obligor");
        LegalSupport.RequireParty(contract, command.ObligeePartyId, "obligee");

        await LegalSupport
            .RequireVersionAsync(
                _versions, command.OrganizationId, contract.Id, command.ContractVersionId, cancellationToken)
            .ConfigureAwait(false);

        DateOnly? anchor = command.AnchorDate ?? LegalSupport.AnchorFrom(contract, command.Due);

        Obligation obligation = Obligation.Record(
            command.OrganizationId,
            contract.Id,
            command.ContractVersionId,
            command.ObligorPartyId,
            command.ObligeePartyId,
            command.Kind,
            command.Description,
            command.Due,
            actor,
            _clock.UtcNow,
            anchor,
            command.ClauseReference,
            command.RelatedOptionId,
            command.RelatedRightsGrantId,
            command.Notes,
            command.Privilege);

        _obligations.Add(obligation);

        _audit.Record(
            AuditAction.ObligationRecorded,
            entityType: nameof(Obligation),
            entityId: obligation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ObligationsWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                Kind = obligation.Kind.ToString(),
                DueResolved = obligation.ResolvedDueOn is not null,
                Privilege = obligation.Privilege.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return obligation.Id;
    }

    /// <summary>Records what became of an obligation.</summary>
    public async Task<ObligationStatus> HandleAsync(
        ResolveObligationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ObligationsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Obligation obligation = await _obligations
            .FindAsync(command.OrganizationId, command.ObligationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Obligation), command.ObligationId.ToString());

        string action;

        switch (command.Outcome)
        {
            case ObligationOutcome.Satisfied:
                obligation.Satisfy(
                    RequireDate(command, "satisfied"),
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason);
                action = AuditAction.ObligationSatisfied;
                break;

            case ObligationOutcome.Waived:
                obligation.Waive(
                    RequireDate(command, "waived"),
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason);
                action = AuditAction.ObligationWaived;
                break;

            case ObligationOutcome.Breached:
                obligation.RecordBreach(
                    _clock.UtcNow,
                    actor,
                    command.ExpectedVersion,
                    command.Reason ?? string.Empty);
                action = AuditAction.ObligationBreachRecorded;
                break;

            case ObligationOutcome.Reinstated:
                obligation.Reinstate(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.ObligationReinstated;
                break;

            case ObligationOutcome.Cancelled:
                obligation.Cancel(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.ObligationCancelled;
                break;

            default:
                throw new DomainException($"Unknown obligation outcome '{command.Outcome}'.");
        }

        await LegalSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                obligation.ContractId,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken,
                obligationId: obligation.Id)
            .ConfigureAwait(false);

        _audit.Record(
            action,
            entityType: nameof(Obligation),
            entityId: obligation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ObligationsWrite,
            semanticDelta: new
            {
                Status = obligation.Status.ToString(),
                ResolvedOn = obligation.ResolvedOn?.ToString("O"),
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return obligation.Status;
    }

    public async Task<NoticeRequirementId> HandleAsync(
        RecordNoticeRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ObligationsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        LegalSupport.RequireParty(contract, command.ObligorPartyId, "party giving notice");
        LegalSupport.RequireParty(contract, command.RecipientPartyId, "recipient");

        await LegalSupport
            .RequireVersionAsync(
                _versions, command.OrganizationId, contract.Id, command.ContractVersionId, cancellationToken)
            .ConfigureAwait(false);

        DateOnly? anchor = command.AnchorDate ?? LegalSupport.AnchorFrom(contract, command.Due);

        NoticeRequirement requirement = NoticeRequirement.Record(
            command.OrganizationId,
            contract.Id,
            command.ContractVersionId,
            command.ObligorPartyId,
            command.RecipientPartyId,
            command.Description,
            command.Due,
            command.Method,
            actor,
            _clock.UtcNow,
            anchor,
            command.ClauseReference,
            command.AddressReference,
            command.RelatedOptionId,
            command.RelatedObligationId,
            command.Notes);

        _notices.AddRequirement(requirement);

        _audit.Record(
            AuditAction.NoticeRequirementRecorded,
            entityType: nameof(NoticeRequirement),
            entityId: requirement.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ObligationsWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                Method = requirement.Method.ToString(),
                DueResolved = requirement.ResolvedDueOn is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return requirement.Id;
    }

    /// <summary>Records that a notice passed between the parties.</summary>
    /// <remarks>AgencyOS did not send it. This records that somebody says it went.</remarks>
    public async Task<Guid> HandleAsync(
        RecordNoticeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ObligationsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        LegalSupport.RequireParty(contract, command.SenderPartyId, "sender");
        LegalSupport.RequireParty(contract, command.RecipientPartyId, "recipient");

        if (command.NoticeRequirementId is { } requirementId)
        {
            NoticeRequirement requirement = await _notices
                .FindRequirementAsync(command.OrganizationId, requirementId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EntityNotFoundException(
                    nameof(NoticeRequirement), requirementId.ToString());

            if (requirement.ContractId != contract.Id)
            {
                throw new DomainException(
                    "That notice requirement belongs to a different contract.");
            }
        }

        NoticeRecord record = NoticeRecord.Record(
            command.OrganizationId,
            contract.Id,
            command.Direction,
            command.SenderPartyId,
            command.RecipientPartyId,
            command.OccurredOn,
            command.Method,
            actor,
            _clock.UtcNow,
            command.NoticeRequirementId,
            command.ExternalReference,
            command.Summary,
            command.Notes);

        _notices.AddRecord(record);

        _audit.Record(
            AuditAction.NoticeRecorded,
            entityType: nameof(NoticeRecord),
            entityId: record.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ObligationsWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                Direction = record.Direction.ToString(),
                Method = record.Method.ToString(),
                OccurredOn = record.OccurredOn.ToString("O"),
                AgainstRequirement = command.NoticeRequirementId is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return record.Id;
    }

    /// <summary>Creates a task to manage a piece of contract work.</summary>
    public async Task<Guid> HandleAsync(
        CreateContractTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ContractsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await LegalSupport
            .RequireContractAsync(_contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        TaskItemId? taskId = await LegalSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                command.ContractId,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken,
                command.ObligationId,
                command.ContractOptionId)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return taskId!.Value.Value;
    }

    private static DateOnly RequireDate(ResolveObligationCommand command, string outcome) =>
        command.OccurredOn
        ?? throw new DomainException($"Recording an obligation as {outcome} needs the date it happened.");
}

/// <summary>Shared helpers for the legal commands.</summary>
internal static class LegalSupport
{
    internal static async Task<Contract> RequireContractAsync(
        IContractRepository contracts,
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken) =>
        await contracts.FindAsync(organizationId, contractId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Contract), contractId.ToString());

    /// <summary>Confirms a version belongs to the contract it claims to.</summary>
    internal static async Task<ContractVersion> RequireVersionAsync(
        IContractVersionRepository versions,
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        CancellationToken cancellationToken)
    {
        ContractVersion version =
            await versions.FindAsync(organizationId, versionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(ContractVersion), versionId.ToString());

        if (version.ContractId != contractId)
        {
            throw new DomainException("That version belongs to a different contract.");
        }

        return version;
    }

    /// <summary>Confirms a party is on the contract before anything points at it.</summary>
    internal static void RequireParty(Contract contract, Guid partyId, string role)
    {
        if (contract.Parties.All(party => party.Id != partyId))
        {
            throw new DomainException($"The {role} is not a party to this contract.");
        }
    }

    /// <summary>
    /// The contract's own date for the event a deadline hangs off, when it has one.
    /// </summary>
    /// <remarks>
    /// Only execution and effectiveness are knowable from the contract itself.
    /// Delivery, commencement and first release are events nothing here has seen,
    /// so a deadline measured from one stays unresolved until somebody supplies the
    /// date - which is the honest answer rather than an invented deadline
    /// (ADR-0022).
    /// </remarks>
    internal static DateOnly? AnchorFrom(Contract contract, DeadlineRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Anchor switch
        {
            DeadlineAnchor.OnExecution => contract.ExecutedOn,
            DeadlineAnchor.OnEffective => contract.EffectiveOn,
            _ => null,
        };
    }

    /// <summary>Creates a task and records which contract work it manages.</summary>
    internal static async Task<TaskItemId?> AddFollowUpAsync(
        ITaskRepository tasks,
        IContractTaskLinkRepository links,
        IMembershipRepository memberships,
        OrganizationId organizationId,
        ContractId contractId,
        LegalFollowUpInput? followUp,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        ObligationId? obligationId = null,
        ContractOptionId? optionId = null)
    {
        if (followUp is null)
        {
            return null;
        }

        if (followUp.AssignedTo is { } assignee)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(memberships, organizationId, assignee, cancellationToken)
                .ConfigureAwait(false);
        }

        TaskItem task = TaskItem.Create(
            organizationId,
            followUp.Title,
            actor,
            now,
            dueAt: followUp.DueAt,
            assignedTo: followUp.AssignedTo ?? actor,
            notes: followUp.Notes);

        tasks.Add(task);

        links.Add(ContractTaskLink.Create(
            organizationId, task.Id, contractId, now, obligationId, optionId));

        return task.Id;
    }
}
