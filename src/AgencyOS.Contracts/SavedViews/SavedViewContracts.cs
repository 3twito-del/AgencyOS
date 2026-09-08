namespace AgencyOS.Contracts.SavedViews;

/// <summary>
/// The filters a saved view applies.
/// </summary>
/// <remarks>
/// A closed set of optional predicates, not an expression language. Everything
/// here maps to a specific reviewed clause in the query layer, so a saved view can
/// never become an arbitrary query.
/// </remarks>
/// <param name="Status">Person or company lifecycle status.</param>
/// <param name="CompanyId">Restricts people to one primary company.</param>
/// <param name="TitleContains">Substring match on a person's title.</param>
/// <param name="TextContains">Substring match on the primary name field.</param>
/// <param name="TaskState">Open or Completed.</param>
/// <param name="DueWithinDays">Tasks due within this many days.</param>
/// <param name="OverdueOnly">Only tasks past their due date.</param>
/// <param name="Discipline">Restricts talent to one professional discipline.</param>
/// <param name="ScopeArea">Restricts talent to one represented area.</param>
/// <param name="LeadUserId">Restricts talent to one internal owner.</param>
/// <param name="ClientsOnly">Only people the agency currently represents.</param>
/// <param name="FormerClientsOnly">Only people the agency used to represent.</param>
/// <param name="ProspectStage">Restricts prospects to one stage.</param>
/// <param name="OwnerUserId">Restricts prospects to one internal owner.</param>
/// <param name="FollowUpWithinDays">Only prospects needing attention within this many days.</param>
/// <param name="ContractKind">Restricts contracts to one kind of instrument.</param>
/// <param name="ContractStatus">Restricts contracts to one status.</param>
/// <param name="DealId">Only contracts arising from this negotiation.</param>
/// <param name="ContractPartyCompanyId">Only contracts with this company as a party.</param>
/// <param name="ContractPartyPersonId">Only contracts with this person as a party.</param>
/// <param name="AwaitingSignature">Only contracts with a required signature outstanding.</param>
/// <param name="EffectiveOnly">Only contracts in force today.</param>
/// <param name="ExecutedAfter">Only contracts executed on or after this date.</param>
/// <param name="ExecutedBefore">Only contracts executed on or before this date.</param>
/// <param name="HasUnresolvedReconciliation">
/// Only contracts whose newest version differs from what was agreed. A count of
/// differences, never a claim that any of them is a problem.
/// </param>
/// <param name="ReceivableStatus">Restricts receivables to one status.</param>
/// <param name="InvoiceStatus">Restricts invoices to one status.</param>
/// <param name="PaymentDirection">Incoming or Outgoing.</param>
/// <param name="PayerPartyId">Only rows this party owes or paid.</param>
/// <param name="ClientPersonId">Only money attributable to this client.</param>
/// <param name="ContractId">Only rows arising from this instrument.</param>
/// <param name="OverdueReceivablesOnly">
/// Only rows past a resolvable due date with something still owed. A receivable
/// with no due date is never overdue: the contract did not say when.
/// </param>
/// <param name="UnappliedPaymentsOnly">Only payments with cash still to apply.</param>
/// <param name="UnreconciledOnly">
/// Only receivables whose arithmetic does not yet explain itself.
/// </param>
/// <param name="DueAfter">Only rows due on or after this date.</param>
/// <param name="DueBefore">Only rows due on or before this date.</param>
/// <param name="RecordedAfter">Only rows recorded on or after this date.</param>
/// <param name="RecordedBefore">Only rows recorded on or before this date.</param>
/// <param name="CurrencyCode">
/// Restricts to one currency. The honest way to ask a monetary question of a
/// mixed book: there is no rate here that would let two be added.
/// </param>
public sealed record SavedViewFiltersModel(
    string? Status = null,
    Guid? CompanyId = null,
    string? TitleContains = null,
    string? TextContains = null,
    string? TaskState = null,
    int? DueWithinDays = null,
    bool OverdueOnly = false,
    string? Discipline = null,
    string? ScopeArea = null,
    Guid? LeadUserId = null,
    bool ClientsOnly = false,
    bool FormerClientsOnly = false,
    string? ProspectStage = null,
    Guid? OwnerUserId = null,
    int? FollowUpWithinDays = null,
    string? ProjectType = null,
    string? DevelopmentStage = null,
    string? ProjectStatus = null,
    Guid? AttachedPersonId = null,
    string? MissingRoleType = null,
    string? PackageStatus = null,
    Guid? ProjectId = null,
    string? OpportunityKind = null,
    string? OpportunityStatus = null,
    Guid? TalentProfileId = null,
    Guid? PackageId = null,
    Guid? TargetCompanyId = null,
    Guid? TargetPersonId = null,
    string? TargetStage = null,
    bool HasSubmission = false,
    bool AwaitingResponse = false,
    int? FollowUpDueWithinDays = null,

    // M7. Deliberately nothing economic: a saved view is a query somebody else may
    // run, and one narrowing by a compensation figure would tell its reader that
    // figure whether or not they may read it.
    string? DealKind = null,
    string? DealStatus = null,
    Guid? OpportunityId = null,
    Guid? OpportunityTargetId = null,
    Guid? CounterpartyCompanyId = null,
    Guid? CounterpartyPersonId = null,
    bool HasOpenOffer = false,
    bool TermsAgreedOnly = false,
    DateOnly? OpenedAfter = null,
    DateOnly? OpenedBefore = null,

    // M8. The same rule again, and one more with it: nothing here narrows by what
    // a clause says or by anything a person classified as privileged, because a
    // saved view is a query somebody else may run and its predicate would tell
    // them what it matched.
    string? ContractKind = null,
    string? ContractStatus = null,
    Guid? DealId = null,
    Guid? ContractPartyCompanyId = null,
    Guid? ContractPartyPersonId = null,
    bool AwaitingSignature = false,
    bool EffectiveOnly = false,
    DateOnly? ExecutedAfter = null,
    DateOnly? ExecutedBefore = null,
    bool HasUnresolvedReconciliation = false,

    // M9. The economic rule from M7 and M8, sharpened. Nothing here narrows by an
    // amount, a balance or a commission rate: a saved view is a query somebody
    // else may run, and a predicate reading "outstanding over fifty thousand"
    // would tell its reader the balance whether or not they may read it. What is
    // here is status, party, date, currency and work-queue shape - the questions a
    // finance desk asks to find rows, not to learn figures (ADR-0023).
    string? ReceivableStatus = null,
    string? InvoiceStatus = null,
    string? PaymentDirection = null,
    Guid? PayerPartyId = null,
    Guid? ClientPersonId = null,
    Guid? ContractId = null,
    bool OverdueReceivablesOnly = false,
    bool UnappliedPaymentsOnly = false,
    bool UnreconciledOnly = false,
    DateOnly? DueAfter = null,
    DateOnly? DueBefore = null,
    DateOnly? RecordedAfter = null,
    DateOnly? RecordedBefore = null,
    string? CurrencyCode = null);

