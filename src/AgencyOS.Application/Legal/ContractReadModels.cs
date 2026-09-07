using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Application.Legal;

/// <summary>A party to a contract, with the name resolved.</summary>
/// <param name="Id">Party identifier.</param>
/// <param name="Role">What they are doing in the agreement.</param>
/// <param name="DisplayName">Their name, joined for a known party or as written for an external one.</param>
/// <param name="PersonId">The person, when AgencyOS knows them.</param>
/// <param name="CompanyId">The company, when AgencyOS knows them.</param>
/// <param name="IsResolved">Whether AgencyOS has a record for them.</param>
/// <param name="Provenance">Where an external name came from.</param>
/// <param name="IsRequiredSignatory">Whether the agreement needs their signature.</param>
/// <param name="HasSigned">Whether a signature has been recorded. Derived.</param>
/// <param name="SignedOn">When, as reported.</param>
public sealed record ContractPartyModel(
    Guid Id,
    ContractPartyRole Role,
    string DisplayName,
    Guid? PersonId,
    Guid? CompanyId,
    bool IsResolved,
    string? Provenance,
    bool IsRequiredSignatory,
    bool HasSigned,
    DateOnly? SignedOn);

/// <summary>One term recorded from a drafting version.</summary>
/// <remarks>
/// The value fields mirror the stored row. <see cref="DisplayValue"/> is a
/// rendering for a screen and is never parsed back.
/// </remarks>
public sealed record ContractTermModel(
    ContractTermCode Code,
    string DisplayName,
    TermValueKind ValueKind,
    bool IsEconomic,

    /// <summary>Whether a person classified this term above ordinary. Never inferred.</summary>
    bool IsPrivileged,

    bool IsCommercial,
    decimal? Amount,
    string? Currency,
    decimal? Number,
    long? Whole,
    string? Text,
    bool? Flag,
    DateOnly? Date,
    TermUnit? Unit,
    string DisplayValue,
    string? ClauseReference,
    int Sequence,
    string? Notes);

/// <summary>One drafting state of a contract.</summary>
/// <param name="Id">Version identifier.</param>
/// <param name="ContractId">The instrument it belongs to.</param>
/// <param name="VersionNumber">Position in the drafting sequence.</param>
/// <param name="Label">What the agency calls it.</param>
/// <param name="Direction">Inbound, Outbound or Internal.</param>
/// <param name="Status">Draft, Recorded or Superseded.</param>
/// <param name="RecordedAt">When AgencyOS was told.</param>
/// <param name="ReceivedOn">When it arrived.</param>
/// <param name="SentOn">When it went out.</param>
/// <param name="RecordedByDisplayName">Who recorded it.</param>
/// <param name="ExternalReference">Where the document actually lives.</param>
/// <param name="SourceSystem">Which system that reference belongs to.</param>
/// <param name="DisplayFileName">The filename as a person would recognise it.</param>
/// <param name="MediaType">The document's media type, as reported.</param>
/// <param name="HoldsDocument">
/// Whether AgencyOS holds the file itself. Always false: M8 records a reference.
/// </param>
/// <param name="Notes">What changed, factually.</param>
/// <param name="Terms">
/// The terms read out of it. Economic values are absent without
/// <c>deals.economics.read</c>, and privileged terms without
/// <c>contracts.privileged.read</c>.
/// </param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ContractVersionModel(
    ContractVersionId Id,
    ContractId ContractId,
    int VersionNumber,
    string Label,
    VersionDirection Direction,
    ContractVersionStatus Status,
    DateTimeOffset RecordedAt,
    DateOnly? ReceivedOn,
    DateOnly? SentOn,
    string? RecordedByDisplayName,
    string? ExternalReference,
    string? SourceSystem,
    string? DisplayFileName,
    string? MediaType,
    bool HoldsDocument,
    string? Notes,
    IReadOnlyList<ContractTermModel> Terms,
    int Version);

