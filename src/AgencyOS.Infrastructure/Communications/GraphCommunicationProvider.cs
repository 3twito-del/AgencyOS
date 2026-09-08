using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Domain.Communications;

namespace AgencyOS.Infrastructure.Communications;

/// <summary>
/// What an operator must provision before a Microsoft mailbox can be connected.
/// </summary>
/// <remarks>
/// <para>
/// AgencyOS cannot create an Entra application registration for you, and it does
/// not pretend to. The exact provisioning steps are in
/// <c>docs/adr/ADR-0027-oauth-token-protection-and-graph-provisioning.md</c>;
/// without them the adapter compiles, its contract tests pass and no mailbox
/// connects, which is the honest state (ADR-0027).
/// </para>
/// <para>
/// No secret is committed. These values are configuration, absent by default, and
/// the provider registry simply does not offer Microsoft Graph when they are not
/// set.
/// </para>
/// </remarks>
public sealed class GraphOptions
{
    /// <summary>The Entra application (client) identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>The application secret. Configuration only; never committed.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The tenant the application is registered in, or "common".</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// The delegated scopes AgencyOS asks for.
    /// </summary>
    /// <remarks>
    /// Least privilege, and short on purpose. <c>Mail.Read</c> to synchronize,
    /// <c>Mail.Send</c> to send, <c>offline_access</c> to keep the connection alive.
    /// AgencyOS asks for nothing about calendars, contacts, files or directories,
    /// because it uses none of them (ADR-0027).
    /// </remarks>
    public string Scopes { get; set; } = "offline_access Mail.Read Mail.Send User.Read";

    public string Authority { get; set; } = "https://login.microsoftonline.com";