/// <param name="Field">Field to order by. Must be sortable for the target.</param>
/// <param name="Direction">Ascending or Descending.</param>
public sealed record SavedViewSortModel(string Field, string Direction);

/// <summary>
/// A saved view's query, as a versioned document.
/// </summary>
/// <param name="DefinitionVersion">
/// Schema version of this document. Version 2 is current; version 1 documents are
/// still understood and read as they always meant, because version 2 only added
/// targets and filters. A version the server genuinely does not understand is
/// rejected rather than guessed at.
/// </param>
/// <param name="Target">People, Companies or Tasks.</param>
/// <param name="Filters">Predicates to apply.</param>
/// <param name="Sort">Ordering, or null for the target's default.</param>
public sealed record SavedViewDefinitionModel(
    int DefinitionVersion,
    string Target,
    SavedViewFiltersModel Filters,
    SavedViewSortModel? Sort = null);

/// <param name="Name">What the user calls this view.</param>
/// <param name="Definition">The query document.</param>
public sealed record CreateSavedViewRequest(string Name, SavedViewDefinitionModel Definition);

/// <param name="Name">Replacement name.</param>
/// <param name="Definition">Replacement query document.</param>
/// <param name="ExpectedVersion">Version the caller observed. Required.</param>
public sealed record UpdateSavedViewRequest(
    string Name,
    SavedViewDefinitionModel Definition,
    int ExpectedVersion);

/// <summary>
/// What running a saved view produced.
/// </summary>
/// <remarks>
/// Typed per target rather than one untyped list, so the client renders people as
/// people and tasks as tasks without inspecting a discriminator. Exactly one of
/// the three collections is populated, named by <paramref name="Target"/>.
/// </remarks>
/// <param name="Target">
/// Which list this is: People, Companies, Tasks, Talent, Prospects, Projects,
/// Packages, Opportunities, Deals, Contracts, Receivables, Invoices or Payments.
/// </param>
/// <param name="People">Matching people.</param>
/// <param name="Companies">Matching companies.</param>
/// <param name="Tasks">Matching tasks.</param>
/// <param name="Talent">Matching talent.</param>
/// <param name="Prospects">Matching prospects.</param>
/// <param name="Projects">Matching projects.</param>
/// <param name="Packages">Matching packages.</param>
/// <param name="Opportunities">Matching opportunities.</param>
/// <param name="Deals">Matching negotiations.</param>
/// <param name="Contracts">Matching contracts.</param>
/// <param name="Receivables">Matching receivables.</param>
/// <param name="Invoices">Matching invoices.</param>
/// <param name="Payments">Matching payments.</param>
public sealed record SavedViewResultsResponse(
    string Target,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.PersonSummaryResponse> People,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.CompanySummaryResponse> Companies,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.TaskResponse> Tasks,
    IReadOnlyList<AgencyOS.Contracts.Representation.TalentSummaryResponse> Talent,
    IReadOnlyList<AgencyOS.Contracts.Representation.ProspectResponse> Prospects,
    IReadOnlyList<AgencyOS.Contracts.Projects.ProjectSummaryResponse> Projects,
    IReadOnlyList<AgencyOS.Contracts.Projects.PackageSummaryResponse> Packages,
    IReadOnlyList<AgencyOS.Contracts.Opportunities.OpportunitySummaryResponse> Opportunities,
    IReadOnlyList<AgencyOS.Contracts.Deals.DealSummaryResponse> Deals,
    IReadOnlyList<AgencyOS.Contracts.Legal.ContractSummaryResponse> Contracts,
    IReadOnlyList<AgencyOS.Contracts.Finance.ReceivableResponse> Receivables,
    IReadOnlyList<AgencyOS.Contracts.Finance.InvoiceResponse> Invoices,
    IReadOnlyList<AgencyOS.Contracts.Finance.PaymentResponse> Payments);

/// <param name="Id">Saved view identifier.</param>
/// <param name="Name">What the user calls it.</param>
/// <param name="Target">What it lists.</param>
/// <param name="Definition">The query document.</param>
/// <param name="DefinitionVersion">Schema version of the document.</param>
/// <param name="Version">Optimistic concurrency token.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
public sealed record SavedViewResponse(
    Guid Id,
    string Name,
    string Target,
    SavedViewDefinitionModel Definition,
    int DefinitionVersion,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
