using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Deals;

/// <summary>
/// One negotiated term as a reader sees it.
/// </summary>
/// <remarks>
/// The value fields are optional and exactly one group is populated, matching the
/// stored row. <see cref="DisplayValue"/> is a rendering of the same value for
/// screens that only show it; it is never the canonical form, and nothing parses
/// it back (ADR-0021).
/// </remarks>
/// <param name="Code">The controlled term code.</param>
/// <param name="DisplayName">What the catalog calls it, or the offer's own label.</param>
/// <param name="ValueKind">Which shape the value takes.</param>
/// <param name="IsEconomic">Whether reading it required <c>deals.economics.read</c>.</param>
/// <param name="Amount">Money amount.</param>
/// <param name="Currency">Currency of the amount.</param>
/// <param name="Number">Percentage or decimal value.</param>
/// <param name="Whole">Integer, duration length or count quantity.</param>
/// <param name="Text">Text value.</param>
/// <param name="Flag">Boolean value.</param>
/// <param name="Date">Business date value.</param>
/// <param name="Unit">Unit for durations and counts.</param>
/// <param name="DisplayValue">A rendering, for display only.</param>
/// <param name="Sequence">Display order within the offer.</param>
/// <param name="Notes">Context that is not part of the value.</param>
public sealed record OfferTermModel(
    DealTermCode Code,
    string DisplayName,
    TermValueKind ValueKind,
    bool IsEconomic,
    decimal? Amount,
    string? Currency,
    decimal? Number,
    long? Whole,
    string? Text,
    bool? Flag,
    DateOnly? Date,
    TermUnit? Unit,
    string DisplayValue,
    int Sequence,
    string? Notes);

/// <summary>One offer in a negotiation.</summary>
/// <param name="Id">Offer identifier.</param>
/// <param name="DealId">The negotiation it belongs to.</param>
/// <param name="Direction">Which side proposed the terms.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Sequence">Position in the thread.</param>
/// <param name="RespondsToOfferId">The offer it answers, when it answers one.</param>
/// <param name="ResponseKind">Counter or revision, derived from the two sides.</param>
/// <param name="RecordedAt">When AgencyOS was told.</param>
/// <param name="CommunicatedAt">When it was actually made or received.</param>
/// <param name="RecordedByUserId">Who wrote it down.</param>
/// <param name="RecordedByDisplayName">Their name.</param>
/// <param name="Summary">A short factual description.</param>
/// <param name="Notes">Narrative context.</param>
/// <param name="ExpiresAt">When it was stated to lapse, if a lapse was stated.</param>
/// <param name="Terms">
/// The commercial snapshot. Economic terms are absent without
/// <c>deals.economics.read</c>, and absent is indistinguishable from empty.
/// </param>
/// <param name="HasRedactedTerms">
/// Always false on the wire. Present so the redaction decision has one name in the
/// code; the API never sets it, because saying that something was withheld leaks
/// that there is something to withhold.
/// </param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OfferModel(
    OfferId Id,
    DealId DealId,
    OfferDirection Direction,
    OfferStatus Status,
    int Sequence,
    OfferId? RespondsToOfferId,
    OfferResponseKind? ResponseKind,
    DateTimeOffset RecordedAt,
    DateTimeOffset? CommunicatedAt,
    UserId RecordedByUserId,
    string? RecordedByDisplayName,
    string? Summary,
    string? Notes,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<OfferTermModel> Terms,
    bool HasRedactedTerms,
    int Version);