/// <summary>A contract in a list.</summary>
/// <remarks>
/// Every derived figure here is a count, a date or a boolean over rows the
/// database holds. Nothing is stored alongside the aggregate, so nothing can
/// disagree with it (ADR-0022).
/// </remarks>
public sealed record ContractSummaryModel(
    ContractId Id,
    string Title,
    string? Reference,
    ContractKind Kind,
    ContractStatus Status,
    DealId DealId,
    string DealName,
    OfferId AcceptedOfferId,
    string? SubjectDisplayName,
    string CounterpartyDisplayName,
    UserId OwnerUserId,
    string? OwnerDisplayName,
    DateOnly? ExecutedOn,
    DateOnly? EffectiveOn,
    DateOnly? TerminatedOn,

    /// <summary>Whether the agreement is in force today. Derived from its dates.</summary>
    bool IsEffective,

    int VersionCount,
    int? LatestVersionNumber,
    string? LatestVersionLabel,
    ContractVersionId? LatestVersionId,

    /// <summary>How many required signatures are still outstanding. Derived.</summary>
    int OutstandingSignatureCount,

    int PartyCount,
    int RightsGrantCount,
    int OpenOptionCount,
    int OutstandingObligationCount,

    /// <summary>Obligations past their resolved due date and still outstanding. Derived.</summary>
    int OverdueObligationCount,

    /// <summary>The earliest legal deadline still ahead. Derived.</summary>
    DateOnly? NextDeadlineOn,

    /// <summary>What that deadline is.</summary>
    string? NextDeadlineDescription,

    /// <summary>How many terms differ between the offer and the latest version. Derived.</summary>
    int? UnresolvedDifferenceCount,

    DateTimeOffset UpdatedAt,
    int Version);

/// <summary>A grant recorded from a contract.</summary>
/// <remarks>
/// A grant row means the contract records this grant. It is not a finding that the
/// grantor held the rights or that title is good, and the wording throughout says
/// so (ADR-0022).
/// </remarks>
public sealed record RightsGrantModel(
    RightsGrantId Id,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    string? ClauseReference,
    Guid GrantorPartyId,
    string GrantorDisplayName,
    Guid GranteePartyId,
    string GranteeDisplayName,
    RightType RightType,
    RightsMedium Medium,
    RightsTerritory Territory,
    string? TerritoryDetail,
    GrantExclusivity Exclusivity,
    GrantPeriodKind PeriodKind,
    DateOnly? StartsOn,
    DateOnly? EndsOn,

    /// <summary>Whether the grant is running today. Derived from its period and status.</summary>
    bool IsCurrent,

    Guid? SourcePropertyId,
    string? SourcePropertyTitle,
    Guid? ProjectId,
    string? ProjectTitle,
    string? Reservations,
    string? Notes,
    RightsGrantStatus Status,
    RightsGrantId? SupersededByGrantId,
    int Version);

/// <summary>An option recorded from a contract.</summary>
public sealed record ContractOptionModel(
    ContractOptionId Id,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    string? ClauseReference,
    OptionKind Kind,
    Guid HolderPartyId,
    string HolderDisplayName,
    string Subject,
    Guid? ProjectId,
    string? ProjectTitle,
    DateOnly? WindowOpensOn,

    /// <summary>The deadline, once it is a date anybody can work out.</summary>
    DateOnly? DeadlineOn,

    /// <summary>Why the deadline is not a date, when it is not.</summary>
    string? DeadlineUnresolvedReason,

    /// <summary>The clause's own wording, always.</summary>
    string? DeadlineDescription,

    string? ExerciseMethod,
    OptionStatus Status,
    DateOnly? ResolvedOn,

    /// <summary>Whether the election could be made today. Derived.</summary>
    bool IsExercisable,

    /// <summary>Whether the deadline has passed with the option unresolved. Derived.</summary>
    bool IsPastDeadline,

    ContractTermId? EconomicsTermId,
    NoticeRequirementId? NoticeRequirementId,
    string? Notes,
    int Version);

