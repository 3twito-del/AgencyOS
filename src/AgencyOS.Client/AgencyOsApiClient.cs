using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Releases;

namespace AgencyOS.Client;

/// <summary>Raised when the API refuses or fails a request.</summary>
public sealed class AgencyOsApiException : Exception
{
    public AgencyOsApiException(HttpStatusCode statusCode, string message, string? detail = null)
        : base(message)
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Problem-details explanation from the server, when it supplied one.</summary>
    public string? Detail { get; }

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

    Task<PersonDetailResponse> CreatePersonAsync(
        CreatePersonRequest request,
        CancellationToken cancellationToken = default);

    Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
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
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task ReopenTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default);
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
        CancellationToken cancellationToken = default) =>
        PostAsync<CreatePersonRequest, PersonDetailResponse>($"{TenantRoot}/people", request, cancellationToken);

    public Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdatePersonRequest, PersonDetailResponse>(
            HttpMethod.Put,
            $"{TenantRoot}/people/{personId}",
            request,
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
        CancellationToken cancellationToken = default) =>
        PostAsync<CreateCompanyRequest, CompanyDetailResponse>($"{TenantRoot}/companies", request, cancellationToken);

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
        PostNoContentAsync($"{TenantRoot}/relationships/{relationshipId}/end", cancellationToken);

    public Task<RecordInteractionResponse> RecordInteractionAsync(
        RecordInteractionRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<RecordInteractionRequest, RecordInteractionResponse>(
            $"{TenantRoot}/interactions",
            request,
            cancellationToken);

    public Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default) =>
        GetListAsync<TaskResponse>(
            $"{TenantRoot}/tasks?openOnly={(openOnly ? "true" : "false")}",
            cancellationToken);

    public async Task<Guid> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        CreatedIdResponse created = await PostAsync<CreateTaskRequest, CreatedIdResponse>(
            $"{TenantRoot}/tasks",
            request,
            cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    public Task CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        PostNoContentAsync($"{TenantRoot}/tasks/{taskId}/complete", cancellationToken);

    public Task ReopenTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        PostNoContentAsync($"{TenantRoot}/tasks/{taskId}/reopen", cancellationToken);

    public Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CommandCenterResponse>($"{TenantRoot}/command-center", cancellationToken);

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
        SendAsync<TRequest, TResponse>(HttpMethod.Post, uri, body, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string uri,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostNoContentAsync(string uri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, uri);

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
        }
        catch (JsonException)
        {
            // A non-JSON error body is still an error; the status carries the meaning.
        }

        throw new AgencyOsApiException(response.StatusCode, title, detail);
    }

    /// <summary>Shape returned by endpoints that create a record and answer with its identifier.</summary>
    private sealed record CreatedIdResponse(Guid Id);
}
