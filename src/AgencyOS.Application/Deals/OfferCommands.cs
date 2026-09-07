using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Deals;

/// <summary>
/// Records offers, answers them, and keeps the negotiation thread coherent.
/// </summary>
/// <remarks>
/// <para>
/// Every write here loads the whole thread and validates it through the rules
/// kernel before saving. Checking one offer in isolation would let two standing
/// offers through whenever two people recorded counters at the same moment, which
/// is exactly when it matters (ADR-0021).
/// </para>
/// <para>
/// The deal and the offer move together, in one transaction. Recording an offer
/// moves a draft deal into negotiation and accepting one agrees its terms, and
/// there is no path that advances either alone.
/// </para>
/// </remarks>
public sealed class OfferHandler
{
    private readonly IDealRepository _deals;
    private readonly IOfferRepository _offers;
    private readonly ITaskRepository _tasks;
    private readonly IDealTaskLinkRepository _links;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OfferHandler(
        IDealRepository deals,
        IOfferRepository offers,
        ITaskRepository tasks,
        IDealTaskLinkRepository links,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _deals = deals;
        _offers = offers;
        _tasks = tasks;
        _links = links;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Records an offer that has already been made or received.</summary>
    public async Task<RecordOfferResult> HandleAsync(
        RecordOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Terms is null || command.Terms.Count == 0)
        {
            throw new DomainException("An offer must carry at least one term.");
        }

        Deal deal = await RequireDealAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false);

        RequireDealAccepts(deal);

        IReadOnlyList<Offer> thread = await _offers
            .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        Offer? answered = ResolveAnswered(thread, command.RespondsToOfferId);

        // Drafted, filled in, then recorded - the same three steps the draft path
        // takes, because terms may only be added while an offer is editable. An
        // offer written down after the call goes through the identical rules as
        // one prepared in advance.
        Offer offer = Offer.StartDraft(
            command.OrganizationId,
            deal.Id,
            command.Direction,
            NextSequence(thread),
            actor,
            _clock.UtcNow,
            answered?.Id,
            command.Summary,
            command.Notes,
            command.ExpiresAt);

        AddTerms(offer, deal.Kind, command.Terms);

        offer.Open(_clock.UtcNow, actor, offer.Version, command.CommunicatedAt);

        _offers.Add(offer);

        // Answering a standing offer supersedes it. That is what a counter does in
        // the world, and leaving the earlier one open would make two proposals
        // appear to be on the table at once.
        answered?.NoteAnsweredByCounter(_clock.UtcNow, actor, offer.Id);

        // An unanswered offer already standing is superseded too: this negotiation
        // holds one canonical thread, so the new proposal replaces whatever was on
        // the table even when the caller did not say what it answers.
        Offer? displaced = thread.FirstOrDefault(
            existing => existing.Status == OfferStatus.Open && existing.Id != answered?.Id);

        displaced?.NoteAnsweredByCounter(_clock.UtcNow, actor, offer.Id);

        deal.NoteOfferRecorded(_clock.UtcNow, actor);

        TaskItemId? followUpId = await DealSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                deal.Id,
                offer.Id,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        RequireCoherentThread(deal, [.. thread, offer]);