/// <summary>An obligation recorded from a contract.</summary>
public sealed record ObligationModel(
    ObligationId Id,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    string? ClauseReference,
    Guid ObligorPartyId,
    string ObligorDisplayName,
    Guid ObligeePartyId,
    string ObligeeDisplayName,
    ObligationKind Kind,
    string Description,

    /// <summary>The due date, once it is a date anybody can work out.</summary>
    DateOnly? DueOn,

    /// <summary>Why the due date is not a date, when it is not.</summary>
    string? DueUnresolvedReason,

    /// <summary>The clause's own wording, always.</summary>
    string? DueDescription,

    ObligationStatus Status,
    DateOnly? ResolvedOn,

    /// <summary>
    /// Whether the due date has passed with the obligation outstanding. Derived,
    /// and deliberately not the same as breached.
    /// </summary>
    bool IsPastDue,

    ContractOptionId? RelatedOptionId,
    RightsGrantId? RelatedRightsGrantId,

    /// <summary>Whether a person classified this obligation above ordinary.</summary>
    bool IsPrivileged,

    string? Notes,
    int Version);

/// <summary>A notice the contract requires.</summary>
public sealed record NoticeRequirementModel(
    NoticeRequirementId Id,
    ContractId ContractId,
    string? ClauseReference,
    Guid ObligorPartyId,
    string ObligorDisplayName,
    Guid RecipientPartyId,
    string RecipientDisplayName,
    string Description,
    DateOnly? DueOn,
    string? DueUnresolvedReason,
    string? DueDescription,
    NoticeMethod Method,
    string? AddressReference,
    ContractOptionId? RelatedOptionId,
    ObligationId? RelatedObligationId,

    /// <summary>How many notices have been recorded against it. Derived.</summary>
    int RecordedNoticeCount,

    int Version);

/// <summary>A notice somebody recorded as given or received.</summary>
/// <remarks>AgencyOS does not send notices. This is an assertion that one passed.</remarks>
public sealed record NoticeRecordModel(
    Guid Id,
    ContractId ContractId,
    NoticeRequirementId? NoticeRequirementId,
    NoticeDirection Direction,
    string SenderDisplayName,
    string RecipientDisplayName,
    DateOnly OccurredOn,
    NoticeMethod Method,
    string? ExternalReference,
    string? Summary,
    string? RecordedByDisplayName);

/// <summary>How one contract relates to another.</summary>
public sealed record ContractRelationshipModel(
    Guid Id,
    ContractRelationshipKind Kind,
    ContractId RelatedContractId,
    string RelatedContractTitle,
    ContractStatus RelatedContractStatus,
    string? Notes);

/// <summary>An outstanding task linked to contract work.</summary>
public sealed record ContractTaskModel(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    ObligationId? ObligationId,
    ContractOptionId? ContractOptionId);

/// <summary>One entry in a contract's curated legal timeline.</summary>
/// <remarks>
/// Composed from contract, option and obligation events plus the notices and
/// signatures recorded against it. Never raw audit rows, which answer a security
/// question in a security vocabulary (ADR-0012).
/// </remarks>
public sealed record ContractHistoryEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <summary>One line of the negotiated-against-drafted comparison.</summary>
/// <param name="Code">The term compared.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="Result">Matched, Changed, MissingFromContract, AddedInContract or NotComparable.</param>
/// <param name="Direction">Which way a comparable number moved. Movement, never merit.</param>
/// <param name="Negotiated">What the accepted offer said, absent when the contract added it.</param>
/// <param name="Contracted">What the version says, absent when the contract omits it.</param>
public sealed record ReconciliationLineModel(
    ContractTermCode Code,
    string DisplayName,
    string Result,
    string Direction,
    ContractTermModel? Negotiated,
    ContractTermModel? Contracted);

