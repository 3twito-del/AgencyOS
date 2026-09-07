using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts;
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
public interface IAgencyOsApi
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
}

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
public sealed class AgencyOsApiClient : IAgencyOsApi
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
}
