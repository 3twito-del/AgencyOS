using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Releases;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using AgencyOS.Contracts.Sync;

namespace AgencyOS.Client;

/// <summary>Raised when the API refuses or fails a request.</summary>
public sealed class AgencyOsApiException : Exception
{
    public AgencyOsApiException(
        HttpStatusCode statusCode,
        string message,
        string? detail = null,
        string? code = null,
        int? expectedVersion = null,
        int? actualVersion = null)
        : base(message)
    {
        StatusCode = statusCode;
        Detail = detail;
        Code = code;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Problem-details explanation from the server, when it supplied one.</summary>
    public string? Detail { get; }

    /// <summary>
    /// Machine-readable reason, when the server gave one.
    /// </summary>
    /// <remarks>
    /// Three different conditions answer 409 - a version conflict, a key still in
    /// flight, a duplicate name - and they call for three different responses.
    /// Distinguishing them by the title string would break the first time the
    /// wording improved.
    /// </remarks>
    public string? Code { get; }

    /// <summary>The version this client sent, when the server refused it as stale.</summary>
    public int? ExpectedVersion { get; }

    /// <summary>The version the record actually holds, when the server refused a stale write.</summary>
    public int? ActualVersion { get; }

    /// <summary>Gets a value indicating whether the record moved on before this write arrived.</summary>
    public bool IsVersionConflict => string.Equals(Code, "version_conflict", StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether an identical submission is still being processed.</summary>
    public bool IsInProgress => string.Equals(Code, "idempotency_in_progress", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether retrying could plausibly succeed.
    /// </summary>
    /// <remarks>
    /// A refusal - unauthorized, forbidden, invalid, conflicting - will refuse
    /// again. A transport failure or a server fault may not. The write queue uses
    /// this to decide between waiting and asking a person.
    /// </remarks>
    public bool IsRetryable =>
        IsInProgress
        || StatusCode >= HttpStatusCode.InternalServerError
        || StatusCode == HttpStatusCode.RequestTimeout
        || StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Gets a value indicating whether the build must be updated before it may write.
    /// </summary>
    /// <remarks>
    /// 426 is the server saying this client is incompatible; 403 with a revoked
    /// policy is the server saying it is withdrawn. Both mean the same thing to a
    /// user: this build cannot change anything until it is updated.
    /// </remarks>
    public bool RequiresClientUpdate => StatusCode == HttpStatusCode.UpgradeRequired;
}

/// <summary>The API surface the Windows client depends on.</summary>
/// <remarks>
/// An interface so view models can be tested without a server. The Windows client
/// never talks to PostgreSQL; it talks to this, which talks to the versioned HTTP
/// contract.
/// </remarks>
public partial interface IAgencyOsApi
{
    Task<HandshakeResponse> HandshakeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonSummaryResponse>> ListPeopleAsync(
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<PersonDetailResponse> GetPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <param name="request">The person to create.</param>
    /// <param name="idempotencyKey">
    /// Stable across every retry of this command, so a submission that follows a
    /// lost response is recognized as the same one rather than creating a second
    /// person. Null for an interactive request the user can simply repeat.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<PersonDetailResponse> CreatePersonAsync(
        CreatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineEntryResponse>> GetPersonTimelineAsync(
        Guid personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<CompanyDetailResponse> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<CompanyDetailResponse> CreateCompanyAsync(
        CreateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<CompanyDetailResponse> UpdateCompanyAsync(
        Guid companyId,
        UpdateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineEntryResponse>> GetCompanyTimelineAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateRelationshipAsync(
        CreateRelationshipRequest request,
        CancellationToken cancellationToken = default);

    Task EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default);

    Task<RecordInteractionResponse> RecordInteractionAsync(
        RecordInteractionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateTaskAsync(
        CreateTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ReopenTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default);

    // ---- Search, saved views and synchronization (M3) ----

    Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string>? types = null,
        bool includeArchived = false,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedViewResponse>> ListSavedViewsAsync(CancellationToken cancellationToken = default);

    Task<SavedViewResponse> CreateSavedViewAsync(
        CreateSavedViewRequest request,
        CancellationToken cancellationToken = default);

    Task<SavedViewResponse> UpdateSavedViewAsync(
        Guid savedViewId,
        UpdateSavedViewRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteSavedViewAsync(Guid savedViewId, CancellationToken cancellationToken = default);

    /// <summary>Runs a saved view, which re-checks the caller's permission server-side.</summary>
    Task<SavedViewResultsResponse> RunSavedViewAsync(
        Guid savedViewId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<SyncChangesResponse> ReadSyncChangesAsync(
        long cursor,
        int? take = null,
        CancellationToken cancellationToken = default);

    // ---- Talent and representation (M4) ----

    /// <param name="clientsOnly">Only people with an active representation.</param>
    /// <param name="formerClientsOnly">Only people whose representation has ended.</param>
    /// <param name="discipline">Restrict to one discipline.</param>
    /// <param name="scope">Restrict to one represented area.</param>
    /// <param name="leadUserId">Restrict to one internal owner.</param>
    /// <param name="search">Substring match on the person's name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<TalentSummaryResponse>> ListTalentAsync(
        bool clientsOnly = false,
        bool formerClientsOnly = false,
        string? discipline = null,
        string? scope = null,
        Guid? leadUserId = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<TalentDetailResponse> GetTalentAsync(Guid personId, CancellationToken cancellationToken = default);

    Task<TalentDetailResponse> CreateTalentProfileAsync(
        CreateTalentProfileRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<ClientOverviewResponse> GetClientOverviewAsync(
        Guid personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepresentationHistoryEntryResponse>> GetRepresentationHistoryAsync(
        Guid personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProspectResponse>> ListProspectsAsync(
        bool openOnly = true,
        string? stage = null,
        Guid? ownerUserId = null,
        DateOnly? dueOnOrBefore = null,
        CancellationToken cancellationToken = default);

    Task<ProspectResponse> GetProspectAsync(Guid prospectId, CancellationToken cancellationToken = default);

    Task<ProspectResponse> CreateProspectAsync(
        CreateProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task AdvanceProspectAsync(
        Guid prospectId,
        AdvanceProspectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Converts a prospect. Safe to retry under an idempotency key.</summary>
    Task<RepresentationResponse> ConvertProspectAsync(
        Guid prospectId,
        ConvertProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<RepresentationResponse> GetRepresentationAsync(
        Guid representationId,
        CancellationToken cancellationToken = default);

    Task TransitionRepresentationAsync(
        Guid representationId,
        TransitionRepresentationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditResponse>> ListCreditsAsync(
        Guid personId,
        CancellationToken cancellationToken = default);

    Task<Guid> AddCreditAsync(
        AddCreditRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialResponse>> ListMaterialsAsync(
        Guid personId,
        CancellationToken cancellationToken = default);

    Task<Guid> AddMaterialAsync(
        AddMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    // ---- Projects and packaging (M5) ----

    /// <param name="status">Restrict to one operational status.</param>
    /// <param name="stage">Restrict to one development stage.</param>
    /// <param name="type">Restrict to one kind of work.</param>
    /// <param name="leadUserId">Restrict to one internal owner.</param>
    /// <param name="missingRole">Only projects nobody currently holds this role on.</param>
    /// <param name="search">Substring match on title and working title.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<ProjectSummaryResponse>> ListProjectsAsync(
        string? status = null,
        string? stage = null,
        string? type = null,
        Guid? leadUserId = null,
        string? missingRole = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<ProjectDetailResponse> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectDetailResponse> CreateProjectAsync(
        CreateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateProjectAsync(
        Guid projectId,
        UpdateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeProjectStatusAsync(
        Guid projectId,
        ChangeProjectStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeProjectStageAsync(
        Guid projectId,
        ChangeProjectStageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectHistoryEntryResponse>> GetProjectHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateProjectRoleAsync(
        Guid projectId,
        CreateProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeProjectRoleAsync(
        Guid projectId,
        Guid roleId,
        ChangeProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<Guid> AttachToRoleAsync(
        Guid projectId,
        Guid roleId,
        AttachToRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeAttachmentAsync(
        Guid projectId,
        Guid attachmentId,
        ChangeAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<Guid> AddProjectCompanyAsync(
        Guid projectId,
        AddProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task EndProjectCompanyAsync(
        Guid projectId,
        Guid participationId,
        EndProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourcePropertyResponse>> ListSourcePropertiesAsync(
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<SourcePropertyResponse> CreateSourcePropertyAsync(
        CreateSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task LinkSourcePropertyAsync(
        Guid projectId,
        LinkSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task LinkMaterialToProjectAsync(
        Guid projectId,
        LinkProjectMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageSummaryResponse>> ListPackagesAsync(
        string? status = null,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<PackageDetailResponse> GetPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);

    Task<PackageDetailResponse> CreatePackageAsync(
        CreatePackageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangePackageStatusAsync(
        Guid packageId,
        ChangePackageStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<Guid> AddPackageElementAsync(
        Guid packageId,
        AddPackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task RemovePackageElementAsync(
        Guid packageId,
        Guid elementId,
        RemovePackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<ProjectCommandCenterResponse> GetProjectCommandCenterAsync(
        CancellationToken cancellationToken = default);

    // ---- Opportunities and submissions (M6) ----

    /// <param name="status">Restrict to one status.</param>
    /// <param name="kind">Restrict to one kind of pursuit.</param>
    /// <param name="ownerUserId">Restrict to one internal owner.</param>
    /// <param name="awaitingResponse">Only pursuits with a reply overdue.</param>
    /// <param name="search">Substring match on name and description.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<OpportunitySummaryResponse>> ListOpportunitiesAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool awaitingResponse = false,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<OpportunityDetailResponse> GetOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<OpportunityDetailResponse> CreateOpportunityAsync(
        CreateOpportunityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeOpportunityStatusAsync(
        Guid opportunityId,
        ChangeOpportunityStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpportunityHistoryEntryResponse>> GetOpportunityHistoryAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<Guid> AddOpportunityTargetAsync(
        Guid opportunityId,
        AddOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<OpportunityTargetResponse> GetOpportunityTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default);

    Task MoveOpportunityTargetAsync(
        Guid targetId,
        MoveOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task RecordTargetResponseAsync(
        Guid targetId,
        RecordTargetResponseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<RecordSubmissionResponse> RecordSubmissionAsync(
        Guid targetId,
        RecordSubmissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<RecordPitchResponse> RecordPitchAsync(
        Guid targetId,
        RecordPitchRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(
        Guid? opportunityId = null,
        Guid? targetId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineColumnResponse>> GetPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    Task<OpportunityCommandCenterResponse> GetOpportunityCommandCenterAsync(
        CancellationToken cancellationToken = default);

    // ---- Deals and offers (M7) ----
    //
    // Every write here is online-only. None of it is queued, because a stale
    // offer or acceptance replayed hours later causes commercial harm in the world
    // that no later synchronization repairs (ADR-0021).

    /// <param name="status">Restrict to one status.</param>
    /// <param name="kind">Restrict to one kind of transaction.</param>
    /// <param name="ownerUserId">Restrict to one internal owner.</param>
    /// <param name="hasOpenOffer">Only negotiations with an offer awaiting an answer.</param>
    /// <param name="termsAgreed">Only negotiations whose commercial terms are settled.</param>
    /// <param name="search">Substring match on name, reference and summary.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<DealSummaryResponse>> ListDealsAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool hasOpenOffer = false,
        bool termsAgreed = false,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<DealDetailResponse> GetDealAsync(
        Guid dealId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealHistoryEntryResponse>> GetDealHistoryAsync(
        Guid dealId,
        CancellationToken cancellationToken = default);

    Task<DealDetailResponse> CreateDealAsync(
        CreateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateDealAsync(
        Guid dealId,
        UpdateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task CloseDealAsync(
        Guid dealId,
        CloseDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ReopenNegotiationAsync(
        Guid dealId,
        ReopenNegotiationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfferResponse>> ListOffersAsync(
        Guid? dealId = null,
        CancellationToken cancellationToken = default);

    Task<OfferResponse> GetOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an offer as made or received. AgencyOS does not send it.
    /// </summary>
    Task<RecordOfferResponse> RecordOfferAsync(
        Guid dealId,
        RecordOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<AnswerOfferResponse> AnswerOfferAsync(
        Guid offerId,
        AnswerOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two offers in the same negotiation.
    /// </summary>
    /// <remarks>
    /// Requires <c>deals.economics.read</c> and is refused without it, unlike every
    /// other read, which redacts. A diff with the economic rows removed would say
    /// nothing changed when the number moved.
    /// </remarks>
    Task<OfferComparisonResponse> CompareOffersAsync(
        Guid dealId,
        Guid previousOfferId,
        Guid currentOfferId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealPipelineColumnResponse>> GetDealPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    Task<DealCommandCenterResponse> GetDealCommandCenterAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The supported commercial terms, so a term editor need not hard-code them.</summary>
    Task<IReadOnlyList<DealTermDefinitionResponse>> ListDealTermsAsync(
        CancellationToken cancellationToken = default);

    // ---- Contracts, rights, options and obligations (M8) ----

    /// <param name="status">Restrict to one status.</param>
    /// <param name="kind">Restrict to one kind of instrument.</param>
    /// <param name="dealId">Only contracts papering this negotiation.</param>
    /// <param name="awaitingSignature">Only contracts with a required signature outstanding.</param>
    /// <param name="effectiveOnly">Only contracts in force today.</param>
    /// <param name="hasUnresolvedReconciliation">
    /// Only contracts whose newest version differs from what was agreed.
    /// </param>
    /// <param name="search">Substring match on title and reference.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<ContractSummaryResponse>> ListContractsAsync(
        string? status = null,
        string? kind = null,
        Guid? dealId = null,
        bool awaitingSignature = false,
        bool effectiveOnly = false,
        bool hasUnresolvedReconciliation = false,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<ContractDetailResponse> GetContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContractHistoryEntryResponse>> GetContractHistoryAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);

    Task<ContractDetailResponse> CreateContractAsync(
        CreateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UpdateContractAsync(
        Guid contractId,
        UpdateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a contract through its drafting lifecycle.
    /// </summary>
    /// <remarks>
    /// Cannot reach execution. A contract becomes partially or fully executed by
    /// recording the signatures it requires, and nothing else produces those
    /// statuses.
    /// </remarks>
    Task ChangeContractStatusAsync(
        Guid contractId,
        ChangeContractStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task RecordContractEffectiveDateAsync(
        Guid contractId,
        RecordEffectiveDateRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<AddContractPartyResponse> AddContractPartyAsync(
        Guid contractId,
        AddContractPartyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a party signed. AgencyOS verifies nothing.
    /// </summary>
    Task<RecordSignatureResponse> RecordContractSignatureAsync(
        Guid contractId,
        RecordSignatureRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task RecordContractRelationshipAsync(
        Guid contractId,
        RecordContractRelationshipRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a drafting version. It does not upload the document.
    /// </summary>
    Task<RecordContractVersionResponse> RecordContractVersionAsync(
        Guid contractId,
        RecordContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<ContractVersionResponse> GetContractVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task ChangeContractTermAsync(
        Guid versionId,
        ChangeContractTermRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task FinaliseContractVersionAsync(
        Guid versionId,
        FinaliseContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares a drafting version against the offer the contract papers.
    /// </summary>
    /// <remarks>
    /// Requires <c>contracts.terms.read</c> and <c>deals.economics.read</c>, and is
    /// refused without either, unlike every other read, which redacts. A comparison
    /// with the terms stripped out would report that the draft matched what was
    /// agreed when it did not.
    /// </remarks>
    Task<ReconciliationResponse> ReconcileContractVersionAsync(
        Guid contractId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RightsGrantResponse>> ListRightsGrantsAsync(
        Guid? contractId = null,
        Guid? projectId = null,
        bool currentOnly = true,
        CancellationToken cancellationToken = default);

    Task<RecordRightsGrantResponse> RecordRightsGrantAsync(
        Guid contractId,
        RecordRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task EndRightsGrantAsync(
        Guid grantId,
        EndRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContractOptionResponse>> ListContractOptionsAsync(
        Guid? contractId = null,
        string? status = null,
        bool exercisableOnly = false,
        bool pastDeadlineOnly = false,
        CancellationToken cancellationToken = default);

    Task<RecordOptionResponse> RecordContractOptionAsync(
        Guid contractId,
        RecordOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what became of an option.
    /// </summary>
    /// <remarks>
    /// Every outcome is an act somebody performed, expiry included. Nothing lapses
    /// because a date passed.
    /// </remarks>
    Task<ResolveOptionResponse> ResolveContractOptionAsync(
        Guid optionId,
        ResolveOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObligationResponse>> ListObligationsAsync(
        Guid? contractId = null,
        string? status = null,
        bool outstandingOnly = false,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default);

    Task<RecordObligationResponse> RecordObligationAsync(
        Guid contractId,
        RecordObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what became of an obligation.
    /// </summary>
    /// <remarks>
    /// Breach requires a reason and never follows from a due date passing. Past due
    /// is a fact the server derives; breach is a determination a person makes.
    /// </remarks>
    Task<ResolveObligationResponse> ResolveObligationAsync(
        Guid obligationId,
        ResolveObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<RecordNoticeRequirementResponse> RecordNoticeRequirementAsync(
        Guid contractId,
        RecordNoticeRequirementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a notice passed between the parties. AgencyOS sends nothing.
    /// </summary>
    Task<RecordNoticeResponse> RecordNoticeAsync(
        Guid contractId,
        RecordNoticeRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<CreateContractTaskResponse> CreateContractTaskAsync(
        Guid contractId,
        CreateContractTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LegalDeadlineResponse>> ListLegalDeadlinesAsync(
        int? withinDays = null,
        CancellationToken cancellationToken = default);

    Task<ContractCommandCenterResponse> GetLegalCommandCenterAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The supported contract terms, so a term editor need not hard-code them.</summary>
    Task<IReadOnlyList<ContractTermDefinitionResponse>> ListContractTermsAsync(
        CancellationToken cancellationToken = default);

    // ---- Finance, commissions, receivables, payments and ledger (M9) ----
    //
    // Every method here goes to the server and waits. None is queued.
    // docs/13_OFFLINE_CLASSIFICATION.md classifies the whole milestone
    // ONLINE_ONLY, reads included: a balance computed from a cache that is four
    // hours stale is not a slightly old balance, it is a different number, and the
    // person reading it has no way to tell which they are looking at (ADR-0023).

    Task<IReadOnlyList<MonetaryObligationResponse>> ListMonetaryObligationsAsync(
        Guid? contractId = null,
        bool unbilledOnly = false,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<MonetaryObligationResponse> GetMonetaryObligationAsync(
        Guid obligationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sum an operative contract says is payable.
    /// </summary>
    /// <remarks>
    /// The server refuses unless the contract is operative. Agreed commercial terms
    /// are not a collectible legal amount, and no client flag overrides that.
    /// </remarks>
    Task<RecordMonetaryObligationResponse> RecordMonetaryObligationAsync(
        Guid contractId,
        RecordMonetaryObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task QuantifyObligationAsync(
        Guid obligationId,
        QuantifyObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ReleaseObligationAsync(
        Guid obligationId,
        ReleaseObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <param name="status">Open, PartiallyPaid, Paid, Cancelled or WrittenOff.</param>
    /// <param name="beneficiary">Client or Agency.</param>
    /// <param name="overdueOnly">Only rows past a resolvable date with something owed.</param>
    /// <param name="unreconciledOnly">Only rows whose arithmetic does not yet explain itself.</param>
    /// <param name="currency">One currency. There is no rate that would let two be added.</param>
    Task<IReadOnlyList<ReceivableResponse>> ListReceivablesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? payerPartyId = null,
        Guid? clientPersonId = null,
        string? beneficiary = null,
        bool overdueOnly = false,
        bool unreconciledOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<ReceivableResponse> GetReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default);

    Task<RaiseReceivableResponse> RaiseReceivableAsync(
        Guid obligationId,
        RaiseReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives up on collecting what remains.
    /// </summary>
    /// <remarks>
    /// A financial act with a reason and a posting, never data cleanup. The
    /// original amount stays exactly what it was.
    /// </remarks>
    Task WriteOffReceivableAsync(
        Guid receivableId,
        WriteOffReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task CancelReceivableAsync(
        Guid receivableId,
        CancelReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a deduction that reduces what will ever arrive.
    /// </summary>
    /// <remarks>
    /// A fact somebody entered, never an inference. A gap with no adjustment
    /// against it stays a gap.
    /// </remarks>
    Task<RecordAdjustmentResponse> RecordAdjustmentAsync(
        Guid receivableId,
        RecordAdjustmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<ReceivableReconciliationResponse> ReconcileReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceResponse>> ListInvoicesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? debtorPartyId = null,
        bool overdueOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<InvoiceResponse> GetInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an invoice against receivables that already exist.
    /// </summary>
    /// <remarks>
    /// Records one. Does not send one: there is no transport anywhere in the client
    /// or the server, and the verb says so.
    /// </remarks>
    Task<RecordInvoiceResponse> RecordInvoiceAsync(
        Guid contractId,
        RecordInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task IssueInvoiceAsync(
        Guid invoiceId,
        IssueInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(
        string? direction = null,
        string? status = null,
        Guid? payerPartyId = null,
        bool unappliedOnly = false,
        DateOnly? recordedAfter = null,
        DateOnly? recordedBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<PaymentResponse> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that money moved.
    /// </summary>
    /// <remarks>
    /// The response carries what was allocated and what was not. Anything left over
    /// stays unapplied: nothing is matched to whichever receivable looks closest,
    /// because that would be the system guessing at intent and acting on it.
    /// </remarks>
    Task<RecordPaymentResponse> RecordPaymentAsync(
        RecordPaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<RecordPaymentResponse> AllocatePaymentAsync(
        Guid paymentId,
        AllocatePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ReverseAllocationAsync(
        Guid paymentId,
        ReverseAllocationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<ReversePaymentResponse> ReversePaymentAsync(
        Guid paymentId,
        ReversePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionRuleResponse>> ListCommissionRulesAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        CancellationToken cancellationToken = default);

    Task<CreateCommissionRuleResponse> CreateCommissionRuleAsync(
        CreateCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task EndCommissionRuleAsync(
        Guid ruleId,
        EndCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionEntitlementResponse>> ListCommissionsAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        Guid? representationId = null,
        string? status = null,
        bool outstandingOnly = false,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<CommissionEntitlementResponse> GetCommissionAsync(
        Guid commissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Works out what the agency is entitled to against an obligation.
    /// </summary>
    /// <remarks>
    /// The governing date defaults to when the obligation falls due, not to today.
    /// Recalculating a 2027 commission in 2029 gives the 2027 answer.
    /// </remarks>
    Task<CalculateCommissionResponse> CalculateCommissionAsync(
        Guid obligationId,
        CalculateCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task AdjustCommissionAsync(
        Guid commissionId,
        AdjustCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountResponse>> ListLedgerAccountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Account balances, per currency and never summed across them.</summary>
    Task<IReadOnlyList<AccountBalanceResponse>> GetLedgerBalancesAsync(
        string? currency = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JournalEntryResponse>> ListJournalEntriesAsync(
        string? status = null,
        string? source = null,
        Guid? accountId = null,
        DateOnly? postedAfter = null,
        DateOnly? postedBefore = null,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<JournalEntryResponse> GetJournalEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a balanced entry somebody wrote by hand.</summary>
    Task<PostJournalEntryResponse> PostJournalEntryAsync(
        PostJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the entry that undoes a posted one.
    /// </summary>
    /// <remarks>
    /// A posted entry is never edited. A correction is another entry saying the
    /// opposite, and both stay readable.
    /// </remarks>
    Task<ReverseJournalEntryResponse> ReverseJournalEntryAsync(
        Guid entryId,
        ReverseJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The curated financial history.
    /// </summary>
    /// <remarks>
    /// One business act, one entry, however many rows it wrote. Never raw audit
    /// rows, which answer a security question in a security vocabulary.
    /// </remarks>
    Task<IReadOnlyList<FinanceHistoryEntryResponse>> GetFinanceHistoryAsync(
        Guid? contractId = null,
        Guid? receivableId = null,
        CancellationToken cancellationToken = default);

    Task<FinanceCommandCenterResponse> GetFinanceCommandCenterAsync(
        CancellationToken cancellationToken = default);

    // ---- Documents and communications (M10) ----
    //
    // Every method here goes to the server and waits. Nothing is queued and nothing
    // is cached: docs/13_OFFLINE_CLASSIFICATION.md classifies the whole milestone
    // ONLINE_ONLY, and an actual send is never held in the M3 offline queue - a
    // message queued for four hours is a message somebody has already been told was
    // sent (ADR-0028).

    Task<IReadOnlyList<DocumentSummaryResponse>> ListDocumentsAsync(
        string? kind = null,
        string? status = null,
        string? sensitivity = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool hasContent = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<DocumentDetailResponse> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a document and its first version.
    /// </summary>
    /// <remarks>
    /// Multipart, so the bytes stream rather than being base64-encoded into a JSON
    /// body. The digest comes back from the server, computed as it stored them
    /// (ADR-0024).
    /// </remarks>
    Task<RecordDocumentResponse> RecordDocumentAsync(
        RecordDocumentRequest request,
        Stream content,
        string fileName,
        string? mediaType = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a version. Never replaces one.</summary>
    Task<RecordDocumentResponse> AddDocumentVersionAsync(
        Guid documentId,
        Stream content,
        string fileName,
        int expectedVersion,
        string? mediaType = null,
        string? notes = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a version's bytes.</summary>
    /// <remarks>
    /// The only route to stored content. There is no public URL and no path in any
    /// response (ADR-0025).
    /// </remarks>
    Task<DocumentContent> DownloadDocumentVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task UpdateDocumentAsync(
        Guid documentId,
        UpdateDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<LinkDocumentResponse> LinkDocumentAsync(
        Guid documentId,
        LinkDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UnlinkDocumentAsync(
        Guid documentId,
        Guid linkId,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a document out of ordinary use. Destroys nothing.</summary>
    Task ArchiveDocumentAsync(
        Guid documentId,
        ArchiveDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task RestoreDocumentAsync(
        Guid documentId,
        RestoreDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks which providers this server can connect to, and where to authorize them.
    /// </summary>
    /// <remarks>
    /// The client does not build an authorization URL itself. Doing so would mean
    /// carrying the application registration in the Windows build, and that
    /// registration is server configuration (ADR-0027).
    /// </remarks>
    Task<IReadOnlyList<CommunicationProviderResponse>> ListCommunicationProvidersAsync(
        string redirectUri,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommunicationAccountResponse>> ListCommunicationAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<ConnectMailboxResponse> ConnectMailboxAsync(
        ConnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task DisconnectMailboxAsync(
        Guid accountId,
        DisconnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task ChangeMailboxVisibilityAsync(
        Guid accountId,
        ChangeMailboxVisibilityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MessageSummaryResponse>> ListMessagesAsync(
        Guid? accountId = null,
        string? direction = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool unlinkedOnly = false,
        bool hasAttachments = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<MessageDetailResponse> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<LinkMessageResponse> LinkMessageAsync(
        Guid messageId,
        LinkMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task UnlinkMessageAsync(
        Guid messageId,
        Guid linkId,
        CancellationToken cancellationToken = default);

    /// <summary>Records that an address is a person or company AgencyOS knows.</summary>
    Task ResolveParticipantAsync(
        Guid messageId,
        Guid participantId,
        ResolveParticipantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ParticipantSuggestionResponse>> SuggestParticipantsAsync(
        string address,
        CancellationToken cancellationToken = default);

    /// <summary>Pulls an attachment's bytes into the canonical document store.</summary>
    Task<IngestAttachmentResponse> IngestAttachmentAsync(
        Guid attachmentId,
        IngestAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundDispatchResponse>> ListOutboundMessagesAsync(
        string? state = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<OutboundDispatchResponse> GetOutboundMessageAsync(
        Guid dispatchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the intent to send.
    /// </summary>
    /// <remarks>
    /// Nothing has left. The message can still be cancelled until it is queued,
    /// after which AgencyOS cannot unsend anything (ADR-0028).
    /// </remarks>
    Task<ComposeMessageResponse> ComposeMessageAsync(
        ComposeMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task QueueMessageAsync(
        Guid dispatchId,
        QueueMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task CancelMessageAsync(
        Guid dispatchId,
        CancelMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommunicationEventResponse>> GetCommunicationHistoryAsync(
        Guid? accountId = null,
        Guid? dispatchId = null,
        CancellationToken cancellationToken = default);

    Task<CommunicationCommandCenterResponse> GetCommunicationCommandCenterAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Bytes streamed back from the server, with what a caller needs to save them.</summary>
/// <param name="ContentHash">
/// The digest the server holds, so a caller can verify the file it received.
/// </param>
public sealed record DocumentContent(
    Stream Content,
    string FileName,
    string MediaType,
    long? ByteLength,
    string? ContentHash);

/// <summary>
/// Typed HTTP client for the AgencyOS API.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than generated. The contract types already exist in
/// <c>AgencyOS.Contracts</c> and are shared by both sides; generating a client
/// would produce a second, structurally identical set of DTOs that could drift
/// from the first. A generator earns its place when the client cannot share the
/// server's types, which is not the situation here.
/// </para>
/// <para>
/// Every request carries the release identity headers, so the server can govern
/// this build exactly as <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c> requires. The
/// client does not decide whether it is allowed to act; it presents who it is and
/// the server decides.
/// </para>
/// </remarks>
public sealed partial class AgencyOsApiClient : IAgencyOsApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AgencyOsSession _session;

    public AgencyOsApiClient(HttpClient http, AgencyOsSession session)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        _http = http;
        _session = session;

        _http.BaseAddress ??= session.BaseAddress;

        ApplyIdentityHeaders();
    }

    private string TenantRoot =>
        $"/api/v1/organizations/{_session.OrganizationId.ToString("D", CultureInfo.InvariantCulture)}";

    public async Task<HandshakeResponse> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        HandshakeRequest request = new(
            _session.Platform,
            _session.Channel,
            _session.ClientVersion,
            ApiContract.Current,
            BuildInfo.BuildId,
            BuildInfo.GitCommit);

        return await PostAsync<HandshakeRequest, HandshakeResponse>(
            "/api/v1/release/handshake",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PersonSummaryResponse>> ListPeopleAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        string query = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : $"?search={Uri.EscapeDataString(search.Trim())}";

        return GetListAsync<PersonSummaryResponse>($"{TenantRoot}/people{query}", cancellationToken);
    }

    public Task<PersonDetailResponse> GetPersonAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetAsync<PersonDetailResponse>($"{TenantRoot}/people/{personId}", cancellationToken);

    public Task<PersonDetailResponse> CreatePersonAsync(
        CreatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreatePersonRequest, PersonDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/people",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdatePersonRequest, PersonDetailResponse>(
            HttpMethod.Put,
            $"{TenantRoot}/people/{personId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<TimelineEntryResponse>> GetPersonTimelineAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<TimelineEntryResponse>($"{TenantRoot}/people/{personId}/timeline", cancellationToken);

    public Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        string query = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : $"?search={Uri.EscapeDataString(search.Trim())}";

        return GetListAsync<CompanySummaryResponse>($"{TenantRoot}/companies{query}", cancellationToken);
    }

    public Task<CompanyDetailResponse> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<CompanyDetailResponse>($"{TenantRoot}/companies/{companyId}", cancellationToken);

    public Task<CompanyDetailResponse> CreateCompanyAsync(
        CreateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateCompanyRequest, CompanyDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/companies",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<CompanyDetailResponse> UpdateCompanyAsync(
        Guid companyId,
        UpdateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdateCompanyRequest, CompanyDetailResponse>(
            HttpMethod.Put,
            $"{TenantRoot}/companies/{companyId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<TimelineEntryResponse>> GetCompanyTimelineAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<TimelineEntryResponse>($"{TenantRoot}/companies/{companyId}/timeline", cancellationToken);

    public async Task<Guid> CreateRelationshipAsync(
        CreateRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await PostAsync<CreateRelationshipRequest, CreatedIdResponse>(
            $"{TenantRoot}/relationships",
            request,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/relationships/{relationshipId}/end",
            new EndRelationshipRequest(),
            idempotencyKey: null,
            cancellationToken);

    public Task<RecordInteractionResponse> RecordInteractionAsync(
        RecordInteractionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordInteractionRequest, RecordInteractionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/interactions",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default) =>
        GetListAsync<TaskResponse>(
            $"{TenantRoot}/tasks?openOnly={(openOnly ? "true" : "false")}",
            cancellationToken);

    public async Task<Guid> CreateTaskAsync(
        CreateTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<CreateTaskRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/tasks",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task CompleteTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/tasks/{taskId}/complete",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ReopenTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/tasks/{taskId}/reopen",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CommandCenterResponse>($"{TenantRoot}/command-center", cancellationToken);

    // ---- Search, saved views and synchronization (M3) ----

    public Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string>? types = null,
        bool includeArchived = false,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/search"
            + $"?q={Uri.EscapeDataString(query ?? string.Empty)}"
            + $"&includeArchived={(includeArchived ? "true" : "false")}"
            + $"&skip={skip.ToString(CultureInfo.InvariantCulture)}"
            + $"&take={take.ToString(CultureInfo.InvariantCulture)}";

        if (types is { Count: > 0 })
        {
            uri += $"&types={Uri.EscapeDataString(string.Join(',', types))}";
        }

        return GetAsync<SearchResponse>(uri, cancellationToken);
    }

    public Task<IReadOnlyList<SavedViewResponse>> ListSavedViewsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<SavedViewResponse>($"{TenantRoot}/saved-views", cancellationToken);

    public Task<SavedViewResponse> CreateSavedViewAsync(
        CreateSavedViewRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateSavedViewRequest, SavedViewResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/saved-views",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<SavedViewResponse> UpdateSavedViewAsync(
        Guid savedViewId,
        UpdateSavedViewRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdateSavedViewRequest, SavedViewResponse>(
            HttpMethod.Put,
            $"{TenantRoot}/saved-views/{savedViewId}",
            request,
            idempotencyKey: null,
            cancellationToken);

    public async Task DeleteSavedViewAsync(Guid savedViewId, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"{TenantRoot}/saved-views/{savedViewId}");

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<SavedViewResultsResponse> RunSavedViewAsync(
        Guid savedViewId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/saved-views/{savedViewId}/results";

        if (limit is { } size)
        {
            uri += $"?limit={size.ToString(CultureInfo.InvariantCulture)}";
        }

        return GetAsync<SavedViewResultsResponse>(uri, cancellationToken);
    }

    // ---- Talent and representation (M4) ----

    public Task<IReadOnlyList<TalentSummaryResponse>> ListTalentAsync(
        bool clientsOnly = false,
        bool formerClientsOnly = false,
        string? discipline = null,
        string? scope = null,
        Guid? leadUserId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/talent"
            + $"?clientsOnly={(clientsOnly ? "true" : "false")}"
            + $"&formerClientsOnly={(formerClientsOnly ? "true" : "false")}";

        if (!string.IsNullOrWhiteSpace(discipline))
        {
            uri += $"&discipline={Uri.EscapeDataString(discipline)}";
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            uri += $"&scope={Uri.EscapeDataString(scope)}";
        }

        if (leadUserId is { } lead)
        {
            uri += $"&leadUserId={lead}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            uri += $"&search={Uri.EscapeDataString(search.Trim())}";
        }

        return GetListAsync<TalentSummaryResponse>(uri, cancellationToken);
    }

    public Task<TalentDetailResponse> GetTalentAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetAsync<TalentDetailResponse>($"{TenantRoot}/talent/{personId}", cancellationToken);

    public Task<TalentDetailResponse> CreateTalentProfileAsync(
        CreateTalentProfileRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateTalentProfileRequest, TalentDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/talent",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ClientOverviewResponse> GetClientOverviewAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ClientOverviewResponse>($"{TenantRoot}/talent/{personId}/overview", cancellationToken);

    public Task<IReadOnlyList<RepresentationHistoryEntryResponse>> GetRepresentationHistoryAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<RepresentationHistoryEntryResponse>(
            $"{TenantRoot}/talent/{personId}/history",
            cancellationToken);

    public Task<IReadOnlyList<ProspectResponse>> ListProspectsAsync(
        bool openOnly = true,
        string? stage = null,
        Guid? ownerUserId = null,
        DateOnly? dueOnOrBefore = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/prospects?openOnly={(openOnly ? "true" : "false")}";

        if (!string.IsNullOrWhiteSpace(stage))
        {
            uri += $"&stage={Uri.EscapeDataString(stage)}";
        }

        if (ownerUserId is { } owner)
        {
            uri += $"&ownerUserId={owner}";
        }

        if (dueOnOrBefore is { } due)
        {
            uri += $"&dueOnOrBefore={due:yyyy-MM-dd}";
        }

        return GetListAsync<ProspectResponse>(uri, cancellationToken);
    }

    public Task<ProspectResponse> GetProspectAsync(Guid prospectId, CancellationToken cancellationToken = default) =>
        GetAsync<ProspectResponse>($"{TenantRoot}/prospects/{prospectId}", cancellationToken);

    public Task<ProspectResponse> CreateProspectAsync(
        CreateProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateProspectRequest, ProspectResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/prospects",
            request,
            idempotencyKey,
            cancellationToken);

    public Task AdvanceProspectAsync(
        Guid prospectId,
        AdvanceProspectRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/prospects/{prospectId}/advance",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<RepresentationResponse> ConvertProspectAsync(
        Guid prospectId,
        ConvertProspectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConvertProspectRequest, RepresentationResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/prospects/{prospectId}/convert",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RepresentationResponse> GetRepresentationAsync(
        Guid representationId,
        CancellationToken cancellationToken = default) =>
        GetAsync<RepresentationResponse>($"{TenantRoot}/representations/{representationId}", cancellationToken);

    public Task TransitionRepresentationAsync(
        Guid representationId,
        TransitionRepresentationRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/representations/{representationId}/transition",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<IReadOnlyList<CreditResponse>> ListCreditsAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<CreditResponse>($"{TenantRoot}/talent/{personId}/credits", cancellationToken);

    public async Task<Guid> AddCreditAsync(
        AddCreditRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AddCreditRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/credits",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task<IReadOnlyList<MaterialResponse>> ListMaterialsAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<MaterialResponse>($"{TenantRoot}/talent/{personId}/materials", cancellationToken);

    public async Task<Guid> AddMaterialAsync(
        AddMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AddMaterialRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/materials",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    // ---- Projects and packaging (M5) ----

    public Task<IReadOnlyList<ProjectSummaryResponse>> ListProjectsAsync(
        string? status = null,
        string? stage = null,
        string? type = null,
        Guid? leadUserId = null,
        string? missingRole = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(stage))
        {
            query.Add($"stage={Uri.EscapeDataString(stage)}");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (leadUserId is { } lead)
        {
            query.Add($"leadUserId={lead}");
        }

        if (!string.IsNullOrWhiteSpace(missingRole))
        {
            query.Add($"missingRole={Uri.EscapeDataString(missingRole)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        string uri = $"{TenantRoot}/projects";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<ProjectSummaryResponse>(uri, cancellationToken);
    }

    public Task<ProjectDetailResponse> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ProjectDetailResponse>($"{TenantRoot}/projects/{projectId}", cancellationToken);

    public Task<ProjectDetailResponse> CreateProjectAsync(
        CreateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateProjectRequest, ProjectDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/projects",
            request,
            idempotencyKey,
            cancellationToken);

    public Task UpdateProjectAsync(
        Guid projectId,
        UpdateProjectRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Put,
            $"{TenantRoot}/projects/{projectId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangeProjectStatusAsync(
        Guid projectId,
        ChangeProjectStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/status",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangeProjectStageAsync(
        Guid projectId,
        ChangeProjectStageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/stage",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<ProjectHistoryEntryResponse>> GetProjectHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<ProjectHistoryEntryResponse>(
            $"{TenantRoot}/projects/{projectId}/history", cancellationToken);

    public async Task<Guid> CreateProjectRoleAsync(
        Guid projectId,
        CreateProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<CreateProjectRoleRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/roles",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task ChangeProjectRoleAsync(
        Guid projectId,
        Guid roleId,
        ChangeProjectRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/roles/{roleId}/change",
            request,
            idempotencyKey,
            cancellationToken);

    public async Task<Guid> AttachToRoleAsync(
        Guid projectId,
        Guid roleId,
        AttachToRoleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AttachToRoleRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/roles/{roleId}/attachments",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task ChangeAttachmentAsync(
        Guid projectId,
        Guid attachmentId,
        ChangeAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/attachments/{attachmentId}/status",
            request,
            idempotencyKey,
            cancellationToken);

    public async Task<Guid> AddProjectCompanyAsync(
        Guid projectId,
        AddProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AddProjectCompanyRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/companies",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task EndProjectCompanyAsync(
        Guid projectId,
        Guid participationId,
        EndProjectCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/companies/{participationId}/end",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<SourcePropertyResponse>> ListSourcePropertiesAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/source-properties";

        if (!string.IsNullOrWhiteSpace(search))
        {
            uri += $"?search={Uri.EscapeDataString(search)}";
        }

        return GetListAsync<SourcePropertyResponse>(uri, cancellationToken);
    }

    public Task<SourcePropertyResponse> CreateSourcePropertyAsync(
        CreateSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateSourcePropertyRequest, SourcePropertyResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/source-properties",
            request,
            idempotencyKey,
            cancellationToken);

    public Task LinkSourcePropertyAsync(
        Guid projectId,
        LinkSourcePropertyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/source-properties",
            request,
            idempotencyKey,
            cancellationToken);

    public Task LinkMaterialToProjectAsync(
        Guid projectId,
        LinkProjectMaterialRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/projects/{projectId}/materials",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<PackageSummaryResponse>> ListPackagesAsync(
        string? status = null,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (projectId is { } project)
        {
            query.Add($"projectId={project}");
        }

        string uri = $"{TenantRoot}/packages";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<PackageSummaryResponse>(uri, cancellationToken);
    }

    public Task<PackageDetailResponse> GetPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PackageDetailResponse>($"{TenantRoot}/packages/{packageId}", cancellationToken);

    public Task<PackageDetailResponse> CreatePackageAsync(
        CreatePackageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreatePackageRequest, PackageDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/packages",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangePackageStatusAsync(
        Guid packageId,
        ChangePackageStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/packages/{packageId}/status",
            request,
            idempotencyKey,
            cancellationToken);

    public async Task<Guid> AddPackageElementAsync(
        Guid packageId,
        AddPackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AddPackageElementRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/packages/{packageId}/elements",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task RemovePackageElementAsync(
        Guid packageId,
        Guid elementId,
        RemovePackageElementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/packages/{packageId}/elements/{elementId}/remove",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ProjectCommandCenterResponse> GetProjectCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<ProjectCommandCenterResponse>(
            $"{TenantRoot}/project-command-center", cancellationToken);

    // ---- Opportunities and submissions (M6) ----

    public Task<IReadOnlyList<OpportunitySummaryResponse>> ListOpportunitiesAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool awaitingResponse = false,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query.Add($"kind={Uri.EscapeDataString(kind)}");
        }

        if (ownerUserId is { } owner)
        {
            query.Add($"ownerUserId={owner}");
        }

        if (awaitingResponse)
        {
            query.Add("awaitingResponse=true");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        string uri = $"{TenantRoot}/opportunities";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<OpportunitySummaryResponse>(uri, cancellationToken);
    }

    public Task<OpportunityDetailResponse> GetOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default) =>
        GetAsync<OpportunityDetailResponse>(
            $"{TenantRoot}/opportunities/{opportunityId}", cancellationToken);

    public Task<OpportunityDetailResponse> CreateOpportunityAsync(
        CreateOpportunityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateOpportunityRequest, OpportunityDetailResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/opportunities",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangeOpportunityStatusAsync(
        Guid opportunityId,
        ChangeOpportunityStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/opportunities/{opportunityId}/status",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<OpportunityHistoryEntryResponse>> GetOpportunityHistoryAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<OpportunityHistoryEntryResponse>(
            $"{TenantRoot}/opportunities/{opportunityId}/history", cancellationToken);

    public async Task<Guid> AddOpportunityTargetAsync(
        Guid opportunityId,
        AddOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await SendAsync<AddOpportunityTargetRequest, CreatedIdResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/opportunities/{opportunityId}/targets",
            request,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task<OpportunityTargetResponse> GetOpportunityTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        GetAsync<OpportunityTargetResponse>(
            $"{TenantRoot}/opportunity-targets/{targetId}", cancellationToken);

    public Task MoveOpportunityTargetAsync(
        Guid targetId,
        MoveOpportunityTargetRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/opportunity-targets/{targetId}/stage",
            request,
            idempotencyKey,
            cancellationToken);

    public Task RecordTargetResponseAsync(
        Guid targetId,
        RecordTargetResponseRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/opportunity-targets/{targetId}/responses",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordSubmissionResponse> RecordSubmissionAsync(
        Guid targetId,
        RecordSubmissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordSubmissionRequest, RecordSubmissionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/opportunity-targets/{targetId}/submissions",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordPitchResponse> RecordPitchAsync(
        Guid targetId,
        RecordPitchRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordPitchRequest, RecordPitchResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/opportunity-targets/{targetId}/pitches",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(
        Guid? opportunityId = null,
        Guid? targetId = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (opportunityId is { } opportunity)
        {
            query.Add($"opportunityId={opportunity}");
        }

        if (targetId is { } target)
        {
            query.Add($"targetId={target}");
        }

        string uri = $"{TenantRoot}/submissions";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<SubmissionResponse>(uri, cancellationToken);
    }

    public Task<IReadOnlyList<PipelineColumnResponse>> GetPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/pipeline";

        if (ownerUserId is { } owner)
        {
            uri += $"?ownerUserId={owner}";
        }

        return GetListAsync<PipelineColumnResponse>(uri, cancellationToken);
    }

    public Task<OpportunityCommandCenterResponse> GetOpportunityCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<OpportunityCommandCenterResponse>(
            $"{TenantRoot}/opportunity-command-center", cancellationToken);

    // ---- Deals and offers (M7) ----

    public Task<IReadOnlyList<DealSummaryResponse>> ListDealsAsync(
        string? status = null,
        string? kind = null,
        Guid? ownerUserId = null,
        bool hasOpenOffer = false,
        bool termsAgreed = false,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query.Add($"kind={Uri.EscapeDataString(kind)}");
        }

        if (ownerUserId is { } owner)
        {
            query.Add($"ownerUserId={owner}");
        }

        if (hasOpenOffer)
        {
            query.Add("hasOpenOffer=true");
        }

        if (termsAgreed)
        {
            query.Add("termsAgreed=true");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        string uri = $"{TenantRoot}/deals";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<DealSummaryResponse>(uri, cancellationToken);
    }

    public Task<DealDetailResponse> GetDealAsync(
        Guid dealId,
        CancellationToken cancellationToken = default) =>
        GetAsync<DealDetailResponse>($"{TenantRoot}/deals/{dealId}", cancellationToken);

    public Task<IReadOnlyList<DealHistoryEntryResponse>> GetDealHistoryAsync(
        Guid dealId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<DealHistoryEntryResponse>(
            $"{TenantRoot}/deals/{dealId}/history", cancellationToken);

    public Task<DealDetailResponse> CreateDealAsync(
        CreateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateDealRequest, DealDetailResponse>(
            HttpMethod.Post, $"{TenantRoot}/deals", request, idempotencyKey, cancellationToken);

    public Task UpdateDealAsync(
        Guid dealId,
        UpdateDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Put,
            $"{TenantRoot}/deals/{dealId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task CloseDealAsync(
        Guid dealId,
        CloseDealRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/deals/{dealId}/close",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ReopenNegotiationAsync(
        Guid dealId,
        ReopenNegotiationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/deals/{dealId}/reopen",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<OfferResponse>> ListOffersAsync(
        Guid? dealId = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/offers";

        if (dealId is { } deal)
        {
            uri += $"?dealId={deal}";
        }

        return GetListAsync<OfferResponse>(uri, cancellationToken);
    }

    public Task<OfferResponse> GetOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default) =>
        GetAsync<OfferResponse>($"{TenantRoot}/offers/{offerId}", cancellationToken);

    public Task<RecordOfferResponse> RecordOfferAsync(
        Guid dealId,
        RecordOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordOfferRequest, RecordOfferResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/deals/{dealId}/offers",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<AnswerOfferResponse> AnswerOfferAsync(
        Guid offerId,
        AnswerOfferRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<AnswerOfferRequest, AnswerOfferResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/offers/{offerId}/answer",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<OfferComparisonResponse> CompareOffersAsync(
        Guid dealId,
        Guid previousOfferId,
        Guid currentOfferId,
        CancellationToken cancellationToken = default) =>
        GetAsync<OfferComparisonResponse>(
            $"{TenantRoot}/deals/{dealId}/comparison"
                + $"?previousOfferId={previousOfferId}&currentOfferId={currentOfferId}",
            cancellationToken);

    public Task<IReadOnlyList<DealPipelineColumnResponse>> GetDealPipelineAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/deal-pipeline";

        if (ownerUserId is { } owner)
        {
            uri += $"?ownerUserId={owner}";
        }

        return GetListAsync<DealPipelineColumnResponse>(uri, cancellationToken);
    }

    public Task<DealCommandCenterResponse> GetDealCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<DealCommandCenterResponse>(
            $"{TenantRoot}/deal-command-center", cancellationToken);

    public Task<IReadOnlyList<DealTermDefinitionResponse>> ListDealTermsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<DealTermDefinitionResponse>("/api/v1/deal-terms", cancellationToken);

    // ---- Contracts, rights, options and obligations (M8) ----

    public Task<IReadOnlyList<ContractSummaryResponse>> ListContractsAsync(
        string? status = null,
        string? kind = null,
        Guid? dealId = null,
        bool awaitingSignature = false,
        bool effectiveOnly = false,
        bool hasUnresolvedReconciliation = false,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query.Add($"kind={Uri.EscapeDataString(kind)}");
        }

        if (dealId is { } deal)
        {
            query.Add($"dealId={deal}");
        }

        if (awaitingSignature)
        {
            query.Add("awaitingSignature=true");
        }

        if (effectiveOnly)
        {
            query.Add("effectiveOnly=true");
        }

        if (hasUnresolvedReconciliation)
        {
            query.Add("hasUnresolvedReconciliation=true");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        string uri = $"{TenantRoot}/contracts";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<ContractSummaryResponse>(uri, cancellationToken);
    }

    public Task<ContractDetailResponse> GetContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ContractDetailResponse>($"{TenantRoot}/contracts/{contractId}", cancellationToken);

    public Task<IReadOnlyList<ContractHistoryEntryResponse>> GetContractHistoryAsync(
        Guid contractId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<ContractHistoryEntryResponse>(
            $"{TenantRoot}/contracts/{contractId}/history", cancellationToken);

    public Task<ContractDetailResponse> CreateContractAsync(
        CreateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateContractRequest, ContractDetailResponse>(
            HttpMethod.Post, $"{TenantRoot}/contracts", request, idempotencyKey, cancellationToken);

    public Task UpdateContractAsync(
        Guid contractId,
        UpdateContractRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Put,
            $"{TenantRoot}/contracts/{contractId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangeContractStatusAsync(
        Guid contractId,
        ChangeContractStatusRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/status",
            request,
            idempotencyKey,
            cancellationToken);

    public Task RecordContractEffectiveDateAsync(
        Guid contractId,
        RecordEffectiveDateRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/effective-date",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<AddContractPartyResponse> AddContractPartyAsync(
        Guid contractId,
        AddContractPartyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<AddContractPartyRequest, AddContractPartyResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/parties",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordSignatureResponse> RecordContractSignatureAsync(
        Guid contractId,
        RecordSignatureRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordSignatureRequest, RecordSignatureResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/signatures",
            request,
            idempotencyKey,
            cancellationToken);

    public Task RecordContractRelationshipAsync(
        Guid contractId,
        RecordContractRelationshipRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/relationships",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordContractVersionResponse> RecordContractVersionAsync(
        Guid contractId,
        RecordContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordContractVersionRequest, RecordContractVersionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/versions",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ContractVersionResponse> GetContractVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ContractVersionResponse>(
            $"{TenantRoot}/contract-versions/{versionId}", cancellationToken);

    public Task ChangeContractTermAsync(
        Guid versionId,
        ChangeContractTermRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/contract-versions/{versionId}/terms",
            request,
            idempotencyKey,
            cancellationToken);

    public Task FinaliseContractVersionAsync(
        Guid versionId,
        FinaliseContractVersionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/contract-versions/{versionId}/record",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ReconciliationResponse> ReconcileContractVersionAsync(
        Guid contractId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ReconciliationResponse>(
            $"{TenantRoot}/contracts/{contractId}/versions/{versionId}/reconciliation",
            cancellationToken);

    public Task<IReadOnlyList<RightsGrantResponse>> ListRightsGrantsAsync(
        Guid? contractId = null,
        Guid? projectId = null,
        bool currentOnly = true,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (contractId is { } contract)
        {
            query.Add($"contractId={contract}");
        }

        if (projectId is { } project)
        {
            query.Add($"projectId={project}");
        }

        if (!currentOnly)
        {
            query.Add("currentOnly=false");
        }

        string uri = $"{TenantRoot}/rights-grants";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<RightsGrantResponse>(uri, cancellationToken);
    }

    public Task<RecordRightsGrantResponse> RecordRightsGrantAsync(
        Guid contractId,
        RecordRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordRightsGrantRequest, RecordRightsGrantResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/rights-grants",
            request,
            idempotencyKey,
            cancellationToken);

    public Task EndRightsGrantAsync(
        Guid grantId,
        EndRightsGrantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/rights-grants/{grantId}/end",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<ContractOptionResponse>> ListContractOptionsAsync(
        Guid? contractId = null,
        string? status = null,
        bool exercisableOnly = false,
        bool pastDeadlineOnly = false,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (contractId is { } contract)
        {
            query.Add($"contractId={contract}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (exercisableOnly)
        {
            query.Add("exercisableOnly=true");
        }

        if (pastDeadlineOnly)
        {
            query.Add("pastDeadlineOnly=true");
        }

        string uri = $"{TenantRoot}/contract-options";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<ContractOptionResponse>(uri, cancellationToken);
    }

    public Task<RecordOptionResponse> RecordContractOptionAsync(
        Guid contractId,
        RecordOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordOptionRequest, RecordOptionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/options",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ResolveOptionResponse> ResolveContractOptionAsync(
        Guid optionId,
        ResolveOptionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ResolveOptionRequest, ResolveOptionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contract-options/{optionId}/resolve",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<ObligationResponse>> ListObligationsAsync(
        Guid? contractId = null,
        string? status = null,
        bool outstandingOnly = false,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [];

        if (contractId is { } contract)
        {
            query.Add($"contractId={contract}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (outstandingOnly)
        {
            query.Add("outstandingOnly=true");
        }

        if (overdueOnly)
        {
            query.Add("overdueOnly=true");
        }

        string uri = $"{TenantRoot}/obligations";

        if (query.Count > 0)
        {
            uri += "?" + string.Join("&", query);
        }

        return GetListAsync<ObligationResponse>(uri, cancellationToken);
    }

    public Task<RecordObligationResponse> RecordObligationAsync(
        Guid contractId,
        RecordObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordObligationRequest, RecordObligationResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/obligations",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ResolveObligationResponse> ResolveObligationAsync(
        Guid obligationId,
        ResolveObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ResolveObligationRequest, ResolveObligationResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/obligations/{obligationId}/resolve",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordNoticeRequirementResponse> RecordNoticeRequirementAsync(
        Guid contractId,
        RecordNoticeRequirementRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordNoticeRequirementRequest, RecordNoticeRequirementResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/notice-requirements",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordNoticeResponse> RecordNoticeAsync(
        Guid contractId,
        RecordNoticeRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordNoticeRequest, RecordNoticeResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/notices",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<CreateContractTaskResponse> CreateContractTaskAsync(
        Guid contractId,
        CreateContractTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateContractTaskRequest, CreateContractTaskResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/tasks",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<LegalDeadlineResponse>> ListLegalDeadlinesAsync(
        int? withinDays = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/legal/deadlines";

        if (withinDays is { } days)
        {
            uri += $"?withinDays={days.ToString(CultureInfo.InvariantCulture)}";
        }

        return GetListAsync<LegalDeadlineResponse>(uri, cancellationToken);
    }

    public Task<ContractCommandCenterResponse> GetLegalCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<ContractCommandCenterResponse>(
            $"{TenantRoot}/legal/command-center", cancellationToken);

    public Task<IReadOnlyList<ContractTermDefinitionResponse>> ListContractTermsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<ContractTermDefinitionResponse>(
            "/api/v1/contract-terms/catalog", cancellationToken);

    public Task<SyncChangesResponse> ReadSyncChangesAsync(
        long cursor,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        string uri = $"{TenantRoot}/sync/changes?cursor={cursor.ToString(CultureInfo.InvariantCulture)}";

        if (take is { } size)
        {
            uri += $"&take={size.ToString(CultureInfo.InvariantCulture)}";
        }

        return GetAsync<SyncChangesResponse>(uri, cancellationToken);
    }

    // ---- Finance, commissions, receivables, payments and ledger (M9) ----

    public Task<IReadOnlyList<MonetaryObligationResponse>> ListMonetaryObligationsAsync(
        Guid? contractId = null,
        bool unbilledOnly = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("contractId", contractId);
        query.Add("unbilledOnly", unbilledOnly);
        query.Add("limit", limit);

        return GetListAsync<MonetaryObligationResponse>(
            query.Apply($"{TenantRoot}/monetary-obligations"), cancellationToken);
    }

    public Task<MonetaryObligationResponse> GetMonetaryObligationAsync(
        Guid obligationId,
        CancellationToken cancellationToken = default) =>
        GetAsync<MonetaryObligationResponse>(
            $"{TenantRoot}/monetary-obligations/{obligationId}", cancellationToken);

    public Task<RecordMonetaryObligationResponse> RecordMonetaryObligationAsync(
        Guid contractId,
        RecordMonetaryObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordMonetaryObligationRequest, RecordMonetaryObligationResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/monetary-obligations",
            request,
            idempotencyKey,
            cancellationToken);

    public Task QuantifyObligationAsync(
        Guid obligationId,
        QuantifyObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/monetary-obligations/{obligationId}/quantify",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ReleaseObligationAsync(
        Guid obligationId,
        ReleaseObligationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/monetary-obligations/{obligationId}/release",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<ReceivableResponse>> ListReceivablesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? payerPartyId = null,
        Guid? clientPersonId = null,
        string? beneficiary = null,
        bool overdueOnly = false,
        bool unreconciledOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("contractId", contractId);
        query.Add("payerPartyId", payerPartyId);
        query.Add("clientPersonId", clientPersonId);
        query.Add("beneficiary", beneficiary);
        query.Add("overdueOnly", overdueOnly);
        query.Add("unreconciledOnly", unreconciledOnly);
        query.Add("dueAfter", dueAfter);
        query.Add("dueBefore", dueBefore);
        query.Add("currency", currency);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<ReceivableResponse>(
            query.Apply($"{TenantRoot}/receivables"), cancellationToken);
    }

    public Task<ReceivableResponse> GetReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ReceivableResponse>($"{TenantRoot}/receivables/{receivableId}", cancellationToken);

    public Task<RaiseReceivableResponse> RaiseReceivableAsync(
        Guid obligationId,
        RaiseReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RaiseReceivableRequest, RaiseReceivableResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/monetary-obligations/{obligationId}/receivables",
            request,
            idempotencyKey,
            cancellationToken);

    public Task WriteOffReceivableAsync(
        Guid receivableId,
        WriteOffReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/receivables/{receivableId}/write-off",
            request,
            idempotencyKey,
            cancellationToken);

    public Task CancelReceivableAsync(
        Guid receivableId,
        CancelReceivableRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/receivables/{receivableId}/cancel",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordAdjustmentResponse> RecordAdjustmentAsync(
        Guid receivableId,
        RecordAdjustmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordAdjustmentRequest, RecordAdjustmentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/receivables/{receivableId}/adjustments",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ReceivableReconciliationResponse> ReconcileReceivableAsync(
        Guid receivableId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ReceivableReconciliationResponse>(
            $"{TenantRoot}/receivables/{receivableId}/reconciliation", cancellationToken);

    public Task<IReadOnlyList<InvoiceResponse>> ListInvoicesAsync(
        string? status = null,
        Guid? contractId = null,
        Guid? debtorPartyId = null,
        bool overdueOnly = false,
        DateOnly? dueAfter = null,
        DateOnly? dueBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("contractId", contractId);
        query.Add("debtorPartyId", debtorPartyId);
        query.Add("overdueOnly", overdueOnly);
        query.Add("dueAfter", dueAfter);
        query.Add("dueBefore", dueBefore);
        query.Add("currency", currency);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<InvoiceResponse>(query.Apply($"{TenantRoot}/invoices"), cancellationToken);
    }

    public Task<InvoiceResponse> GetInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        GetAsync<InvoiceResponse>($"{TenantRoot}/invoices/{invoiceId}", cancellationToken);

    public Task<RecordInvoiceResponse> RecordInvoiceAsync(
        Guid contractId,
        RecordInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordInvoiceRequest, RecordInvoiceResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/contracts/{contractId}/invoices",
            request,
            idempotencyKey,
            cancellationToken);

    public Task IssueInvoiceAsync(
        Guid invoiceId,
        IssueInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/invoices/{invoiceId}/issue",
            request,
            idempotencyKey,
            cancellationToken);

    public Task VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/invoices/{invoiceId}/void",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(
        string? direction = null,
        string? status = null,
        Guid? payerPartyId = null,
        bool unappliedOnly = false,
        DateOnly? recordedAfter = null,
        DateOnly? recordedBefore = null,
        string? currency = null,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("direction", direction);
        query.Add("status", status);
        query.Add("payerPartyId", payerPartyId);
        query.Add("unappliedOnly", unappliedOnly);
        query.Add("recordedAfter", recordedAfter);
        query.Add("recordedBefore", recordedBefore);
        query.Add("currency", currency);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<PaymentResponse>(query.Apply($"{TenantRoot}/payments"), cancellationToken);
    }

    public Task<PaymentResponse> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PaymentResponse>($"{TenantRoot}/payments/{paymentId}", cancellationToken);

    public Task<RecordPaymentResponse> RecordPaymentAsync(
        RecordPaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecordPaymentRequest, RecordPaymentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/payments",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<RecordPaymentResponse> AllocatePaymentAsync(
        Guid paymentId,
        AllocatePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<AllocatePaymentRequest, RecordPaymentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/payments/{paymentId}/allocations",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ReverseAllocationAsync(
        Guid paymentId,
        ReverseAllocationRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/payments/{paymentId}/allocations/reverse",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ReversePaymentResponse> ReversePaymentAsync(
        Guid paymentId,
        ReversePaymentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ReversePaymentRequest, ReversePaymentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/payments/{paymentId}/reverse",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<CommissionRuleResponse>> ListCommissionRulesAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("clientPersonId", clientPersonId);
        query.Add("contractId", contractId);

        return GetListAsync<CommissionRuleResponse>(
            query.Apply($"{TenantRoot}/commission-rules"), cancellationToken);
    }

    public Task<CreateCommissionRuleResponse> CreateCommissionRuleAsync(
        CreateCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateCommissionRuleRequest, CreateCommissionRuleResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/commission-rules",
            request,
            idempotencyKey,
            cancellationToken);

    public Task EndCommissionRuleAsync(
        Guid ruleId,
        EndCommissionRuleRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/commission-rules/{ruleId}/end",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<CommissionEntitlementResponse>> ListCommissionsAsync(
        Guid? clientPersonId = null,
        Guid? contractId = null,
        Guid? representationId = null,
        string? status = null,
        bool outstandingOnly = false,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("clientPersonId", clientPersonId);
        query.Add("contractId", contractId);
        query.Add("representationId", representationId);
        query.Add("status", status);
        query.Add("outstandingOnly", outstandingOnly);
        query.Add("currency", currency);
        query.Add("limit", limit);

        return GetListAsync<CommissionEntitlementResponse>(
            query.Apply($"{TenantRoot}/commissions"), cancellationToken);
    }

    public Task<CommissionEntitlementResponse> GetCommissionAsync(
        Guid commissionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<CommissionEntitlementResponse>(
            $"{TenantRoot}/commissions/{commissionId}", cancellationToken);

    public Task<CalculateCommissionResponse> CalculateCommissionAsync(
        Guid obligationId,
        CalculateCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<CalculateCommissionRequest, CalculateCommissionResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/monetary-obligations/{obligationId}/commission",
            request,
            idempotencyKey,
            cancellationToken);

    public Task AdjustCommissionAsync(
        Guid commissionId,
        AdjustCommissionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/commissions/{commissionId}/adjustments",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<AccountResponse>> ListLedgerAccountsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<AccountResponse>($"{TenantRoot}/ledger/accounts", cancellationToken);

    public Task<IReadOnlyList<AccountBalanceResponse>> GetLedgerBalancesAsync(
        string? currency = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("currency", currency);

        return GetListAsync<AccountBalanceResponse>(
            query.Apply($"{TenantRoot}/ledger/balances"), cancellationToken);
    }

    public Task<IReadOnlyList<JournalEntryResponse>> ListJournalEntriesAsync(
        string? status = null,
        string? source = null,
        Guid? accountId = null,
        DateOnly? postedAfter = null,
        DateOnly? postedBefore = null,
        string? currency = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("status", status);
        query.Add("source", source);
        query.Add("accountId", accountId);
        query.Add("postedAfter", postedAfter);
        query.Add("postedBefore", postedBefore);
        query.Add("currency", currency);
        query.Add("limit", limit);

        return GetListAsync<JournalEntryResponse>(
            query.Apply($"{TenantRoot}/ledger/entries"), cancellationToken);
    }

    public Task<JournalEntryResponse> GetJournalEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        GetAsync<JournalEntryResponse>($"{TenantRoot}/ledger/entries/{entryId}", cancellationToken);

    public Task<PostJournalEntryResponse> PostJournalEntryAsync(
        PostJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<PostJournalEntryRequest, PostJournalEntryResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/ledger/entries",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<ReverseJournalEntryResponse> ReverseJournalEntryAsync(
        Guid entryId,
        ReverseJournalEntryRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ReverseJournalEntryRequest, ReverseJournalEntryResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/ledger/entries/{entryId}/reverse",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<FinanceHistoryEntryResponse>> GetFinanceHistoryAsync(
        Guid? contractId = null,
        Guid? receivableId = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("contractId", contractId);
        query.Add("receivableId", receivableId);

        return GetListAsync<FinanceHistoryEntryResponse>(
            query.Apply($"{TenantRoot}/finance/history"), cancellationToken);
    }

    public Task<FinanceCommandCenterResponse> GetFinanceCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<FinanceCommandCenterResponse>(
            $"{TenantRoot}/finance/command-center", cancellationToken);

    // ---- Documents and communications (M10) ----

    public Task<IReadOnlyList<DocumentSummaryResponse>> ListDocumentsAsync(
        string? kind = null,
        string? status = null,
        string? sensitivity = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool hasContent = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("kind", kind);
        query.Add("status", status);
        query.Add("sensitivity", sensitivity);
        query.Add("linkedTarget", linkedTarget);
        query.Add("linkedTargetId", linkedTargetId);
        query.Add("hasContent", hasContent);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<DocumentSummaryResponse>(
            query.Apply($"{TenantRoot}/documents"), cancellationToken);
    }

    public Task<DocumentDetailResponse> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default) =>
        GetAsync<DocumentDetailResponse>($"{TenantRoot}/documents/{documentId}", cancellationToken);

    public async Task<RecordDocumentResponse> RecordDocumentAsync(
        RecordDocumentRequest request,
        Stream content,
        string fileName,
        string? mediaType = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        using MultipartFormDataContent form = new();

        form.Add(new StringContent(request.Title), "title");
        form.Add(new StringContent(request.Kind), "kind");
        form.Add(new StringContent(request.Sensitivity), "sensitivity");

        AddOptional(form, "reference", request.Reference);
        AddOptional(form, "description", request.Description);
        AddOptional(form, "notes", request.Notes);

        // One JSON field rather than indexed keys, so uploading and filing are a
        // single act. A separate link call would leave the document unfiled every
        // time the second request failed.
        if (request.Links is { Count: > 0 } links)
        {
            form.Add(
                new StringContent(JsonSerializer.Serialize(links, Json)),
                "links");
        }

        // Streamed rather than buffered. A two-hundred-megabyte deck read into a
        // byte array would be in memory twice before anything hashed it.
        StreamContent file = new(content);

        file.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType);

        form.Add(file, "file", fileName);

        return await SendMultipartAsync<RecordDocumentResponse>(
                $"{TenantRoot}/documents", form, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RecordDocumentResponse> AddDocumentVersionAsync(
        Guid documentId,
        Stream content,
        string fileName,
        int expectedVersion,
        string? mediaType = null,
        string? notes = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using MultipartFormDataContent form = new();

        form.Add(
            new StringContent(expectedVersion.ToString(CultureInfo.InvariantCulture)),
            "expectedVersion");

        AddOptional(form, "notes", notes);

        StreamContent file = new(content);

        file.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType);

        form.Add(file, "file", fileName);

        return await SendMultipartAsync<RecordDocumentResponse>(
                $"{TenantRoot}/documents/{documentId}/versions", form, idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DocumentContent> DownloadDocumentVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _http
            .GetAsync(
                $"{TenantRoot}/document-versions/{versionId}/content",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"{versionId}";

        return new DocumentContent(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            fileName,
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            response.Content.Headers.ContentLength,
            null);
    }

    public Task UpdateDocumentAsync(
        Guid documentId,
        UpdateDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/documents/{documentId}/update",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<LinkDocumentResponse> LinkDocumentAsync(
        Guid documentId,
        LinkDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<LinkDocumentRequest, LinkDocumentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/documents/{documentId}/links",
            request,
            idempotencyKey,
            cancellationToken);

    public async Task UnlinkDocumentAsync(
        Guid documentId,
        Guid linkId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http
            .DeleteAsync($"{TenantRoot}/documents/{documentId}/links/{linkId}", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public Task ArchiveDocumentAsync(
        Guid documentId,
        ArchiveDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/documents/{documentId}/archive",
            request,
            idempotencyKey,
            cancellationToken);

    public Task RestoreDocumentAsync(
        Guid documentId,
        RestoreDocumentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/documents/{documentId}/restore",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<CommunicationProviderResponse>> ListCommunicationProvidersAsync(
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("redirectUri", redirectUri);

        return GetListAsync<CommunicationProviderResponse>(
            query.Apply($"{TenantRoot}/communication-providers"), cancellationToken);
    }

    public Task<IReadOnlyList<CommunicationAccountResponse>> ListCommunicationAccountsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<CommunicationAccountResponse>(
            $"{TenantRoot}/communication-accounts", cancellationToken);

    public Task<ConnectMailboxResponse> ConnectMailboxAsync(
        ConnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConnectMailboxRequest, ConnectMailboxResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/communication-accounts",
            request,
            idempotencyKey,
            cancellationToken);

    public Task DisconnectMailboxAsync(
        Guid accountId,
        DisconnectMailboxRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/communication-accounts/{accountId}/disconnect",
            request,
            idempotencyKey,
            cancellationToken);

    public Task ChangeMailboxVisibilityAsync(
        Guid accountId,
        ChangeMailboxVisibilityRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/communication-accounts/{accountId}/visibility",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<MessageSummaryResponse>> ListMessagesAsync(
        Guid? accountId = null,
        string? direction = null,
        string? linkedTarget = null,
        Guid? linkedTargetId = null,
        bool unlinkedOnly = false,
        bool hasAttachments = false,
        string? search = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("accountId", accountId);
        query.Add("direction", direction);
        query.Add("linkedTarget", linkedTarget);
        query.Add("linkedTargetId", linkedTargetId);
        query.Add("unlinkedOnly", unlinkedOnly);
        query.Add("hasAttachments", hasAttachments);
        query.Add("search", search);
        query.Add("limit", limit);

        return GetListAsync<MessageSummaryResponse>(
            query.Apply($"{TenantRoot}/messages"), cancellationToken);
    }

    public Task<MessageDetailResponse> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        GetAsync<MessageDetailResponse>($"{TenantRoot}/messages/{messageId}", cancellationToken);

    public Task<LinkMessageResponse> LinkMessageAsync(
        Guid messageId,
        LinkMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<LinkMessageRequest, LinkMessageResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/messages/{messageId}/links",
            request,
            idempotencyKey,
            cancellationToken);

    public async Task UnlinkMessageAsync(
        Guid messageId,
        Guid linkId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http
            .DeleteAsync($"{TenantRoot}/messages/{messageId}/links/{linkId}", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public Task ResolveParticipantAsync(
        Guid messageId,
        Guid participantId,
        ResolveParticipantRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/messages/{messageId}/participants/{participantId}",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<ParticipantSuggestionResponse>> SuggestParticipantsAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("address", address);

        return GetListAsync<ParticipantSuggestionResponse>(
            query.Apply($"{TenantRoot}/participant-suggestions"), cancellationToken);
    }

    public Task<IngestAttachmentResponse> IngestAttachmentAsync(
        Guid attachmentId,
        IngestAttachmentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<IngestAttachmentRequest, IngestAttachmentResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/message-attachments/{attachmentId}/ingest",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<OutboundDispatchResponse>> ListOutboundMessagesAsync(
        string? state = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("state", state);
        query.Add("limit", limit);

        return GetListAsync<OutboundDispatchResponse>(
            query.Apply($"{TenantRoot}/outbound-messages"), cancellationToken);
    }

    public Task<OutboundDispatchResponse> GetOutboundMessageAsync(
        Guid dispatchId,
        CancellationToken cancellationToken = default) =>
        GetAsync<OutboundDispatchResponse>(
            $"{TenantRoot}/outbound-messages/{dispatchId}", cancellationToken);

    public Task<ComposeMessageResponse> ComposeMessageAsync(
        ComposeMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ComposeMessageRequest, ComposeMessageResponse>(
            HttpMethod.Post,
            $"{TenantRoot}/outbound-messages",
            request,
            idempotencyKey,
            cancellationToken);

    public Task QueueMessageAsync(
        Guid dispatchId,
        QueueMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/outbound-messages/{dispatchId}/queue",
            request,
            idempotencyKey,
            cancellationToken);

    public Task CancelMessageAsync(
        Guid dispatchId,
        CancelMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{TenantRoot}/outbound-messages/{dispatchId}/cancel",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<CommunicationEventResponse>> GetCommunicationHistoryAsync(
        Guid? accountId = null,
        Guid? dispatchId = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("accountId", accountId);
        query.Add("dispatchId", dispatchId);

        return GetListAsync<CommunicationEventResponse>(
            query.Apply($"{TenantRoot}/communications/history"), cancellationToken);
    }

    public Task<CommunicationCommandCenterResponse> GetCommunicationCommandCenterAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<CommunicationCommandCenterResponse>(
            $"{TenantRoot}/communications/command-center", cancellationToken);

    /// <summary>Adds a form field only when there is one to add.</summary>
    private static void AddOptional(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            form.Add(new StringContent(value), name);
        }
    }

    private async Task<TResponse> SendMultipartAsync<TResponse>(
        string uri,
        MultipartFormDataContent form,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, uri) { Content = form };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(ClientHeaders.IdempotencyKey, idempotencyKey);
        }

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------- plumbing

    private void ApplyIdentityHeaders()
    {
        _http.DefaultRequestHeaders.Remove(ClientHeaders.Platform);
        _http.DefaultRequestHeaders.Remove(ClientHeaders.Channel);
        _http.DefaultRequestHeaders.Remove(ClientHeaders.ClientVersion);
        _http.DefaultRequestHeaders.Remove(ClientHeaders.ApiContractVersion);
        _http.DefaultRequestHeaders.Remove(ClientHeaders.BuildId);

        _http.DefaultRequestHeaders.Add(ClientHeaders.Platform, _session.Platform);
        _http.DefaultRequestHeaders.Add(ClientHeaders.Channel, _session.Channel);
        _http.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, _session.ClientVersion);
        _http.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            ApiContract.Current.ToString(CultureInfo.InvariantCulture));
        _http.DefaultRequestHeaders.Add(ClientHeaders.BuildId, BuildInfo.BuildId);

        if (!string.IsNullOrWhiteSpace(_session.Subject))
        {
            _http.DefaultRequestHeaders.Remove(AgencyOsSession.SubjectHeader);
            _http.DefaultRequestHeaders.Add(AgencyOsSession.SubjectHeader, _session.Subject);
        }
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, CancellationToken cancellationToken)
    {
        T[] items = await GetAsync<T[]>(uri, cancellationToken).ConfigureAwait(false);
        return items;
    }

    private Task<TResponse> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest body,
        CancellationToken cancellationToken) =>
        SendAsync<TRequest, TResponse>(HttpMethod.Post, uri, body, idempotencyKey: null, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string uri,
        TRequest body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        // Per request, never a default header: a key that leaked onto the next
        // command would make the server replay the wrong answer.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(ClientHeaders.IdempotencyKey, idempotencyKey);
        }

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendNoContentAsync<TRequest>(
        HttpMethod method,
        string uri,
        TRequest body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(ClientHeaders.IdempotencyKey, idempotencyKey);
        }

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        T? value = await response.Content
            .ReadFromJsonAsync<T>(Json, cancellationToken)
            .ConfigureAwait(false);

        return value ?? throw new AgencyOsApiException(
            response.StatusCode,
            "The server returned an empty response where content was expected.");
    }

    /// <summary>
    /// Turns a failed response into an exception carrying the server's explanation.
    /// </summary>
    /// <remarks>
    /// The server answers refusals with problem details. Surfacing its wording
    /// rather than inventing one keeps the reason accurate - a refusal may be a
    /// permission, a revoked build or a domain rule, and only the server knows
    /// which.
    /// </remarks>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        string? code = null;
        int? expected = null;
        int? actual = null;
        string title = response.ReasonPhrase ?? response.StatusCode.ToString();

        try
        {
            using JsonDocument problem = await JsonDocument
                .ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (problem.RootElement.TryGetProperty("title", out JsonElement titleElement))
            {
                title = titleElement.GetString() ?? title;
            }

            if (problem.RootElement.TryGetProperty("detail", out JsonElement detailElement))
            {
                detail = detailElement.GetString();
            }

            if (problem.RootElement.TryGetProperty("code", out JsonElement codeElement))
            {
                code = codeElement.GetString();
            }

            if (problem.RootElement.TryGetProperty("expectedVersion", out JsonElement expectedElement)
                && expectedElement.TryGetInt32(out int expectedValue))
            {
                expected = expectedValue;
            }

            if (problem.RootElement.TryGetProperty("actualVersion", out JsonElement actualElement)
                && actualElement.TryGetInt32(out int actualValue))
            {
                actual = actualValue;
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body is still an error; the status carries the meaning.
        }

        throw new AgencyOsApiException(response.StatusCode, title, detail, code, expected, actual);
    }

    /// <summary>Shape returned by endpoints that create a record and answer with its identifier.</summary>
    private sealed record CreatedIdResponse(Guid Id);

    /// <summary>
    /// Accumulates optional query-string parameters.
    /// </summary>
    /// <remarks>
    /// The finance lists take up to twelve optional predicates each. Building those
    /// with a local list and a dozen if-statements per method, as the earlier
    /// milestones did with three or four, would be several hundred lines in which a
    /// forgotten <c>Add</c> silently drops a filter and returns more rows than the
    /// caller asked for. Absent values are skipped, and every value is written with
    /// the invariant culture so a comma decimal separator or a local date format
    /// cannot reach the wire.
    /// </remarks>
    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        public void Add(string name, Guid? value)
        {
            if (value is { } id)
            {
                _parts.Add($"{name}={id.ToString("D", CultureInfo.InvariantCulture)}");
            }
        }

        public void Add(string name, int? value)
        {
            if (value is { } number)
            {
                _parts.Add($"{name}={number.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        public void Add(string name, DateOnly? value)
        {
            if (value is { } date)
            {
                _parts.Add($"{name}={date.ToString("O", CultureInfo.InvariantCulture)}");
            }
        }

        /// <summary>Adds a flag only when it is set, so the default stays off the wire.</summary>
        public void Add(string name, bool value)
        {
            if (value)
            {
                _parts.Add($"{name}=true");
            }
        }

        public string Apply(string uri) =>
            _parts.Count == 0 ? uri : uri + "?" + string.Join("&", _parts);
    }
}
