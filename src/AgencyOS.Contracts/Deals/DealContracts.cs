namespace AgencyOS.Contracts.Deals;

// ------------------------------------------------------------------- requests

/// <summary>
/// One term's value on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one group of fields is populated, decided by <paramref name="Kind"/>.
/// A combination that does not match is refused rather than partly applied, so a
/// money term that also carries text is an error the caller sees rather than a
/// value that silently disappears.
/// </para>
/// <para>
/// <paramref name="Amount"/> is a decimal and there is no floating-point field
/// anywhere in this contract. Money is always an amount and a currency; a bare
/// number would be a figure nobody can settle (CLAUDE.md section 5).
/// </para>
/// </remarks>
/// <param name="Kind">Money, Percentage, Integer, Decimal, Text, Boolean, Date, Duration or Count.</param>
/// <param name="Amount">Money amount, for a money term.</param>
/// <param name="Currency">ISO 4217 alphabetic code. Required with an amount.</param>
/// <param name="Number">Percentage or decimal value.</param>
/// <param name="Whole">Integer, duration length or count quantity.</param>
/// <param name="Text">Text value.</param>
/// <param name="Flag">Boolean value.</param>
/// <param name="Date">Business date value.</param>
/// <param name="Unit">Day, Week, Month, Year, Episode, Season, Draft or Step.</param>
public sealed record TermValueRequest(
    string Kind,
    decimal? Amount = null,
    string? Currency = null,
    decimal? Number = null,
    long? Whole = null,
    string? Text = null,
    bool? Flag = null,
    DateOnly? Date = null,
    string? Unit = null);

/// <param name="Code">The term, from the controlled vocabulary.</param>
/// <param name="Value">Its value.</param>
/// <param name="Label">A display override, when the catalog name is not enough.</param>
/// <param name="Notes">Context that is not part of the value, such as the basis of a percentage.</param>
public sealed record OfferTermRequest(
    string Code,
    TermValueRequest Value,
    string? Label = null,
    string? Notes = null);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="AssignedTo">Who should do it. Defaults to the caller.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record DealFollowUpRequest(
    string Title,
    DateTimeOffset? DueAt = null,
    Guid? AssignedTo = null,
    string? Notes = null);

/// <param name="OpportunityId">The pursuit this comes out of. Required.</param>
/// <param name="OpportunityTargetId">The market conversation it comes out of. Required.</param>
/// <param name="Name">What the negotiation is called.</param>
/// <param name="Kind">
/// TalentEmployment, Writing, Directing, Producing, ProjectSale, ProjectLicense,
/// Package, Services, Partnership or Other.
/// </param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OpenedOn">When it opened. Defaults to today.</param>
/// <param name="Reference">The agency's internal handle, if it uses one.</param>
/// <param name="Summary">Factual description of what is being negotiated.</param>
/// <param name="StrategyNotes">Internal strategy. Requires <c>deals.strategy.read</c> to read back.</param>
public sealed record CreateDealRequest(
    Guid OpportunityId,
    Guid OpportunityTargetId,
    string Name,
    string Kind,
    Guid OwnerUserId,
    DateOnly? OpenedOn = null,
    string? Reference = null,
    string? Summary = null,
    string? StrategyNotes = null);

/// <param name="ExpectedVersion">The version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateDealRequest(
    string Name,
    string Kind,
    Guid OwnerUserId,
    int ExpectedVersion,
    string? Reference = null,
    string? Summary = null,
    string? StrategyNotes = null);

/// <summary>Closes a negotiation, or calls it off.</summary>
/// <param name="Status">
/// NoDeal or Cancelled. Negotiating and TermsAgreed are deliberately not
/// settable: a negotiation reaches them by recording and accepting offers.
/// </param>
/// <param name="ClosedOn">When it ended. Defaults to today.</param>
/// <param name="Reason">Why, for the history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record CloseDealRequest(
    string Status,
    int ExpectedVersion,
    DateOnly? ClosedOn = null,
    string? Reason = null);

/// <param name="Reason">Why the negotiation is being reopened.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ReopenNegotiationRequest(int ExpectedVersion, string? Reason = null);