/// <summary>A negotiation in a list.</summary>
/// <param name="Id">Deal identifier.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Reference">The agency's internal handle.</param>
/// <param name="Kind">What kind of transaction.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="OpportunityId">The pursuit it came from.</param>
/// <param name="OpportunityName">That pursuit's name.</param>
/// <param name="OpportunityTargetId">The market conversation it came from.</param>
/// <param name="CounterpartyDisplayName">
/// Who is on the other side, read through the target rather than copied.
/// </param>
/// <param name="CounterpartyCompanyId">The company, when the target names one.</param>
/// <param name="CounterpartyPersonId">The person, when the target names one.</param>
/// <param name="SubjectDisplayName">What the pursuit is about, through its primary subject.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OwnerDisplayName">Their name.</param>
/// <param name="OpenedOn">When the negotiation opened.</param>
/// <param name="ClosedOn">When it ended, or null while it runs.</param>
/// <param name="OfferCount">How many offers have been recorded. Derived.</param>
/// <param name="LatestOfferId">The most recent offer. Derived.</param>
/// <param name="LatestOfferDirection">Which side made it. Derived.</param>
/// <param name="LatestOfferAt">When it was made or received. Derived.</param>
/// <param name="HasOpenOffer">Whether one is awaiting an answer. Derived.</param>
/// <param name="OpenOfferExpiresAt">When the standing offer lapses, if it says. Derived.</param>
/// <param name="AcceptedOfferId">
/// The agreement, when terms are agreed. Derived from the one accepted offer
/// rather than stored on the deal.
/// </param>
/// <param name="OpenTaskCount">Outstanding linked tasks. Derived.</param>
/// <param name="NextTaskDueAt">The earliest outstanding task. Derived.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record DealSummaryModel(
    DealId Id,
    string Name,
    string? Reference,
    DealKind Kind,
    DealStatus Status,
    OpportunityId OpportunityId,
    string OpportunityName,
    OpportunityTargetId OpportunityTargetId,
    string CounterpartyDisplayName,
    Guid? CounterpartyCompanyId,
    Guid? CounterpartyPersonId,
    string? SubjectDisplayName,
    UserId OwnerUserId,
    string? OwnerDisplayName,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int OfferCount,
    OfferId? LatestOfferId,
    OfferDirection? LatestOfferDirection,
    DateTimeOffset? LatestOfferAt,
    bool HasOpenOffer,
    DateTimeOffset? OpenOfferExpiresAt,
    OfferId? AcceptedOfferId,
    int OpenTaskCount,
    DateTimeOffset? NextTaskDueAt,
    DateTimeOffset UpdatedAt,
    int Version);

/// <summary>An outstanding task linked to a negotiation.</summary>
public sealed record DealTaskModel(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    OfferId? OfferId);

/// <summary>One entry in a negotiation's curated timeline.</summary>
/// <remarks>
/// Composed from deal events, offer events and the M6 activity that led to the
/// negotiation. Never raw audit rows: the audit log answers a security question,
/// and showing it to agents would answer the wrong one (ADR-0012).
/// </remarks>
/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Kind">What kind of entry this is.</param>
/// <param name="Summary">A readable sentence.</param>
/// <param name="Detail">Secondary context.</param>
/// <param name="OfferId">The offer it concerned, when it concerned one.</param>
/// <param name="ActorDisplayName">Who recorded it, when the record names somebody.</param>
public sealed record DealHistoryEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    OfferId? OfferId,
    string? ActorDisplayName);

/// <summary>A negotiation's full working surface.</summary>
/// <param name="Deal">Headline fields.</param>
/// <param name="Summary">Factual description.</param>
/// <param name="StrategyNotes">
/// Internal strategy. Absent without <c>deals.strategy.read</c>, and absent is
/// deliberately indistinguishable from empty.
/// </param>
/// <param name="Offers">The negotiation thread, in order.</param>
/// <param name="AcceptedOffer">The agreement, when there is one.</param>
/// <param name="OpenOffer">The offer awaiting an answer, when there is one.</param>
/// <param name="OpenTasks">Outstanding linked tasks.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record DealDetailModel(
    DealSummaryModel Deal,
    string? Summary,
    string? StrategyNotes,
    IReadOnlyList<OfferModel> Offers,
    OfferModel? AcceptedOffer,
    OfferModel? OpenOffer,
    IReadOnlyList<DealTaskModel> OpenTasks,
    DateTimeOffset CreatedAt);

/// <summary>One term's difference between two offers.</summary>
/// <param name="Code">The term being compared.</param>
/// <param name="DisplayName">What the catalog calls it.</param>
/// <param name="Change">Added, removed, changed or unchanged.</param>
/// <param name="Direction">
/// Which way the number moved, when the values are comparable at all. Movement,
/// never merit.
/// </param>
/// <param name="Previous">The earlier value, absent when the term was added.</param>
/// <param name="Current">The later value, absent when the term was removed.</param>
public sealed record TermDifferenceModel(
    DealTermCode Code,
    string DisplayName,
    string Change,
    string Direction,
    OfferTermModel? Previous,
    OfferTermModel? Current);

/// <summary>A comparison of two offers in the same negotiation.</summary>
/// <param name="DealId">The negotiation.</param>
/// <param name="PreviousOfferId">The earlier offer.</param>
/// <param name="CurrentOfferId">The later offer.</param>
/// <param name="Differences">Every term in either offer, exactly once.</param>
public sealed record OfferComparisonModel(
    DealId DealId,
    OfferId PreviousOfferId,
    OfferId CurrentOfferId,
    IReadOnlyList<TermDifferenceModel> Differences);

/// <summary>A negotiation grouped under the status it is in.</summary>
public sealed record DealPipelineColumnModel(DealStatus Status, IReadOnlyList<DealSummaryModel> Deals);

