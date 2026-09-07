using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Deals;

// -------------------------------------------------------------------- commands

/// <param name="OpportunityId">The pursuit the negotiation comes out of. Required.</param>
/// <param name="OpportunityTargetId">The market conversation it comes out of. Required.</param>
public sealed record CreateDealCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    OpportunityTargetId OpportunityTargetId,
    string Name,
    DealKind Kind,
    UserId OwnerUserId,
    DateOnly OpenedOn,
    string? Reference,
    string? Summary,
    string? StrategyNotes);

/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateDealMetadataCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    string Name,
    DealKind Kind,
    UserId OwnerUserId,
    string? Reference,
    string? Summary,
    string? StrategyNotes,
    int ExpectedVersion);

/// <summary>
/// Closes a negotiation, or calls it off.
/// </summary>
/// <remarks>
/// Deliberately cannot reach <see cref="DealStatus.TermsAgreed"/> or
/// <see cref="DealStatus.Negotiating"/>. Those follow from recording and accepting
/// offers, and a status command that could set them would let a deal claim agreed
/// terms with nothing behind it (ADR-0021).
/// </remarks>
public sealed record CloseDealCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    DealStatus Status,
    DateOnly ClosedOn,
    string? Reason,
    int ExpectedVersion);

/// <summary>
/// Reopens a negotiation, unwinding an acceptance or a closure.
/// </summary>
/// <remarks>
/// When it unwinds an acceptance the accepted offer is superseded in the same
/// transaction. Its terms are not touched: what was agreed on the day stays
/// exactly what it was, and the history says it was later unwound.
/// </remarks>
public sealed record ReopenNegotiationCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    string? Reason,
    int ExpectedVersion);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="AssignedTo">Who should do it. Defaults to the caller.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record DealFollowUpInput(
    string Title,
    DateTimeOffset? DueAt = null,
    UserId? AssignedTo = null,
    string? Notes = null);

/// <param name="Code">The term, from the controlled vocabulary.</param>
/// <param name="Value">Its value. Exactly one shape, matching the catalog.</param>
/// <param name="Label">A display override, when the catalog name is not enough.</param>
/// <param name="Notes">Context that is not part of the value.</param>
public sealed record OfferTermInput(
    DealTermCode Code,
    DealTermValue Value,
    string? Label = null,
    string? Notes = null);

/// <summary>
/// Records an offer that has already been made or received.
/// </summary>
/// <remarks>
/// One command for inbound, outbound and counters. The direction says which side
/// proposed the terms and <see cref="RespondsToOfferId"/> says what it answers;
/// three near-identical commands would be three idempotency surfaces and three
/// places for the chain rules to be applied slightly differently. The Windows
/// surface still offers them as separate actions, which is where that distinction
/// belongs (ADR-0021).
/// </remarks>
/// <param name="ExpectedVersion">The deal's version. Required.</param>
public sealed record RecordOfferCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    OfferDirection Direction,
    IReadOnlyList<OfferTermInput> Terms,
    int ExpectedVersion,
    OfferId? RespondsToOfferId = null,
    DateTimeOffset? CommunicatedAt = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null,
    DealFollowUpInput? FollowUp = null);

/// <summary>Starts an offer as an editable draft.</summary>
/// <param name="ExpectedVersion">The deal's version. Required.</param>
public sealed record DraftOfferCommand(
    OrganizationId OrganizationId,
    DealId DealId,
    OfferDirection Direction,
    int ExpectedVersion,
    OfferId? RespondsToOfferId = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The <em>offer's</em> version, not the deal's.</param>
public sealed record UpdateDraftOfferCommand(
    OrganizationId OrganizationId,
    OfferId OfferId,
    int ExpectedVersion,
    DateTimeOffset? CommunicatedAt = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null);

/// <summary>Adds, replaces or removes a term on a draft offer.</summary>
/// <param name="Value">Null removes the term.</param>
/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record ChangeDraftOfferTermCommand(
    OrganizationId OrganizationId,
    OfferId OfferId,
    DealTermCode Code,
    DealTermValue? Value,
    int ExpectedVersion,
    string? Label = null,
    string? Notes = null);

/// <summary>Records a draft as actually made or received, freezing its terms.</summary>
/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record OpenOfferCommand(
    OrganizationId OrganizationId,
    OfferId OfferId,
    int ExpectedVersion,
    DateTimeOffset? CommunicatedAt = null,
    DealFollowUpInput? FollowUp = null);

/// <summary>What answer is being recorded against a standing offer.</summary>
public enum OfferAnswer
{
    /// <summary>The receiving side accepted it. Agrees the deal's terms.</summary>
    Accept = 1,

    /// <summary>The receiving side rejected it.</summary>
    Reject = 2,

    /// <summary>The proposing side pulled it.</summary>
    Withdraw = 3,

    /// <summary>A stated expiration passed. Never inferred from a clock.</summary>
    Expire = 4,
}

/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record AnswerOfferCommand(
    OrganizationId OrganizationId,
    OfferId OfferId,
    OfferAnswer Answer,
    int ExpectedVersion,
    string? Reason = null,
    DealFollowUpInput? FollowUp = null);

/// <param name="OfferId">The offer recorded.</param>
/// <param name="DealId">The negotiation it belongs to.</param>
/// <param name="SupersededOfferId">The offer it answered, when it answered one.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordOfferResult(
    OfferId OfferId,
    DealId DealId,
    OfferId? SupersededOfferId,
    Guid? FollowUpTaskId);