/// <summary>What a drafting version did to what was agreed.</summary>
/// <param name="ContractId">The instrument.</param>
/// <param name="ContractVersionId">The version compared.</param>
/// <param name="AcceptedOfferId">The commercial snapshot compared against.</param>
/// <param name="Lines">Every term on either side, exactly once.</param>
/// <param name="DifferenceCount">How many lines are not a plain match.</param>
/// <param name="IsFaithful">Whether the draft says exactly what was negotiated.</param>
public sealed record ReconciliationModel(
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    OfferId AcceptedOfferId,
    IReadOnlyList<ReconciliationLineModel> Lines,
    int DifferenceCount,
    bool IsFaithful);

/// <summary>A contract's full working surface.</summary>
/// <param name="Contract">Headline fields.</param>
/// <param name="Summary">Factual description of the instrument.</param>
/// <param name="LegalAnalysis">
/// What counsel thinks. Absent without <c>contracts.privileged.read</c> when
/// classified above ordinary, and absent is indistinguishable from empty.
/// </param>
/// <param name="StrategyNotes">The agency's own strategy on the paper. Same rule.</param>
/// <param name="Privilege">How the content is classified. Assigned by a person.</param>
/// <param name="Parties">Who is party to it, and who still has to sign.</param>
/// <param name="Versions">The drafting history, newest first.</param>
/// <param name="Relationships">Amendments, side letters and what this supersedes.</param>
/// <param name="RightsGrants">What the contract records as granted.</param>
/// <param name="Options">The elections it creates.</param>
/// <param name="Obligations">What the parties must do.</param>
/// <param name="NoticeRequirements">The notices it requires.</param>
/// <param name="RecordedNotices">Notices somebody recorded as given or received.</param>
/// <param name="OpenTasks">Outstanding linked tasks.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record ContractDetailModel(
    ContractSummaryModel Contract,
    string? Summary,
    string? LegalAnalysis,
    string? StrategyNotes,
    PrivilegeClass Privilege,
    IReadOnlyList<ContractPartyModel> Parties,
    IReadOnlyList<ContractVersionModel> Versions,
    IReadOnlyList<ContractRelationshipModel> Relationships,
    IReadOnlyList<RightsGrantModel> RightsGrants,
    IReadOnlyList<ContractOptionModel> Options,
    IReadOnlyList<ObligationModel> Obligations,
    IReadOnlyList<NoticeRequirementModel> NoticeRequirements,
    IReadOnlyList<NoticeRecordModel> RecordedNotices,
    IReadOnlyList<ContractTaskModel> OpenTasks,
    DateTimeOffset CreatedAt);

/// <summary>What kind of thing a legal deadline comes from.</summary>
public enum LegalDeadlineSource
{
    Option = 1,
    Obligation = 2,
    Notice = 3,
}

/// <summary>
/// A date somebody has to act on, unioned from the rows that carry one.
/// </summary>
/// <remarks>
/// Derived rather than stored in a table of its own. A deadlines table would need
/// a source identifier that no foreign key could constrain, and it would be a
/// second copy of dates the option, obligation and notice rows already hold - the
/// exact shape the milestone brief warned against (ADR-0022).
/// </remarks>
public sealed record LegalDeadlineModel(
    LegalDeadlineSource Source,
    Guid SourceId,
    ContractId ContractId,
    string ContractTitle,
    DateOnly DueOn,
    string Description,

    /// <summary>Days from today. Negative once it has passed.</summary>
    int DaysRemaining,

    bool IsPast);

