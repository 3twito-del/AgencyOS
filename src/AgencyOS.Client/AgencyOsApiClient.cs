using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Releases;
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