/// <param name="OfferId">The offer answered.</param>
/// <param name="DealStatus">Where the negotiation stands afterwards.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record AnswerOfferResult(
    OfferId OfferId,
    DealStatus DealStatus,
    Guid? FollowUpTaskId);

// -------------------------------------------------------------------- handlers

/// <summary>Opens and maintains negotiations.</summary>
public sealed class DealHandler
{
    private readonly IDealRepository _deals;
    private readonly IOfferRepository _offers;
    private readonly IOpportunityRepository _opportunities;
    private readonly IOpportunityTargetRepository _targets;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public DealHandler(
        IDealRepository deals,
        IOfferRepository offers,
        IOpportunityRepository opportunities,
        IOpportunityTargetRepository targets,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _deals = deals;
        _offers = offers;
        _opportunities = opportunities;
        _targets = targets;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Opens a negotiation against a target that has reached commercial discussion.
    /// </summary>
    /// <remarks>
    /// Opening is always explicit. Nothing creates a deal because a target became
    /// interested: "we are negotiating" is a claim about the world, and a pipeline
    /// stage change cannot make it true (ADR-0021).
    /// </remarks>
    public async Task<DealId> HandleAsync(
        CreateDealCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.DealsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireOpportunityAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        OpportunityTarget target = await RequireTargetAsync(
            command.OrganizationId, command.OpportunityTargetId, cancellationToken).ConfigureAwait(false);

        // The lineage has to hold: a target from another pursuit would give the
        // deal two different answers to what it is about. The database enforces
        // this too, through a composite foreign key on the triple.
        if (target.OpportunityId != opportunity.Id)
        {
            throw new DomainException(
                "That target belongs to a different opportunity, so it cannot anchor this negotiation.");
        }

        if (!opportunity.AcceptsMarketActivity)
        {
            throw new DomainException(
                $"The pursuit is {opportunity.Status.ToString().ToLowerInvariant()}, "
                + "so a negotiation cannot be opened from it.");
        }

        if (!Deal.OpenableFromStages.Contains(target.Stage))
        {
            throw new DomainException(
                $"A negotiation can only be opened once a target reaches "
                + $"{string.Join(" or ", Deal.OpenableFromStages)}; this one is {target.Stage}.");
        }

        Deal? live = await _deals
            .FindLiveForTargetAsync(command.OrganizationId, target.Id, command.Kind, cancellationToken)
            .ConfigureAwait(false);

        if (live is not null)
        {
            throw new DomainException(
                $"A {command.Kind} negotiation with this target is already open. "
                + "Close or cancel it before opening another.");
        }

        Deal deal = Deal.Open(
            command.OrganizationId,
            opportunity.Id,
            target.Id,
            command.Name,
            command.Kind,
            command.OwnerUserId,
            command.OpenedOn,
            actor,
            _clock.UtcNow,
            command.Reference,
            command.Summary,
            command.StrategyNotes);

        _deals.Add(deal);

        _audit.Record(
            AuditAction.DealOpened,
            entityType: nameof(Deal),
            entityId: deal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DealsWrite,
            semanticDelta: new
            {
                deal.Name,
                Kind = deal.Kind.ToString(),
                OpportunityId = opportunity.Id.ToString(),
                TargetId = target.Id.ToString(),
                Owner = command.OwnerUserId.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return deal.Id;
    }

    public async Task HandleAsync(
        UpdateDealMetadataCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.DealsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false);

        deal.UpdateMetadata(
            command.Name,
            command.Kind,
            command.OwnerUserId,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Reference,
            command.Summary,
            command.StrategyNotes);

        _audit.Record(
            AuditAction.DealUpdated,
            entityType: nameof(Deal),
            entityId: deal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DealsWrite,
            semanticDelta: new { deal.Name, Kind = deal.Kind.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes a negotiation without agreement, or calls it off.</summary>
    public async Task HandleAsync(
        CloseDealCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.DealsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false);

        switch (command.Status)
        {
            case DealStatus.NoDeal:
                deal.CloseWithoutAgreement(
                    command.ClosedOn, _clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                break;

            case DealStatus.Cancelled:
                deal.Cancel(
                    command.ClosedOn, _clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                break;

            default:
                throw new DomainException(
                    $"'{command.Status}' is not a status a caller may set. "
                    + "A negotiation reaches it by recording or accepting an offer.");
        }

        // A closed negotiation leaves nothing standing. The offer that was on the
        // table was neither rejected nor withdrawn, so it is superseded by the
        // closure rather than given an answer nobody gave.
        await WithdrawStandingOfferAsync(deal, actor, command.Reason, cancellationToken)
            .ConfigureAwait(false);

        _audit.Record(
            AuditAction.DealStatusChanged,
            entityType: nameof(Deal),
            entityId: deal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DealsWrite,
            semanticDelta: new
            {
                Status = deal.Status.ToString(),
                ClosedOn = deal.ClosedOn?.ToString("O"),
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reopens a negotiation, superseding the accepted offer when there is one.
    /// </summary>
    /// <remarks>
    /// The accepted offer's terms are never edited. It is marked superseded and
    /// the timeline records both the acceptance and the unwinding, so the answer
    /// to "what did we agree in March" survives a renegotiation in April.
    /// </remarks>
    public async Task HandleAsync(
        ReopenNegotiationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.DealsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false);

        bool unwindingAgreement = deal.Status == DealStatus.TermsAgreed;

        deal.Reopen(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);

        if (unwindingAgreement)
        {
            IReadOnlyList<Offer> offers = await _offers
                .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
                .ConfigureAwait(false);

            Offer accepted = offers.FirstOrDefault(offer => offer.Status == OfferStatus.Accepted)
                ?? throw new DomainException(
                    "This deal says its terms are agreed but no offer records the agreement. "
                    + "It cannot be reopened until that is corrected.");

            accepted.NoteUnwoundByReopen(_clock.UtcNow, actor, command.Reason);
        }

        _audit.Record(
            AuditAction.DealReopened,
            entityType: nameof(Deal),
            entityId: deal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DealsWrite,
            semanticDelta: new { UnwoundAgreement = unwindingAgreement, command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WithdrawStandingOfferAsync(
        Deal deal,
        UserId actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Offer> offers = await _offers
            .ListForDealAsync(deal.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        Offer? standing = offers.FirstOrDefault(offer => offer.Status == OfferStatus.Open);

        standing?.Withdraw(_clock.UtcNow, actor, standing.Version, reason);
    }

    private async Task<Deal> RequireDealAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken) =>
        await _deals.FindAsync(organizationId, dealId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Deal), dealId.ToString());

    private async Task<Opportunity> RequireOpportunityAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken) =>
        await _opportunities.FindAsync(organizationId, opportunityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Opportunity), opportunityId.ToString());

    private async Task<OpportunityTarget> RequireTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId targetId,
        CancellationToken cancellationToken) =>
        await _targets.FindAsync(organizationId, targetId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(OpportunityTarget), targetId.ToString());
}

/// <summary>Shared helpers for the offer commands.</summary>
internal static class DealSupport
{
    /// <summary>Creates a follow-up task and records which negotiation it belongs to.</summary>
    internal static async Task<TaskItemId?> AddFollowUpAsync(
        ITaskRepository tasks,
        IDealTaskLinkRepository links,
        IMembershipRepository memberships,
        OrganizationId organizationId,
        DealId dealId,
        OfferId? offerId,
        DealFollowUpInput? followUp,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
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

        links.Add(DealTaskLink.Create(organizationId, task.Id, dealId, offerId, now));

        return task.Id;
    }
}