/// <summary>
/// Records an offer that has already been made or received.
/// </summary>
/// <remarks>
/// AgencyOS records the offer. It does not send it, has no outbound transport and
/// cannot confirm that anything reached anybody. <paramref name="CommunicatedAt"/>
/// is when the agent says it happened, freely backdated.
/// </remarks>
/// <param name="Direction">Inbound when the counterparty proposed it, Outbound when we did.</param>
/// <param name="Terms">The commercial snapshot. At least one.</param>
/// <param name="RespondsToOfferId">The offer this answers, for a counter or a revision.</param>
/// <param name="CommunicatedAt">When it was made or received. Defaults to now.</param>
/// <param name="ExpiresAt">When it was stated to lapse, if a lapse was stated.</param>
/// <param name="Summary">A short factual description.</param>
/// <param name="Notes">Narrative context the structured terms do not carry.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
/// <param name="ExpectedVersion">The deal's version. Required.</param>
public sealed record RecordOfferRequest(
    string Direction,
    IReadOnlyList<OfferTermRequest> Terms,
    int ExpectedVersion,
    Guid? RespondsToOfferId = null,
    DateTimeOffset? CommunicatedAt = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null,
    DealFollowUpRequest? FollowUp = null);

/// <summary>Starts an offer as an editable draft.</summary>
/// <param name="ExpectedVersion">The deal's version. Required.</param>
public sealed record DraftOfferRequest(
    string Direction,
    int ExpectedVersion,
    Guid? RespondsToOfferId = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The <em>offer's</em> version, not the deal's.</param>
public sealed record UpdateDraftOfferRequest(
    int ExpectedVersion,
    DateTimeOffset? CommunicatedAt = null,
    DateTimeOffset? ExpiresAt = null,
    string? Summary = null,
    string? Notes = null);

/// <summary>Adds, replaces or removes a term on a draft offer.</summary>
/// <param name="Value">Omit to remove the term.</param>
/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record ChangeOfferTermRequest(
    string Code,
    int ExpectedVersion,
    TermValueRequest? Value = null,
    string? Label = null,
    string? Notes = null);

/// <summary>Records a draft as actually made or received, freezing its terms.</summary>
/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record OpenOfferRequest(
    int ExpectedVersion,
    DateTimeOffset? CommunicatedAt = null,
    DealFollowUpRequest? FollowUp = null);

/// <summary>Records what happened to a standing offer.</summary>
/// <param name="Answer">
/// Accept, Reject, Withdraw or Expire. Accepting is the only route to agreed
/// terms. Expire requires the offer to have carried an expiration that has passed:
/// nothing lapses merely because time went by.
/// </param>
/// <param name="Reason">What they said, for the history.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
/// <param name="ExpectedVersion">The offer's version. Required.</param>
public sealed record AnswerOfferRequest(
    string Answer,
    int ExpectedVersion,
    string? Reason = null,
    DealFollowUpRequest? FollowUp = null);

// ------------------------------------------------------------------ responses

/// <param name="Code">The controlled term code.</param>
/// <param name="DisplayName">What it is called.</param>
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
/// <param name="DisplayValue">
/// A rendering for display only. The typed fields above are canonical; nothing
/// parses this back.
/// </param>
/// <param name="Sequence">Display order within the offer.</param>
/// <param name="Notes">Context that is not part of the value.</param>
public sealed record OfferTermResponse(
    string Code,
    string DisplayName,
    string ValueKind,
    bool IsEconomic,
    decimal? Amount,
    string? Currency,
    decimal? Number,
    long? Whole,
    string? Text,
    bool? Flag,
    DateOnly? Date,
    string? Unit,
    string DisplayValue,
    int Sequence,
    string? Notes);

/// <param name="Id">Offer identifier.</param>
/// <param name="DealId">The negotiation it belongs to.</param>
/// <param name="Direction">Inbound or Outbound.</param>
/// <param name="Status">Draft, Open, Accepted, Rejected, Withdrawn, Expired or Superseded.</param>
/// <param name="Sequence">Position in the negotiation thread.</param>
/// <param name="RespondsToOfferId">The offer it answers, when it answers one.</param>
/// <param name="ResponseKind">Counter or Revision, derived from the two sides.</param>
/// <param name="RecordedAt">When AgencyOS was told.</param>
/// <param name="CommunicatedAt">When it was made or received, as reported.</param>
/// <param name="RecordedByUserId">Who wrote it down.</param>
/// <param name="RecordedByDisplayName">Their name.</param>
/// <param name="Summary">A short factual description.</param>
/// <param name="Notes">Narrative context.</param>
/// <param name="ExpiresAt">When it was stated to lapse.</param>
/// <param name="Terms">
/// The commercial snapshot. Economic terms are <em>absent</em> without
/// <c>deals.economics.read</c>, and absent is deliberately indistinguishable from
/// empty.
/// </param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OfferResponse(
    Guid Id,
    Guid DealId,
    string Direction,
    string Status,
    int Sequence,
    Guid? RespondsToOfferId,
    string? ResponseKind,
    DateTimeOffset RecordedAt,
    DateTimeOffset? CommunicatedAt,
    Guid RecordedByUserId,
    string? RecordedByDisplayName,
    string? Summary,
    string? Notes,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<OfferTermResponse> Terms,
    int Version);