/// <summary>
/// What a negotiator needs to look at, stated as fact.
/// </summary>
/// <remarks>
/// Counts and dates only. There is no probability, no expected commission, no
/// forecast and no quality score: M9 owns financial accounting and a later
/// milestone owns judgment, and a number invented here would be read as one the
/// agency stood behind (ADR-0021).
/// </remarks>
/// <param name="Negotiating">Deals actively being worked.</param>
/// <param name="AwaitingResponse">Deals whose standing offer has had no answer.</param>
/// <param name="ExpiringSoon">Standing offers with a stated lapse approaching.</param>
/// <param name="RecentlyReceived">Offers recorded recently.</param>
/// <param name="RecentlyAccepted">Deals whose terms were agreed recently.</param>
/// <param name="RecentlyClosed">Deals recently rejected, withdrawn or closed without agreement.</param>
/// <param name="TermsAgreedAwaitingContract">
/// Deals whose commercial terms are settled. A factual state, and explicitly not a
/// claim that any contract work has begun.
/// </param>
/// <param name="OverdueTasks">Linked tasks past their date.</param>
/// <param name="NegotiatingCount">How many deals are being worked.</param>
/// <param name="TermsAgreedCount">How many have agreed terms.</param>
public sealed record DealCommandCenterModel(
    IReadOnlyList<DealSummaryModel> Negotiating,
    IReadOnlyList<DealSummaryModel> AwaitingResponse,
    IReadOnlyList<DealSummaryModel> ExpiringSoon,
    IReadOnlyList<DealSummaryModel> RecentlyReceived,
    IReadOnlyList<DealSummaryModel> RecentlyAccepted,
    IReadOnlyList<DealSummaryModel> RecentlyClosed,
    IReadOnlyList<DealSummaryModel> TermsAgreedAwaitingContract,
    IReadOnlyList<DealTaskModel> OverdueTasks,
    int NegotiatingCount,
    int TermsAgreedCount);

/// <summary>
/// The optional predicates a deal list accepts.
/// </summary>
/// <remarks>
/// Deliberately carries nothing economic. A filter is a question somebody may run
/// from a saved view, and one that narrows by a figure tells its reader the figure
/// whether or not they may read it.
/// </remarks>
/// <param name="Status">Restrict to one status.</param>
/// <param name="Kind">Restrict to one kind of transaction.</param>
/// <param name="OwnerUserId">Restrict to one internal owner.</param>
/// <param name="OpportunityId">Only negotiations from this pursuit.</param>
/// <param name="OpportunityTargetId">Only negotiations from this market conversation.</param>
/// <param name="CounterpartyCompanyId">Only negotiations with this company.</param>
/// <param name="CounterpartyPersonId">Only negotiations with this person.</param>
/// <param name="TalentProfileId">Only negotiations about this client.</param>
/// <param name="ProjectId">Only negotiations about this project.</param>
/// <param name="HasOpenOffer">Only negotiations with an offer awaiting an answer.</param>
/// <param name="TermsAgreedOnly">Only negotiations whose terms are settled.</param>
/// <param name="OpenedAfter">Opened on or after this date.</param>
/// <param name="OpenedBefore">Opened on or before this date.</param>
/// <param name="Search">Substring match on name, reference and summary.</param>
public sealed record DealFilter(
    DealStatus? Status = null,
    DealKind? Kind = null,
    UserId? OwnerUserId = null,
    OpportunityId? OpportunityId = null,
    OpportunityTargetId? OpportunityTargetId = null,
    Guid? CounterpartyCompanyId = null,
    Guid? CounterpartyPersonId = null,
    Guid? TalentProfileId = null,
    Guid? ProjectId = null,
    bool HasOpenOffer = false,
    bool TermsAgreedOnly = false,
    DateOnly? OpenedAfter = null,
    DateOnly? OpenedBefore = null,
    string? Search = null);

/// <summary>
/// Read-side projections for the deal model.
/// </summary>
/// <remarks>
/// Every derived fact - offer counts, the standing offer, the agreement, whether a
/// reply is outstanding - is computed here from the rows that recorded the events.
/// Nothing is stored alongside the aggregate, so nothing can disagree with it
/// (ADR-0021).
/// </remarks>
public interface IDealQueries
{
    Task<IReadOnlyList<DealSummaryModel>> ListDealsAsync(
        OrganizationId organizationId,
        DealFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<DealDetailModel?> GetDealAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfferModel>> ListOffersAsync(
        OrganizationId organizationId,
        DealId? dealId,
        CancellationToken cancellationToken = default);

    Task<OfferModel?> GetOfferAsync(
        OrganizationId organizationId,
        OfferId id,
        CancellationToken cancellationToken = default);

    /// <summary>The offer awaiting an answer, or the agreement when terms are agreed.</summary>
    Task<OfferModel?> GetCurrentOfferAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        DealId id,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealPipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        UserId? ownerUserId,
        CancellationToken cancellationToken = default);

    Task<DealCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