        _audit.Record(
            AuditAction.OfferRecorded,
            entityType: nameof(Offer),
            entityId: offer.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OffersWrite,
            semanticDelta: Describe(deal, offer, answered, followUpId));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordOfferResult(
            offer.Id, deal.Id, (answered ?? displaced)?.Id, followUpId?.Value);
    }

    /// <summary>Starts an offer as an editable draft.</summary>
    public async Task<OfferId> HandleAsync(
        DraftOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, command.DealId, cancellationToken)
            .ConfigureAwait(false);

        RequireDealAccepts(deal);

        IReadOnlyList<Offer> thread = await _offers
            .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        Offer? answered = ResolveAnswered(thread, command.RespondsToOfferId);

        if (deal.Version != command.ExpectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Deal), deal.Id.ToString(), command.ExpectedVersion, deal.Version);
        }

        Offer offer = Offer.StartDraft(
            command.OrganizationId,
            deal.Id,
            command.Direction,
            NextSequence(thread),
            actor,
            _clock.UtcNow,
            answered?.Id,
            command.Summary,
            command.Notes,
            command.ExpiresAt);

        _offers.Add(offer);

        _audit.Record(
            AuditAction.OfferDrafted,
            entityType: nameof(Offer),
            entityId: offer.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OffersWrite,
            semanticDelta: new
            {
                DealId = deal.Id.ToString(),
                Direction = offer.Direction.ToString(),
                RespondsTo = answered?.Id.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return offer.Id;
    }

    public async Task HandleAsync(
        UpdateDraftOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Offer offer = await RequireOfferAsync(command.OrganizationId, command.OfferId, cancellationToken)
            .ConfigureAwait(false);

        offer.UpdateDraft(
            _clock.UtcNow,
            command.ExpectedVersion,
            command.CommunicatedAt,
            command.Summary,
            command.Notes,
            command.ExpiresAt);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds, replaces or removes a term on a draft offer.</summary>
    /// <remarks>
    /// Refused outright once the offer has been recorded. The refusal names the
    /// alternative, because "no" without "record a new offer instead" is a dead end
    /// for somebody who has just noticed a wrong number.
    /// </remarks>
    public async Task HandleAsync(
        ChangeDraftOfferTermCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Offer offer = await RequireOfferAsync(command.OrganizationId, command.OfferId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, offer.DealId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Value is null)
        {
            offer.RemoveTerm(command.Code, _clock.UtcNow, command.ExpectedVersion);
        }
        else if (offer.Terms.Any(term => term.Code == command.Code))
        {
            offer.UpdateTerm(
                deal.Kind,
                command.Code,
                command.Value,
                _clock.UtcNow,
                command.ExpectedVersion,
                command.Label,
                command.Notes);
        }
        else
        {
            offer.AddTerm(
                deal.Kind,
                command.Code,
                command.Value,
                _clock.UtcNow,
                command.ExpectedVersion,
                command.Label,
                command.Notes);
        }

        _audit.Record(
            AuditAction.OfferTermChanged,
            entityType: nameof(Offer),
            entityId: offer.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OffersWrite,

            // The code, never the figure. Telemetry and audit both stay clear of
            // compensation values, or the log becomes the leak.
            semanticDelta: new
            {
                Term = command.Code.ToString(),
                Action = command.Value is null ? "removed" : "set",
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a draft as actually made or received, freezing its terms.</summary>
    public async Task<RecordOfferResult> HandleAsync(
        OpenOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Offer offer = await RequireOfferAsync(command.OrganizationId, command.OfferId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, offer.DealId, cancellationToken)
            .ConfigureAwait(false);

        RequireDealAccepts(deal);

        IReadOnlyList<Offer> thread = await _offers
            .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        offer.Open(_clock.UtcNow, actor, command.ExpectedVersion, command.CommunicatedAt);

        Offer? answered = offer.RespondsToOfferId is { } respondsTo
            ? thread.FirstOrDefault(existing => existing.Id == respondsTo)
            : null;

        answered?.NoteAnsweredByCounter(_clock.UtcNow, actor, offer.Id);

        Offer? displaced = thread.FirstOrDefault(
            existing => existing.Status == OfferStatus.Open
                && existing.Id != offer.Id
                && existing.Id != answered?.Id);

        displaced?.NoteAnsweredByCounter(_clock.UtcNow, actor, offer.Id);

        deal.NoteOfferRecorded(_clock.UtcNow, actor);

        TaskItemId? followUpId = await DealSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                deal.Id,
                offer.Id,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        RequireCoherentThread(deal, thread.Any(x => x.Id == offer.Id) ? thread : [.. thread, offer]);

        _audit.Record(
            AuditAction.OfferRecorded,
            entityType: nameof(Offer),
            entityId: offer.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OffersWrite,
            semanticDelta: Describe(deal, offer, answered, followUpId));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordOfferResult(
            offer.Id, deal.Id, (answered ?? displaced)?.Id, followUpId?.Value);
    }

    /// <summary>
    /// Records what happened to a standing offer.
    /// </summary>
    /// <remarks>
    /// Accepting is the only route to agreed terms, and it moves both the offer and
    /// the deal in one transaction. Rejecting, withdrawing and expiring are three
    /// different facts about three different actors, and none of them is "nobody
    /// replied" - M6 established that silence is derived and never recorded, and
    /// the same holds here.
    /// </remarks>
    public async Task<AnswerOfferResult> HandleAsync(
        AnswerOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OffersWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Offer offer = await RequireOfferAsync(command.OrganizationId, command.OfferId, cancellationToken)
            .ConfigureAwait(false);

        Deal deal = await RequireDealAsync(command.OrganizationId, offer.DealId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<Offer> thread = await _offers
            .ListForDealAsync(command.OrganizationId, deal.Id, cancellationToken)
            .ConfigureAwait(false);

        string action;

        switch (command.Answer)
        {
            case OfferAnswer.Accept:
                offer.Accept(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                deal.NoteOfferAccepted(_clock.UtcNow, actor);
                action = AuditAction.OfferAccepted;
                break;

            case OfferAnswer.Reject:
                offer.Reject(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.OfferRejected;
                break;

            case OfferAnswer.Withdraw:
                offer.Withdraw(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.OfferWithdrawn;
                break;

            case OfferAnswer.Expire:
                offer.Expire(_clock.UtcNow, actor, command.ExpectedVersion, command.Reason);
                action = AuditAction.OfferExpired;
                break;

            default:
                throw new DomainException($"Unknown offer answer '{command.Answer}'.");
        }

        TaskItemId? followUpId = await DealSupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                deal.Id,
                offer.Id,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        RequireCoherentThread(deal, thread);

        _audit.Record(
            action,
            entityType: nameof(Offer),
            entityId: offer.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OffersWrite,
            semanticDelta: new
            {
                DealId = deal.Id.ToString(),
                DealStatus = deal.Status.ToString(),
                OfferStatus = offer.Status.ToString(),
                command.Reason,
                FollowUpTaskId = followUpId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AnswerOfferResult(offer.Id, deal.Status, followUpId?.Value);
    }

    /// <summary>Fills in a brand-new draft's terms.</summary>
    /// <remarks>
    /// The offer's own version guards each addition. The caller's expected version
    /// is the deal's, and that is checked when the deal advances.
    /// </remarks>
    private void AddTerms(Offer offer, DealKind kind, IReadOnlyList<OfferTermInput> terms)
    {
        foreach (OfferTermInput term in terms)
        {
            offer.AddTerm(kind, term.Code, term.Value, _clock.UtcNow, offer.Version, term.Label, term.Notes);
        }
    }

    /// <summary>Validates the whole thread through the rules kernel.</summary>
    /// <remarks>
    /// Belt and braces with the partial unique indexes, and deliberately so. The
    /// index is what wins a race between two transactions; this is what produces a
    /// sentence the user can act on when a single transaction would have written
    /// something incoherent.
    /// </remarks>
    private static void RequireCoherentThread(Deal deal, IReadOnlyList<Offer> offers)
    {
        OfferNode[] nodes = [.. offers.Select(offer => offer.ToRulesNode())];

        if (DealRules.DescribeChainProblem((int)deal.Status, nodes) is { } problem)
        {
            throw new DomainException(problem);
        }
    }

    private static int NextSequence(IReadOnlyList<Offer> thread) =>
        DealRules.NextSequence([.. thread.Select(offer => offer.ToRulesNode())]);

    private static Offer? ResolveAnswered(IReadOnlyList<Offer> thread, OfferId? respondsToOfferId)
    {
        if (respondsToOfferId is not { } respondsTo)
        {
            return null;
        }

        Offer answered = thread.FirstOrDefault(offer => offer.Id == respondsTo)
            ?? throw new DomainException(
                "The offer being answered is not part of this negotiation.");

        if (!DealRules.MayBeAnswered(answered.ToRulesNode()))
        {
            throw new DomainException(
                answered.Status == OfferStatus.Accepted
                    ? "That offer has already been accepted. Reopen the negotiation before countering it."
                    : $"That offer was {answered.Status.ToString().ToLowerInvariant()}, so it cannot be answered.");
        }

        return answered;
    }

    private static void RequireDealAccepts(Deal deal)
    {
        if (!deal.AcceptsOfferActivity)
        {
            throw new DomainException(
                deal.Status == DealStatus.TermsAgreed
                    ? "This deal's terms are agreed. Reopen the negotiation before recording another offer."
                    : $"This deal is {deal.Status.ToString().ToLowerInvariant()}, so offers cannot be recorded against it.");
        }
    }

    private static object Describe(Deal deal, Offer offer, Offer? answered, TaskItemId? followUpId) =>
        new
        {
            DealId = deal.Id.ToString(),
            DealStatus = deal.Status.ToString(),
            Direction = offer.Direction.ToString(),
            offer.Sequence,
            RespondsTo = answered?.Id.ToString(),

            // The number of terms, never their values. An audit trail that carried
            // compensation would be a second copy of the economics with none of
            // the permissions that guard the first.
            TermCount = offer.Terms.Count,
            FollowUpTaskId = followUpId?.ToString(),
        };

    private async Task<Deal> RequireDealAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken) =>
        await _deals.FindAsync(organizationId, dealId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Deal), dealId.ToString());

    private async Task<Offer> RequireOfferAsync(
        OrganizationId organizationId,
        OfferId offerId,
        CancellationToken cancellationToken) =>
        await _offers.FindAsync(organizationId, offerId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Offer), offerId.ToString());
}