/// <param name="Id">Deal identifier.</param>
/// <param name="Name">What the negotiation is called.</param>
/// <param name="Reference">The agency's internal handle.</param>
/// <param name="Kind">What kind of transaction.</param>
/// <param name="Status">
/// Draft, Negotiating, TermsAgreed, NoDeal or Cancelled. TermsAgreed means the
/// commercial terms are settled and nothing more: no contract is drafted, signed
/// or executed, and M7 has no way to know whether any exists.
/// </param>
/// <param name="OpportunityId">The pursuit it came from.</param>
/// <param name="OpportunityName">That pursuit's name.</param>
/// <param name="OpportunityTargetId">The market conversation it came from.</param>
/// <param name="CounterpartyDisplayName">Who is on the other side, read through the target.</param>
/// <param name="CounterpartyCompanyId">The company, when the target names one.</param>
/// <param name="CounterpartyPersonId">The person, when the target names one.</param>
/// <param name="SubjectDisplayName">What the pursuit is about.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OwnerDisplayName">Their name.</param>
/// <param name="OpenedOn">When it opened.</param>
/// <param name="ClosedOn">When it ended, or null while it runs.</param>
/// <param name="OfferCount">How many offers have been recorded. Derived.</param>
/// <param name="LatestOfferId">The most recent offer. Derived.</param>
/// <param name="LatestOfferDirection">Which side made it. Derived.</param>
/// <param name="LatestOfferAt">When it was made or received. Derived.</param>
/// <param name="HasOpenOffer">Whether one is awaiting an answer. Derived.</param>
/// <param name="OpenOfferExpiresAt">When the standing offer lapses, if it says.</param>
/// <param name="AcceptedOfferId">
/// The agreement, when terms are agreed. Derived from the one accepted offer, not
/// stored on the deal.
/// </param>
/// <param name="OpenTaskCount">Outstanding linked tasks. Derived.</param>
/// <param name="NextTaskDueAt">The earliest outstanding task. Derived.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record DealSummaryResponse(
    Guid Id,
    string Name,
    string? Reference,
    string Kind,
    string Status,
    Guid OpportunityId,
    string OpportunityName,
    Guid OpportunityTargetId,
    string CounterpartyDisplayName,
    Guid? CounterpartyCompanyId,
    Guid? CounterpartyPersonId,
    string? SubjectDisplayName,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int OfferCount,
    Guid? LatestOfferId,
    string? LatestOfferDirection,
    DateTimeOffset? LatestOfferAt,
    bool HasOpenOffer,
    DateTimeOffset? OpenOfferExpiresAt,
    Guid? AcceptedOfferId,
    int OpenTaskCount,
    DateTimeOffset? NextTaskDueAt,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Task identifier.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="State">Open or Completed.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="OfferId">The offer it concerns, when it concerns one.</param>
/// <param name="AssigneeUserId">The member accountable for it, when one is.</param>
/// <param name="AssigneeDisplayName">Their name.</param>
public sealed record DealTaskResponse(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    Guid? OfferId,
    Guid? AssigneeUserId = null,
    string? AssigneeDisplayName = null);

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
public sealed record DealDetailResponse(
    DealSummaryResponse Deal,
    string? Summary,
    string? StrategyNotes,
    IReadOnlyList<OfferResponse> Offers,
    OfferResponse? AcceptedOffer,
    OfferResponse? OpenOffer,
    IReadOnlyList<DealTaskResponse> OpenTasks,
    DateTimeOffset CreatedAt);

/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Kind">What kind of entry this is.</param>
/// <param name="Summary">A readable sentence.</param>
/// <param name="Detail">Secondary context.</param>
/// <param name="OfferId">The offer it concerned, when it concerned one.</param>
/// <param name="ActorDisplayName">Who recorded it.</param>
public sealed record DealHistoryEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    Guid? OfferId,
    string? ActorDisplayName);