/// <summary>
/// What the legal side of the desk needs to look at, stated as fact.
/// </summary>
/// <remarks>
/// Counts, dates and lists. No risk score, no reading of what a clause means, no
/// expected payment: whether a difference matters is a legal judgement, and
/// whether money arrives is M9's subject (ADR-0022).
/// </remarks>
public sealed record ContractCommandCenterModel(
    IReadOnlyList<ContractSummaryModel> UnderReview,
    IReadOnlyList<ContractSummaryModel> AwaitingSignature,
    IReadOnlyList<ContractSummaryModel> WithUnresolvedDifferences,
    IReadOnlyList<ContractSummaryModel> RecentlyExecuted,

    /// <summary>Agreed deals with nothing papered yet. The M7 to M8 gap.</summary>
    IReadOnlyList<DealsWithoutContractModel> TermsAgreedWithoutContract,

    IReadOnlyList<LegalDeadlineModel> UpcomingDeadlines,
    IReadOnlyList<ObligationModel> OverdueObligations,
    IReadOnlyList<ContractOptionModel> OptionsPastDeadline,
    IReadOnlyList<ContractTaskModel> OverdueTasks,
    int UnderReviewCount,
    int AwaitingSignatureCount,
    int EffectiveCount);

/// <summary>A negotiation whose terms are agreed and which has no contract yet.</summary>
public sealed record DealsWithoutContractModel(
    DealId DealId,
    string DealName,
    string CounterpartyDisplayName,
    DateTimeOffset AgreedAt);

/// <summary>
/// The optional predicates a contract list accepts.
/// </summary>
/// <remarks>
/// Nothing economic, for the reason the deal filters give: a filter is a question
/// somebody else may run from a saved view.
/// </remarks>
public sealed record ContractFilter(
    ContractStatus? Status = null,
    ContractKind? Kind = null,
    UserId? OwnerUserId = null,
    DealId? DealId = null,
    Guid? PartyCompanyId = null,
    Guid? PartyPersonId = null,
    Guid? TalentProfileId = null,
    Guid? ProjectId = null,
    bool AwaitingSignature = false,
    bool EffectiveOnly = false,
    bool HasUnresolvedReconciliation = false,
    DateOnly? ExecutedAfter = null,
    DateOnly? ExecutedBefore = null,
    string? Search = null);

/// <summary>The optional predicates an option list accepts.</summary>
public sealed record OptionFilter(
    ContractId? ContractId = null,
    OptionStatus? Status = null,
    OptionKind? Kind = null,
    bool ExercisableOnly = false,
    bool PastDeadlineOnly = false,
    DateOnly? DeadlineBefore = null);

/// <summary>The optional predicates an obligation list accepts.</summary>
public sealed record ObligationFilter(
    ContractId? ContractId = null,
    ObligationStatus? Status = null,
    ObligationKind? Kind = null,
    Guid? ObligorPartyId = null,
    bool OutstandingOnly = false,
    bool OverdueOnly = false,
    DateOnly? DueBefore = null);

/// <summary>
/// Read-side projections for the contract model.
/// </summary>
/// <remarks>
/// Every derived fact - outstanding signatures, effectiveness, overdue
/// obligations, unresolved differences, the next deadline - is computed here from
/// the rows that recorded the events. Nothing is stored beside the aggregate, so
/// nothing can drift from it (ADR-0022).
/// </remarks>
public interface IContractQueries
{
    Task<IReadOnlyList<ContractSummaryModel>> ListContractsAsync(
        OrganizationId organizationId,
        ContractFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ContractDetailModel?> GetContractAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default);

    Task<ContractVersionModel?> GetVersionAsync(
        OrganizationId organizationId,
        ContractVersionId id,
        CancellationToken cancellationToken = default);

    /// <summary>Compares a version against the offer the contract was drafted from.</summary>
    Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContractOptionModel>> ListOptionsAsync(
        OrganizationId organizationId,
        OptionFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ObligationFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RightsGrantModel>> ListRightsGrantsAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        Guid? projectId,
        bool currentOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContractHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId id,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Every legal date still ahead, from options, obligations and notices.</summary>
    Task<IReadOnlyList<LegalDeadlineModel>> GetDeadlinesAsync(
        OrganizationId organizationId,
        DateOnly from,
        int withinDays,
        CancellationToken cancellationToken = default);

    Task<ContractCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