    public string GraphEndpoint { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>Whether the adapter has enough configuration to be offered at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Microsoft Outlook mail through Microsoft Graph.
/// </summary>
/// <remarks>
/// <para>
/// Written directly against the six REST endpoints AgencyOS uses rather than on
/// top of the Microsoft Graph SDK. The SDK is very large, models the whole of
/// Microsoft 365, and would be a substantial dependency in exchange for six calls;
/// the same reasoning produced a hand-written API client in M3 (ADR-0026).
/// </para>
/// <para>
/// The protocol is the one the fake provider implements: a draft first, a send
/// second, and a search of the sent items when the send does not answer. Delta
/// synchronization uses Graph's own <c>@odata.deltaLink</c>, and a <c>410 Gone</c>
/// is reported as an expired cursor rather than as an error, because that is what
/// Graph means by it.
/// </para>
/// <para>
/// <strong>This adapter has not been exercised against a real Microsoft tenant in
/// this repository.</strong> It compiles, and its behaviour under the protocol is
/// checked against the contract the fake also satisfies. Real-provider validation
/// is a separate claim and is not made here.
/// </para>
/// </remarks>
public sealed class GraphCommunicationProvider : ICommunicationProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GraphOptions _options;

    public GraphCommunicationProvider(HttpClient http, GraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
    }

    /// <inheritdoc />
    public CommunicationProviderKind Kind => CommunicationProviderKind.MicrosoftGraph;

    /// <inheritdoc />
    public ProviderAuthorization? DescribeAuthorization(string redirectUri, string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        if (!_options.IsConfigured)
        {
            return null;
        }

        // Built here rather than in the client, because the application
        // registration is server configuration. The client secret is not in this
        // URL and never leaves the server (ADR-0027).
        string query = string.Join(
            '&',
            $"client_id={Uri.EscapeDataString(_options.ClientId!)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_mode=query",
            $"scope={Uri.EscapeDataString(_options.Scopes)}",
            $"state={Uri.EscapeDataString(state)}",
            "prompt=select_account");

        return new ProviderAuthorization(
            $"{_options.Authority}/{_options.TenantId}/oauth2/v2.0/authorize?{query}",
            _options.Scopes);
    }

    /// <inheritdoc />
    public async Task<ProviderConnection> CompleteConnectionAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        RequireConfigured();

        TokenResponse token = await ExchangeAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId!,
                ["client_secret"] = _options.ClientSecret!,
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = redirectUri,
                ["scope"] = _options.Scopes,
            },
            cancellationToken)
            .ConfigureAwait(false);

        if (token.RefreshToken is not { Length: > 0 } refreshToken)
        {
            throw new ProviderAuthorizationException(
                "The provider returned no refresh token. The offline_access scope is required.");
        }

        GraphUser user = await GetAsync<GraphUser>(token.AccessToken, "/me", cancellationToken)
            .ConfigureAwait(false);

        return new ProviderConnection(
            user.Id,
            user.Mail ?? user.UserPrincipalName,
            user.DisplayName,
            token.Scope ?? _options.Scopes,
            refreshToken,
            token.ExpiresIn is { } seconds
                ? DateTimeOffset.UtcNow.AddSeconds(seconds)
                : null);
    }

    /// <inheritdoc />
    public async Task<ProviderSyncPage> SyncAsync(
        string refreshToken,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        string accessToken = await AccessTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        // A cursor is Graph's own delta link and is used verbatim. Reconstructing
        // one from a timestamp would miss back-dated messages and re-read unchanged
        // ones (ADR-0026).
        string uri = cursor
            ?? string.Create(
                CultureInfo.InvariantCulture,
                $"{_options.GraphEndpoint}/me/mailFolders/inbox/messages/delta?$top={pageSize}");

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Gone)
        {
            // Graph's way of saying the cursor is too old to honour. Not an error:
            // the caller forgets it and resynchronizes.
            return new ProviderSyncPage([], null, false, CursorExpired: true);
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        GraphDeltaPage page = (await response.Content
            .ReadFromJsonAsync<GraphDeltaPage>(Json, cancellationToken)
            .ConfigureAwait(false))!;

        return new ProviderSyncPage(
            [.. page.Value.Select(ToProviderMessage)],
            page.NextLink ?? page.DeltaLink,
            HasMore: page.NextLink is not null);
    }

    /// <inheritdoc />
    public async Task<ProviderDraft> CreateDraftAsync(
        string refreshToken,
        OutboundDraftRequest request,
        string clientReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string accessToken = await AccessTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        // The correlation value travels as an internet message header, so a message
        // found in the sent items later is attributable to this intent with
        // certainty rather than by comparing subject lines (ADR-0028).
        object body = new
        {
            subject = request.Subject,
            body = new { contentType = "Text", content = request.BodyText },
            toRecipients = Recipients(request, ParticipantRole.To),
            ccRecipients = Recipients(request, ParticipantRole.Cc),
            bccRecipients = Recipients(request, ParticipantRole.Bcc),
            internetMessageHeaders = new[]
            {
                new { name = "x-agencyos-reference", value = clientReference },
            },
        };

        GraphMessage draft = await PostAsync<GraphMessage>(
            accessToken, "/me/messages", body, cancellationToken).ConfigureAwait(false);

        foreach (OutboundDraftAttachment attachment in request.Attachments)
        {
            await UploadAttachmentAsync(accessToken, draft.Id, attachment, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ProviderDraft(draft.Id, draft.InternetMessageId);
    }

    /// <inheritdoc />
    public async Task<ProviderSendResult> SendDraftAsync(
        string refreshToken,
        string providerDraftId,
        CancellationToken cancellationToken = default)
    {
        string accessToken = await AccessTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage request = new(
            HttpMethod.Post, $"{_options.GraphEndpoint}/me/messages/{providerDraftId}/send");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            // No answer. Graph may have accepted and sent it. Reported as unknown,
            // never as failed, because those have opposite consequences (ADR-0028).
            return new ProviderSendResult(
                ProviderSendOutcome.Unknown, Error: "No answer from the provider.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return new ProviderSendResult(
                    ProviderSendOutcome.Accepted, ProviderMessageId: providerDraftId);
            }

            // A stated refusal from the service, before it committed to anything.
            // Client errors will not change on retry; server errors and throttling
            // might.
            bool permanent = response.StatusCode
                is HttpStatusCode.BadRequest
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
                or HttpStatusCode.UnprocessableEntity;

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                throw new ProviderAuthorizationException();
            }

            return new ProviderSendResult(
                ProviderSendOutcome.Rejected,
                Error: $"The provider answered {(int)response.StatusCode}.",
                IsPermanent: permanent);
        }
    }

    /// <inheritdoc />
    public async Task<ProviderReconciliation> ReconcileAsync(
        string refreshToken,
        string clientReference,
        string? providerDraftId,
        CancellationToken cancellationToken = default)
    {
        string accessToken = await AccessTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // The sent items are searched for the correlation header the draft
            // carried. This is the evidence that makes an unknown outcome
            // resolvable at all.
            string uri = string.Create(
                CultureInfo.InvariantCulture,
                $"/me/mailFolders/sentitems/messages?$top=50&$select=id,internetMessageId,internetMessageHeaders");

            GraphMessagePage sent = await GetAsync<GraphMessagePage>(accessToken, uri, cancellationToken)
                .ConfigureAwait(false);

            GraphMessage? match = sent.Value.FirstOrDefault(
                message => message.InternetMessageHeaders?.Any(
                    header => string.Equals(
                                  header.Name, "x-agencyos-reference", StringComparison.OrdinalIgnoreCase)
                              && header.Value == clientReference)
                    == true);

            if (match is not null)
            {
                return new ProviderReconciliation(
                    DispatchReconciliationVerdict.FoundSent,
                    match.Id,
                    match.InternetMessageId,
                    "Found in the sent items.");
            }

            if (providerDraftId is null)
            {
                return new ProviderReconciliation(
                    DispatchReconciliationVerdict.Inconclusive,
                    Detail: "No draft identifier to check against.");
            }

            // Absence is only evidence when the draft is still there. Graph removes
            // a draft when it sends it, so a draft that survives means the send did
            // not happen; a draft that is gone with nothing in the sent items
            // proves nothing at all (ADR-0028).
            using HttpRequestMessage draftRequest = new(
                HttpMethod.Get, $"{_options.GraphEndpoint}/me/messages/{providerDraftId}?$select=id");

            draftRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage draftResponse = await _http
                .SendAsync(draftRequest, cancellationToken).ConfigureAwait(false);

            return draftResponse.IsSuccessStatusCode
                ? new ProviderReconciliation(
                    DispatchReconciliationVerdict.ProvenAbsent,
                    Detail: "Not in the sent items, and the draft still exists.")
                : new ProviderReconciliation(
                    DispatchReconciliationVerdict.Inconclusive,
                    Detail: "Neither a sent message nor the draft could be found.");
        }
        catch (Exception failure) when (failure is not ProviderAuthorizationException
            and not OperationCanceledException)
        {
            return new ProviderReconciliation(
                DispatchReconciliationVerdict.Inconclusive,
                Detail: $"The provider could not answer ({failure.GetType().Name}).");
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAttachmentAsync(
        string refreshToken,
        string externalMessageId,
        string externalAttachmentId,
        CancellationToken cancellationToken = default)
    {
        string accessToken = await AccessTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{_options.GraphEndpoint}/me/messages/{externalMessageId}"
                + $"/attachments/{externalAttachmentId}/$value");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        // Streamed rather than buffered: an attachment is arbitrary size and the
        // caller hashes it on the way into the blob store.
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ plumbing

    private void RequireConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new Domain.Common.DomainException(
                "Microsoft Graph is not configured on this server. An operator must register "
                    + "an application and supply its identifier and secret.");
        }
    }

    private async Task<string> AccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        RequireConfigured();

        TokenResponse token = await ExchangeAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId!,
                ["client_secret"] = _options.ClientSecret!,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = _options.Scopes,
            },
            cancellationToken)
            .ConfigureAwait(false);

        return token.AccessToken;
    }

    private async Task<TokenResponse> ExchangeAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"{_options.Authority}/{_options.TenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(form),
        };

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The body of a failed token exchange can echo the credential back.
            // Nothing from it reaches the exception, the log or the telemetry
            // (ADR-0027).
            throw new ProviderAuthorizationException(
                $"The token endpoint answered {(int)response.StatusCode}.");
        }

        return (await response.Content
            .ReadFromJsonAsync<TokenResponse>(Json, cancellationToken)
            .ConfigureAwait(false))!;
    }

    private async Task<T> GetAsync<T>(
        string accessToken,
        string uri,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            uri.StartsWith("http", StringComparison.Ordinal) ? uri : _options.GraphEndpoint + uri);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return (await response.Content
            .ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<T> PostAsync<T>(
        string accessToken,
        string uri,
        object body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, _options.GraphEndpoint + uri)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return (await response.Content
            .ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Uploads one attachment onto a draft.
    /// </summary>
    /// <remarks>
    /// Graph's simple attachment endpoint takes base64 in the body and is limited to
    /// a few megabytes; larger files need an upload session. The upload-session path
    /// is not implemented, and the limit is enforced by the caller's policy rather
    /// than discovered as a provider error halfway through a send.
    /// </remarks>
    private async Task UploadAttachmentAsync(
        string accessToken,
        string draftId,
        OutboundDraftAttachment attachment,
        CancellationToken cancellationToken)
    {
        await using Stream content = await attachment.OpenContent(cancellationToken)
            .ConfigureAwait(false);

        using MemoryStream buffer = new();

        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        object body = new
        {
            @odata_type = "#microsoft.graph.fileAttachment",
            name = attachment.FileName,
            contentType = attachment.MediaType,
            contentBytes = Convert.ToBase64String(buffer.ToArray()),
        };

        _ = await PostAsync<GraphAttachment>(
            accessToken, $"/me/messages/{draftId}/attachments", body, cancellationToken)
            .ConfigureAwait(false);
    }

    private static object[] Recipients(OutboundDraftRequest request, ParticipantRole role) =>
    [
        .. request.Recipients
            .Where(x => x.Role == role)
            .Select(x => new
            {
                emailAddress = new { address = x.Address, name = x.DisplayName },
            }),
    ];

    private static ProviderMessage ToProviderMessage(GraphMessage message) => new(
        message.Id,
        message.IsDraft == true ? MessageDirection.Outbound : MessageDirection.Inbound,
        message.ConversationId,
        message.InternetMessageId,
        message.Subject,
        message.Body?.ContentType == "text" ? message.Body.Content : null,
        message.Body?.ContentType == "html" ? message.Body.Content : null,
        message.SentDateTime,
        message.ReceivedDateTime,
        message.ParentFolderId,
        [
            .. Participants(message),
        ],
        [
            .. (message.Attachments ?? []).Select(x => new ProviderAttachment(
                x.Id, x.Name ?? "attachment", x.ContentType ?? "application/octet-stream",
                x.Size ?? 0, x.IsInline == true)),
        ],
        message.Removed is not null);

    private static IEnumerable<ProviderParticipant> Participants(GraphMessage message)
    {
        if (message.From?.EmailAddress is { Address.Length: > 0 } from)
        {
            yield return new ProviderParticipant(ParticipantRole.From, from.Address!, from.Name);
        }

        foreach (GraphRecipient recipient in message.ToRecipients ?? [])
        {
            if (recipient.EmailAddress is { Address.Length: > 0 } email)
            {
                yield return new ProviderParticipant(ParticipantRole.To, email.Address!, email.Name);
            }
        }

        foreach (GraphRecipient recipient in message.CcRecipients ?? [])
        {
            if (recipient.EmailAddress is { Address.Length: > 0 } email)
            {
                yield return new ProviderParticipant(ParticipantRole.Cc, email.Address!, email.Name);
            }
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new ProviderAuthorizationException();
        }

        // The status only. A Graph error body can quote the request, including
        // recipients and headers, and none of that belongs in an exception message
        // that will be logged.
        _ = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        throw new ProviderTransientException(
            $"The provider answered {(int)response.StatusCode}.");
    }

    // ---- wire shapes --------------------------------------------------------

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record GraphUser(string Id, string? Mail, string UserPrincipalName, string? DisplayName);

    private sealed record GraphDeltaPage(
        IReadOnlyList<GraphMessage> Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink,
        [property: JsonPropertyName("@odata.deltaLink")] string? DeltaLink);

    private sealed record GraphMessagePage(IReadOnlyList<GraphMessage> Value);

    private sealed record GraphMessage(
        string Id,
        string? ConversationId,
        string? InternetMessageId,
        string? Subject,
        GraphBody? Body,
        GraphRecipient? From,
        IReadOnlyList<GraphRecipient>? ToRecipients,
        IReadOnlyList<GraphRecipient>? CcRecipients,
        DateTimeOffset? SentDateTime,
        DateTimeOffset? ReceivedDateTime,
        string? ParentFolderId,
        bool? IsDraft,
        IReadOnlyList<GraphAttachment>? Attachments,
        IReadOnlyList<GraphHeader>? InternetMessageHeaders,
        [property: JsonPropertyName("@removed")] object? Removed);

    private sealed record GraphBody(string? ContentType, string? Content);

    private sealed record GraphRecipient(GraphEmailAddress? EmailAddress);

    private sealed record GraphEmailAddress(string? Address, string? Name);

    private sealed record GraphAttachment(
        string Id, string? Name, string? ContentType, long? Size, bool? IsInline);

    private sealed record GraphHeader(string Name, string Value);
}