/// <param name="Code">The term being compared.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="Change">Added, Removed, Changed or Unchanged.</param>
/// <param name="Direction">
/// Increased, Decreased, Level or NotComparable. Movement, never merit: whether a
/// change is welcome depends on the term and the side, and AgencyOS does not say.
/// </param>
/// <param name="Previous">The earlier value, absent when the term was added.</param>
/// <param name="Current">The later value, absent when the term was removed.</param>
public sealed record TermDifferenceResponse(
    string Code,
    string DisplayName,
    string Change,
    string Direction,
    OfferTermResponse? Previous,
    OfferTermResponse? Current);

/// <param name="DealId">The negotiation.</param>
/// <param name="PreviousOfferId">The earlier offer.</param>
/// <param name="CurrentOfferId">The later offer.</param>
/// <param name="Differences">Every term in either offer, exactly once.</param>
public sealed record OfferComparisonResponse(
    Guid DealId,
    Guid PreviousOfferId,
    Guid CurrentOfferId,
    IReadOnlyList<TermDifferenceResponse> Differences);

/// <param name="Status">The status these negotiations are in.</param>
/// <param name="Deals">The negotiations.</param>
public sealed record DealPipelineColumnResponse(
    string Status,
    IReadOnlyList<DealSummaryResponse> Deals);

/// <param name="Negotiating">Deals actively being worked.</param>
/// <param name="AwaitingResponse">Deals whose standing offer has had no answer.</param>
/// <param name="ExpiringSoon">Standing offers with a stated lapse approaching.</param>
/// <param name="RecentlyReceived">Offers recorded recently.</param>
/// <param name="RecentlyAccepted">Deals whose terms were agreed recently.</param>
/// <param name="RecentlyClosed">Deals recently closed without agreement or cancelled.</param>
/// <param name="TermsAgreedAwaitingContract">
/// Deals whose commercial terms are settled. A factual state, and explicitly not a
/// claim that contract work has started - M7 has no way to know whether it has.
/// </param>
/// <param name="OverdueTasks">Linked tasks past their date.</param>
/// <param name="NegotiatingCount">How many deals are being worked.</param>
/// <param name="TermsAgreedCount">How many have agreed terms.</param>
public sealed record DealCommandCenterResponse(
    IReadOnlyList<DealSummaryResponse> Negotiating,
    IReadOnlyList<DealSummaryResponse> AwaitingResponse,
    IReadOnlyList<DealSummaryResponse> ExpiringSoon,
    IReadOnlyList<DealSummaryResponse> RecentlyReceived,
    IReadOnlyList<DealSummaryResponse> RecentlyAccepted,
    IReadOnlyList<DealSummaryResponse> RecentlyClosed,
    IReadOnlyList<DealSummaryResponse> TermsAgreedAwaitingContract,
    IReadOnlyList<DealTaskResponse> OverdueTasks,
    int NegotiatingCount,
    int TermsAgreedCount);

/// <param name="OfferId">The offer recorded.</param>
/// <param name="DealId">The negotiation it belongs to.</param>
/// <param name="SupersededOfferId">The offer it answered, when it answered one.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordOfferResponse(
    Guid OfferId,
    Guid DealId,
    Guid? SupersededOfferId,
    Guid? FollowUpTaskId);

/// <param name="OfferId">The offer answered.</param>
/// <param name="DealStatus">Where the negotiation stands afterwards.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record AnswerOfferResponse(
    Guid OfferId,
    string DealStatus,
    Guid? FollowUpTaskId);

/// <summary>One supported term, as the catalog describes it.</summary>
/// <remarks>
/// Published so a client can build a term editor without hard-coding the
/// vocabulary. The catalog is the single source; a client that guessed would drift
/// from it (ADR-0021).
/// </remarks>
/// <param name="Code">The term code.</param>
/// <param name="DisplayName">What a person calls it.</param>
/// <param name="ValueKind">The one shape its value may take.</param>
/// <param name="IsEconomic">Whether reading its value requires <c>deals.economics.read</c>.</param>
/// <param name="AllowedUnits">Units it may be measured in. Empty when it has no unit.</param>
/// <param name="Minimum">Lower bound, when it has one.</param>
/// <param name="Maximum">Upper bound, when it has one.</param>
public sealed record DealTermDefinitionResponse(
    string Code,
    string DisplayName,
    string ValueKind,
    bool IsEconomic,
    IReadOnlyList<string> AllowedUnits,
    decimal? Minimum,
    decimal? Maximum);
