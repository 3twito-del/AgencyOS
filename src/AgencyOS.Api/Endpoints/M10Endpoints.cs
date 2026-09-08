using System.Diagnostics;
using System.Net.Mime;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Application.Documents;
using AgencyOS.Contracts.Documents;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;
using Microsoft.Net.Http.Headers;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M10 document and communication surface.
/// </summary>
/// <remarks>
/// <para>
/// Two families that share a link vocabulary and share almost nothing else.
/// Documents are bytes the agency holds; communications are messages that passed
/// between real mailboxes. Both are guarded on the record itself rather than on
/// what the record happens to be attached to (ADR-0025, ADR-0026).
/// </para>
/// <para>
/// <strong>No route returns a storage path, an OAuth token or a provider secret.</strong>
/// Bytes leave only through the authorized download route, and there is no public
/// URL anywhere in the milestone.
/// </para>
/// </remarks>
internal static class M10Endpoints
{
    public static void MapDocumentsAndCommunications(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapDocuments(tenant);
        MapMailboxes(tenant);
        MapMessages(tenant);
        MapOutbound(tenant);
    }

    // -------------------------------------------------------------- documents

    private static void MapDocuments(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/documents", async (
                Guid organizationId,
                DocumentQueryService queries,
                string? kind,
                string? status,
                string? sensitivity,
                string? source,
                string? linkedTarget,
                Guid? linkedTargetId,
                bool? hasContent,
                DateOnly? createdAfter,
                DateOnly? createdBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                DocumentFilter filter = new(
                    EndpointParsing.ParseNullableEnum<DocumentKind>(kind, nameof(kind)),
                    EndpointParsing.ParseNullableEnum<DocumentStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<DocumentSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    EndpointParsing.ParseNullableEnum<DocumentVersionSource>(
                        source, nameof(source)),
                    EndpointParsing.ParseNullableEnum<DocumentLinkTarget>(
                        linkedTarget, nameof(linkedTarget)),
                    linkedTargetId,
                    hasContent ?? false,
                    createdAfter,
                    createdBefore,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<DocumentSummaryModel> documents = await queries
                    .ListAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(documents.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsRead))
            .WithName("ListDocuments");

        tenant.MapGet("/documents/{documentId:guid}", async (
                Guid organizationId,
                Guid documentId,
                DocumentQueryService queries,
                CancellationToken cancellationToken) =>
            {
                DocumentDetailModel? document = await queries
                    .GetAsync(
                        new OrganizationId(organizationId),
                        new DocumentId(documentId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return document is null ? Results.NotFound() : Results.Ok(Map(document));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsRead))
            .WithName("GetDocument");

        /// <remarks>
        /// Multipart, so the bytes stream. A base64 body would put the whole file in
        /// memory twice before anything could hash it (ADR-0024).
        /// </remarks>
        tenant.MapPost("/documents", async (
                Guid organizationId,
                HttpRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.document.ingest");

                (RecordDocumentRequest metadata, IFormFile file) = await ReadUploadAsync(
                    request, cancellationToken).ConfigureAwait(false);

                await using Stream content = file.OpenReadStream();

                RecordDocumentResult result = await handler.HandleAsync(
                        new RecordDocumentCommand(
                            new OrganizationId(organizationId),
                            metadata.Title,
                            EndpointParsing.ParseEnum<DocumentKind>(
                                metadata.Kind, nameof(metadata.Kind)),
                            EndpointParsing.ParseEnum<DocumentSensitivity>(
                                metadata.Sensitivity, nameof(metadata.Sensitivity)),
                            content,
                            file.FileName,
                            file.ContentType,
                            DocumentVersionSource.Upload,
                            null,
                            metadata.Reference,
                            metadata.Description,
                            metadata.Notes,
                            ParseLinks(metadata.Links)),
                        cancellationToken)
                    .ConfigureAwait(false);

                RecordUploadTelemetry(result, metadata.Sensitivity);

                AgencyOsTelemetry.DocumentsRecorded.Add(
                    1, new KeyValuePair<string, object?>("kind", metadata.Kind));

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/documents/{result.DocumentId.Value}",
                    Map(result));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .DisableAntiforgery()
            .WithName("RecordDocument");

        tenant.MapPost("/documents/{documentId:guid}/versions", async (
                Guid organizationId,
                Guid documentId,
                HttpRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.document.version");

                (AddDocumentVersionRequest metadata, IFormFile file) =
                    await ReadVersionUploadAsync(request, cancellationToken).ConfigureAwait(false);

                await using Stream content = file.OpenReadStream();

                RecordDocumentResult result = await handler.HandleAsync(
                        new AddDocumentVersionCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            content,
                            file.FileName,
                            metadata.ExpectedVersion,
                            file.ContentType,
                            DocumentVersionSource.Upload,
                            null,
                            metadata.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                RecordUploadTelemetry(result, null);

                AgencyOsTelemetry.DocumentVersionsAdded.Add(1);

                return Results.Ok(Map(result));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .DisableAntiforgery()
            .WithName("AddDocumentVersion");

        /// <remarks>
        /// The only route to stored bytes. There is no public URL, no signed link
        /// and no path in any response: every byte leaves through here, having been
        /// authorized against the document's own classification (ADR-0025).
        /// </remarks>
        tenant.MapGet("/document-versions/{versionId:guid}/content", async (
                Guid organizationId,
                Guid versionId,
                DocumentQueryService queries,
                HttpResponse response,
                CancellationToken cancellationToken) =>
            {
                DocumentDownload? download = await queries
                    .OpenAsync(
                        new OrganizationId(organizationId),
                        new DocumentVersionId(versionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (download is null)
                {
                    return Results.NotFound();
                }

                AgencyOsTelemetry.DocumentDownloads.Add(1);

                // Never sniffed. A file that claims to be an image and holds HTML
                // would otherwise be rendered as HTML on the application's own
                // origin (ADR-0025).
                response.Headers["X-Content-Type-Options"] = "nosniff";

                // A locked-down policy on the response itself, so even an inline
                // preview cannot execute or fetch anything.
                response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; sandbox; base-uri 'none'; form-action 'none'";

                // Encoded by the framework, so a filename carrying quotes or
                // newlines cannot inject a header. The name was already reduced to
                // safe characters on the way in.
                ContentDisposition disposition = new(
                    download.Inline
                        ? DispositionTypeNames.Inline
                        : DispositionTypeNames.Attachment)
                {
                    FileName = download.FileName,
                };

                response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();

                return Results.Stream(
                    download.Content,
                    download.MediaType,
                    enableRangeProcessing: true);
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsRead))
            .WithName("DownloadDocumentVersion");

        tenant.MapPost("/documents/{documentId:guid}/update", async (
                Guid organizationId,
                Guid documentId,
                UpdateDocumentRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateDocumentCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            request.Title,
                            EndpointParsing.ParseEnum<DocumentKind>(
                                request.Kind, nameof(request.Kind)),
                            EndpointParsing.ParseEnum<DocumentSensitivity>(
                                request.Sensitivity, nameof(request.Sensitivity)),
                            request.ExpectedVersion,
                            request.Reference,
                            request.Description),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .WithName("UpdateDocument");

        tenant.MapPost("/documents/{documentId:guid}/links", async (
                Guid organizationId,
                Guid documentId,
                LinkDocumentRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid linkId = await handler.HandleAsync(
                        new LinkDocumentCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            EndpointParsing.ParseEnum<DocumentLinkTarget>(
                                request.Target, nameof(request.Target)),
                            request.TargetId,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.DocumentLinks.Add(
                    1, new KeyValuePair<string, object?>("operation", "link"));

                return Results.Ok(new LinkDocumentResponse(linkId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsLink))
            .WithName("LinkDocument");

        tenant.MapDelete("/documents/{documentId:guid}/links/{linkId:guid}", async (
                Guid organizationId,
                Guid documentId,
                Guid linkId,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new UnlinkDocumentCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            linkId),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.DocumentLinks.Add(
                    1, new KeyValuePair<string, object?>("operation", "unlink"));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsLink))
            .WithName("UnlinkDocument");

        /// <remarks>
        /// Archiving hides a document. It destroys nothing: the versions stay, the
        /// bytes stay, and nothing in this path reaches the content store
        /// (ADR-0024).
        /// </remarks>
        tenant.MapPost("/documents/{documentId:guid}/archive", async (
                Guid organizationId,
                Guid documentId,
                ArchiveDocumentRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ArchiveDocumentCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .WithName("ArchiveDocument");

        tenant.MapPost("/documents/{documentId:guid}/restore", async (
                Guid organizationId,
                Guid documentId,
                RestoreDocumentRequest request,
                DocumentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new RestoreDocumentCommand(
                            new OrganizationId(organizationId),
                            new DocumentId(documentId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .WithName("RestoreDocument");
    }

    // -------------------------------------------------------------- mailboxes

    private static void MapMailboxes(RouteGroupBuilder tenant)
    {
        // Which providers this server can actually connect to, and where a person
        // authorizes them. Published because the alternative is shipping the Entra
        // application registration in the Windows build, and because a connect
        // dialog with no link is a dialog nobody can complete (ADR-0027).
        tenant.MapGet("/communication-providers", (
                string redirectUri,
                ICommunicationProviderRegistry registry) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

                List<CommunicationProviderResponse> providers = [];

                foreach (CommunicationProviderKind kind in Enum.GetValues<CommunicationProviderKind>())
                {
                    if (!registry.Supports(kind))
                    {
                        continue;
                    }

                    // The state is opaque to AgencyOS and is echoed back by the
                    // provider. It is generated per request rather than stored,
                    // because M10 completes the flow from a code the operator
                    // pastes rather than from a redirect this server receives.
                    ProviderAuthorization? authorization = registry
                        .Resolve(kind)
                        .DescribeAuthorization(redirectUri, Guid.NewGuid().ToString("N"));

                    providers.Add(new CommunicationProviderResponse(
                        kind.ToString(),
                        kind switch
                        {
                            CommunicationProviderKind.MicrosoftGraph => "Microsoft Outlook",
                            _ => kind.ToString(),
                        },
                        authorization is not null,
                        authorization?.Scopes,
                        authorization?.AuthorizationUrl));
                }

                return Results.Ok(providers.ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsAccountManage))
            .WithName("ListCommunicationProviders");

        tenant.MapGet("/communication-accounts", async (
                Guid organizationId,
                CommunicationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CommunicationAccountModel> accounts = await queries
                    .ListAccountsAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(accounts.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("ListCommunicationAccounts");

        /// <remarks>
        /// The authorization code is exchanged server-side and the tokens it
        /// produces never leave the server. The mailbox is owned by whoever
        /// completed the flow, taken from the authenticated caller and never from
        /// the request (ADR-0026, ADR-0027).
        /// </remarks>
        tenant.MapPost("/communication-accounts", async (
                Guid organizationId,
                ConnectMailboxRequest request,
                CommunicationAccountHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                CommunicationProviderKind provider =
                    EndpointParsing.ParseEnum<CommunicationProviderKind>(
                        request.Provider, nameof(request.Provider));

                CommunicationAccountId id = await handler.HandleAsync(
                        new ConnectMailboxCommand(
                            new OrganizationId(organizationId),
                            provider,
                            request.AuthorizationCode,
                            request.RedirectUri,
                            EndpointParsing.ParseEnum<MailboxVisibility>(
                                request.Visibility, nameof(request.Visibility))),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.MailboxConnections.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", provider.ToString()),
                    new KeyValuePair<string, object?>("operation", "connect"));

                return Results.Ok(new ConnectMailboxResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsAccountManage))
            .WithName("ConnectMailbox");

        tenant.MapPost("/communication-accounts/{accountId:guid}/disconnect", async (
                Guid organizationId,
                Guid accountId,
                DisconnectMailboxRequest request,
                CommunicationAccountHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new DisconnectMailboxCommand(
                            new OrganizationId(organizationId),
                            new CommunicationAccountId(accountId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.MailboxConnections.Add(
                    1, new KeyValuePair<string, object?>("operation", "disconnect"));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsAccountManage))
            .WithName("DisconnectMailbox");

        tenant.MapPost("/communication-accounts/{accountId:guid}/visibility", async (
                Guid organizationId,
                Guid accountId,
                ChangeMailboxVisibilityRequest request,
                CommunicationAccountHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeMailboxVisibilityCommand(
                            new OrganizationId(organizationId),
                            new CommunicationAccountId(accountId),
                            EndpointParsing.ParseEnum<MailboxVisibility>(
                                request.Visibility, nameof(request.Visibility)),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsAccountManage))
            .WithName("ChangeMailboxVisibility");
    }

    // --------------------------------------------------------------- messages

    private static void MapMessages(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/messages", async (
                Guid organizationId,
                CommunicationQueryService queries,
                Guid? accountId,
                string? direction,
                string? linkedTarget,
                Guid? linkedTargetId,
                bool? unlinkedOnly,
                bool? hasAttachments,
                DateOnly? occurredAfter,
                DateOnly? occurredBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                CommunicationFilter filter = new(
                    accountId is { } account ? new CommunicationAccountId(account) : null,
                    EndpointParsing.ParseNullableEnum<MessageDirection>(
                        direction, nameof(direction)),
                    EndpointParsing.ParseNullableEnum<DocumentLinkTarget>(
                        linkedTarget, nameof(linkedTarget)),
                    linkedTargetId,
                    unlinkedOnly ?? false,
                    hasAttachments ?? false,
                    occurredAfter,
                    occurredBefore,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<CommunicationMessageSummaryModel> messages = await queries
                    .ListMessagesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(messages.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("ListMessages");

        tenant.MapGet("/messages/{messageId:guid}", async (
                Guid organizationId,
                Guid messageId,
                CommunicationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CommunicationMessageDetailModel? message = await queries
                    .GetMessageAsync(
                        new OrganizationId(organizationId),
                        new CommunicationMessageId(messageId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return message is null ? Results.NotFound() : Results.Ok(Map(message));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("GetMessage");

        tenant.MapPost("/messages/{messageId:guid}/links", async (
                Guid organizationId,
                Guid messageId,
                LinkMessageRequest request,
                CommunicationMessageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid linkId = await handler.HandleAsync(
                        new LinkMessageCommand(
                            new OrganizationId(organizationId),
                            new CommunicationMessageId(messageId),
                            EndpointParsing.ParseEnum<DocumentLinkTarget>(
                                request.Target, nameof(request.Target)),
                            request.TargetId,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.MessageLinks.Add(
                    1, new KeyValuePair<string, object?>("operation", "link"));

                return Results.Ok(new LinkMessageResponse(linkId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("LinkMessage");

        tenant.MapDelete("/messages/{messageId:guid}/links/{linkId:guid}", async (
                Guid organizationId,
                Guid messageId,
                Guid linkId,
                CommunicationMessageHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new UnlinkMessageCommand(
                            new OrganizationId(organizationId),
                            new CommunicationMessageId(messageId),
                            linkId),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.MessageLinks.Add(
                    1, new KeyValuePair<string, object?>("operation", "unlink"));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("UnlinkMessage");

        /// <remarks>
        /// An explicit act. AgencyOS suggests candidates from an exact address
        /// match and never picks one when two match, because a wrong identification
        /// quietly attributes somebody's correspondence to the wrong person
        /// (ADR-0026).
        /// </remarks>
        tenant.MapPost("/messages/{messageId:guid}/participants/{participantId:guid}", async (
                Guid organizationId,
                Guid messageId,
                Guid participantId,
                ResolveParticipantRequest request,
                CommunicationMessageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ResolveParticipantCommand(
                            new OrganizationId(organizationId),
                            new CommunicationMessageId(messageId),
                            participantId,
                            request.PersonId,
                            request.CompanyId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("ResolveMessageParticipant");

        tenant.MapGet("/participant-suggestions", async (
                Guid organizationId,
                string address,
                CommunicationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<ParticipantSuggestionModel> suggestions = await queries
                    .SuggestParticipantsAsync(
                        new OrganizationId(organizationId), address, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(suggestions.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("SuggestParticipants");

        /// <remarks>
        /// A deliberate act, never automatic. Downloading every attachment on sight
        /// would move gigabytes of unrequested video through the server and would
        /// give the agency durable copies of files nobody asked it to keep
        /// (ADR-0024).
        /// </remarks>
        tenant.MapPost("/message-attachments/{attachmentId:guid}/ingest", async (
                Guid organizationId,
                Guid attachmentId,
                IngestAttachmentRequest request,
                CommunicationMessageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.communication.attachment");

                RecordDocumentResult result = await handler.HandleAsync(
                        new IngestAttachmentCommand(
                            new OrganizationId(organizationId),
                            new CommunicationAttachmentId(attachmentId),
                            EndpointParsing.ParseEnum<DocumentKind>(
                                request.Kind, nameof(request.Kind)),
                            EndpointParsing.ParseEnum<DocumentSensitivity>(
                                request.Sensitivity, nameof(request.Sensitivity)),
                            request.Title,
                            ParseLinks(request.Links)),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.AttachmentsIngested.Add(1);
                RecordUploadTelemetry(result, request.Sensitivity);

                return Results.Ok(new IngestAttachmentResponse(
                    result.DocumentId.Value,
                    result.VersionId.Value,
                    result.ContentHash,
                    result.ByteLength));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DocumentsWrite))
            .WithName("IngestMessageAttachment");
    }

    // --------------------------------------------------------------- outbound

    private static void MapOutbound(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/outbound-messages", async (
                Guid organizationId,
                CommunicationQueryService queries,
                string? state,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<OutboundDispatchModel> dispatches = await queries
                    .ListDispatchesAsync(
                        new OrganizationId(organizationId),
                        EndpointParsing.ParseNullableEnum<OutboundDispatchState>(
                            state, nameof(state)),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(dispatches.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("ListOutboundMessages");

        tenant.MapGet("/outbound-messages/{dispatchId:guid}", async (
                Guid organizationId,
                Guid dispatchId,
                CommunicationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                OutboundDispatchModel? dispatch = await queries
                    .GetDispatchAsync(
                        new OrganizationId(organizationId),
                        new OutboundDispatchId(dispatchId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return dispatch is null ? Results.NotFound() : Results.Ok(Map(dispatch));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("GetOutboundMessage");

        /// <remarks>
        /// Records the intent. Nothing has left, and the message can still be
        /// cancelled: queuing it is the last point at which that is true
        /// (ADR-0028).
        /// </remarks>
        tenant.MapPost("/outbound-messages", async (
                Guid organizationId,
                ComposeMessageRequest request,
                OutboundDispatchHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OutboundDispatchId id = await handler.HandleAsync(
                        new ComposeMessageCommand(
                            new OrganizationId(organizationId),
                            new CommunicationAccountId(request.AccountId),
                            request.Subject,
                            request.BodyText,
                            [
                                .. request.Recipients.Select(x => new OutboundRecipientInput(
                                    EndpointParsing.ParseEnum<ParticipantRole>(
                                        x.Role, nameof(x.Role)),
                                    x.Address,
                                    x.DisplayName)),
                            ],
                            [
                                .. (request.AttachmentVersionIds ?? [])
                                    .Select(x => new DocumentVersionId(x)),
                            ],
                            request.InReplyToMessageId is { } reply
                                ? new CommunicationMessageId(reply)
                                : null),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.OutboundComposed.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "recipients", request.Recipients.Count),
                    new KeyValuePair<string, object?>(
                        "attachments", request.AttachmentVersionIds?.Count ?? 0));

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/outbound-messages/{id.Value}",
                    new ComposeMessageResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsSend))
            .WithName("ComposeMessage");

        tenant.MapPost("/outbound-messages/{dispatchId:guid}/queue", async (
                Guid organizationId,
                Guid dispatchId,
                QueueMessageRequest request,
                OutboundDispatchHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new QueueMessageCommand(
                            new OrganizationId(organizationId),
                            new OutboundDispatchId(dispatchId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsSend))
            .WithName("QueueOutboundMessage");

        tenant.MapPost("/outbound-messages/{dispatchId:guid}/cancel", async (
                Guid organizationId,
                Guid dispatchId,
                CancelMessageRequest request,
                OutboundDispatchHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new CancelMessageCommand(
                            new OrganizationId(organizationId),
                            new OutboundDispatchId(dispatchId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsSend))
            .WithName("CancelOutboundMessage");

        tenant.MapGet("/communications/history", async (
                Guid organizationId,
                CommunicationQueryService queries,
                Guid? accountId,
                Guid? dispatchId,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CommunicationEventModel> history = await queries
                    .GetHistoryAsync(
                        new OrganizationId(organizationId),
                        accountId is { } account ? new CommunicationAccountId(account) : null,
                        dispatchId is { } dispatch ? new OutboundDispatchId(dispatch) : null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("GetCommunicationHistory");

        tenant.MapGet("/communications/command-center", async (
                Guid organizationId,
                CommunicationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CommunicationCommandCenterModel centre = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(centre));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CommunicationsRead))
            .WithName("GetCommunicationCommandCenter");
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Reads a multipart upload: one metadata part and one file.</summary>
    /// <remarks>
    /// Refuses anything else. A request with three files or none is a client bug,
    /// and guessing which one was meant would store the wrong bytes.
    /// </remarks>
    private static async Task<(RecordDocumentRequest Metadata, IFormFile File)> ReadUploadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);

        IFormFile file = form.Files.Count == 1
            ? form.Files[0]
            : throw new Domain.Common.DomainException(
                "An upload carries exactly one file.");

        // Links travel as one JSON field rather than as indexed form keys. Filing a
        // contract against its deal in the same act as uploading it is the ordinary
        // case, and making it a second request would leave an unfiled document
        // behind whenever the second request failed.
        IReadOnlyList<DocumentLinkRequest>? links = null;

        if (Optional(form, "links") is { Length: > 0 } encoded)
        {
            links = System.Text.Json.JsonSerializer.Deserialize<DocumentLinkRequest[]>(
                        encoded,
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web))
                    ?? throw new Domain.Common.DomainException(
                        "'links' is not a list of link requests.");
        }

        RecordDocumentRequest metadata = new(
            Required(form, "title"),
            Required(form, "kind"),
            Required(form, "sensitivity"),
            Optional(form, "reference"),
            Optional(form, "description"),
            Optional(form, "notes"),
            links);

        return (metadata, file);
    }

    private static async Task<(AddDocumentVersionRequest Metadata, IFormFile File)>
        ReadVersionUploadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);

        IFormFile file = form.Files.Count == 1
            ? form.Files[0]
            : throw new Domain.Common.DomainException(
                "An upload carries exactly one file.");

        if (!int.TryParse(
                form["expectedVersion"], System.Globalization.CultureInfo.InvariantCulture,
                out int expectedVersion))
        {
            throw new Domain.Common.DomainException(
                "expectedVersion is required, so a concurrent change is refused rather "
                    + "than silently overwritten.");
        }

        return (new AddDocumentVersionRequest(expectedVersion, Optional(form, "notes")), file);
    }

    private static string Required(IFormCollection form, string field) =>
        form.TryGetValue(field, out Microsoft.Extensions.Primitives.StringValues value)
            && !string.IsNullOrWhiteSpace(value)
                ? value.ToString()
                : throw new Domain.Common.DomainException($"'{field}' is required.");

    private static string? Optional(IFormCollection form, string field) =>
        form.TryGetValue(field, out Microsoft.Extensions.Primitives.StringValues value)
            && !string.IsNullOrWhiteSpace(value)
                ? value.ToString()
                : null;

    private static IReadOnlyList<DocumentLinkInput>? ParseLinks(
        IReadOnlyList<DocumentLinkRequest>? links) =>
        links is null
            ? null
            : [
                .. links.Select(x => new DocumentLinkInput(
                    EndpointParsing.ParseEnum<DocumentLinkTarget>(x.Target, nameof(x.Target)),
                    x.TargetId,
                    x.Note)),
            ];

    /// <summary>Counts bytes and deduplication, never a digest or a filename.</summary>
    private static void RecordUploadTelemetry(RecordDocumentResult result, string? sensitivity)
    {
        AgencyOsTelemetry.BlobBytesStored.Add(result.ByteLength);

        if (result.Deduplicated)
        {
            AgencyOsTelemetry.BlobDeduplications.Add(1);
        }

        _ = sensitivity;
    }

    // --------------------------------------------------------------- mapping

    internal static DocumentSummaryResponse Map(DocumentSummaryModel document) =>
        new(
            document.Id.Value,
            document.Title,
            document.Kind.ToString(),
            document.Status.ToString(),
            document.Sensitivity.ToString(),
            document.Reference,
            document.CurrentVersion is { } current ? Map(current) : null,
            document.VersionCount,
            document.LinkCount,
            document.HoldsContent,
            document.CreatedAt,
            document.UpdatedAt,
            document.CreatedByDisplayName,
            document.Version);

    private static DocumentVersionResponse Map(DocumentVersionSummaryModel version) =>
        new(
            version.Id.Value,
            version.Sequence,
            version.DisplayFileName,
            version.MediaType,
            version.ByteLength,
            version.ContentHash,
            version.Source.ToString(),
            version.SourceExternalReference,
            version.RecordedAt,
            version.CreatedByDisplayName,
            version.Notes,
            version.ExtractionState.ToString(),
            version.ExtractionDetail,
            version.ScanState.ToString());

    private static DocumentDetailResponse Map(DocumentDetailModel document) =>
        new(
            Map(document.Document),
            document.Description,
            document.ArchiveReason,
            [.. document.Versions.Select(Map)],
            [
                .. document.Links.Select(x => new DocumentLinkResponse(
                    x.Id,
                    x.Target.ToString(),
                    x.TargetId,
                    x.TargetLabel,
                    x.Note,
                    x.LinkedAt,
                    x.LinkedByDisplayName)),
            ],
            [
                .. document.History.Select(x => new DocumentEventResponse(
                    x.OccurredAt, x.Kind.ToString(), x.Summary, x.Detail, x.ActorDisplayName)),
            ],
            document.ExtractedText);

    private static RecordDocumentResponse Map(RecordDocumentResult result) =>
        new(
            result.DocumentId.Value,
            result.VersionId.Value,
            result.ContentHash,
            result.ByteLength,
            result.Deduplicated);

    internal static CommunicationAccountResponse Map(CommunicationAccountModel account) =>
        new(
            account.Id.Value,
            account.Provider.ToString(),
            account.MailboxAddress,
            account.DisplayName,
            account.OwnerUserId,
            account.OwnerDisplayName,
            account.State.ToString(),
            account.Visibility.ToString(),
            account.GrantedScopes,
            account.LastSyncedAt,
            account.LastSyncError,
            account.HasStoredCredential,
            account.CredentialExpiresAt,
            account.MessageCount,
            account.CreatedAt,
            account.Version);

    internal static MessageSummaryResponse Map(CommunicationMessageSummaryModel message) =>
        new(
            message.Id.Value,
            message.AccountId.Value,
            message.MailboxAddress,
            message.Direction.ToString(),
            message.Subject,
            message.FromAddress,
            message.FromDisplayName,
            message.ToAddresses,
            message.OccurredAt,
            message.SynchronizedAt,
            message.HasAttachments,
            message.AttachmentCount,
            message.LinkCount,
            message.IsDeletedAtProvider,
            message.Folder);

    private static MessageDetailResponse Map(CommunicationMessageDetailModel message) =>
        new(
            Map(message.Message),
            message.BodyText,
            message.SanitizedHtml,
            message.InternetMessageId,
            message.ExternalMessageId,
            [.. message.Participants.Select(Map)],
            [
                .. message.Attachments.Select(x => new MessageAttachmentResponse(
                    x.Id.Value,
                    x.FileName,
                    x.MediaType,
                    x.ByteLength,
                    x.IsInline,
                    x.HoldsContent,
                    x.DocumentVersionId?.Value,
                    x.DocumentId?.Value,
                    x.IngestedAt)),
            ],
            [
                .. message.Links.Select(x => new MessageLinkResponse(
                    x.Id,
                    x.Target.ToString(),
                    x.TargetId,
                    x.TargetLabel,
                    x.Note,
                    x.LinkedAt,
                    x.LinkedByDisplayName)),
            ],
            [.. message.Thread.Select(Map)]);

    private static ParticipantResponse Map(CommunicationParticipantModel participant) =>
        new(
            participant.Id,
            participant.Role.ToString(),
            participant.Address,
            participant.DisplayName,
            participant.PersonId,
            participant.PersonDisplayName,
            participant.CompanyId,
            participant.CompanyDisplayName,
            participant.ResolvedByDisplayName);

    internal static OutboundDispatchResponse Map(OutboundDispatchModel dispatch) =>
        new(
            dispatch.Id.Value,
            dispatch.AccountId.Value,
            dispatch.MailboxAddress,
            dispatch.State.ToString(),
            dispatch.Subject,
            [.. dispatch.Recipients.Select(Map)],
            [
                .. dispatch.Attachments.Select(x => new OutboundAttachmentResponse(
                    x.DocumentId.Value,
                    x.DocumentVersionId.Value,
                    x.FileName,
                    x.MediaType,
                    x.ByteLength)),
            ],
            dispatch.AttemptCount,
            dispatch.LastError,
            dispatch.LastVerdict?.ToString(),
            dispatch.LastReconciledAt,
            dispatch.HasProviderDraft,
            dispatch.HasProviderEvidence,
            dispatch.SentMessageId?.Value,
            dispatch.SentAt,
            dispatch.CreatedAt,
            dispatch.UpdatedAt,
            dispatch.CreatedByDisplayName,
            dispatch.NeedsAttention,
            dispatch.Version);

    private static CommunicationEventResponse Map(CommunicationEventModel entry) =>
        new(entry.OccurredAt, entry.Kind.ToString(), entry.Summary, entry.Detail,
            entry.ActorDisplayName);

    private static ParticipantSuggestionResponse Map(ParticipantSuggestionModel suggestion) =>
        new(
            suggestion.PersonId,
            suggestion.CompanyId,
            suggestion.DisplayName,
            suggestion.MatchedAddress,
            suggestion.IsUnambiguous);

    private static CommunicationCommandCenterResponse Map(CommunicationCommandCenterModel centre) =>
        new(
            [.. centre.UnknownOutcomes.Select(Map)],
            [.. centre.FailedSends.Select(Map)],
            [.. centre.AccountsNeedingAttention.Select(Map)],
            centre.UnknownOutcomeCount,
            centre.FailedSendCount,
            centre.DisconnectedAccountCount);
}
