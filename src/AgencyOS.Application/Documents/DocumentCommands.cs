using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

// -------------------------------------------------------------------- commands

/// <summary>Records a document, with its first version's bytes.</summary>
/// <param name="Content">The bytes. Streamed, hashed and stored before any row exists.</param>
/// <param name="Links">What the document is about, recorded with it.</param>
public sealed record RecordDocumentCommand(
    OrganizationId OrganizationId,
    string Title,
    DocumentKind Kind,
    DocumentSensitivity Sensitivity,
    Stream Content,
    string FileName,
    string? MediaType = null,
    DocumentVersionSource Source = DocumentVersionSource.Upload,
    string? SourceExternalReference = null,
    string? Reference = null,
    string? Description = null,
    string? Notes = null,
    IReadOnlyList<DocumentLinkInput>? Links = null);

/// <param name="Target">Which kind of record. Each has a real column and a real foreign key.</param>
public sealed record DocumentLinkInput(DocumentLinkTarget Target, Guid TargetId, string? Note = null);

/// <summary>Adds a version. Never replaces one.</summary>
public sealed record AddDocumentVersionCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    Stream Content,
    string FileName,
    int ExpectedVersion,
    string? MediaType = null,
    DocumentVersionSource Source = DocumentVersionSource.Upload,
    string? SourceExternalReference = null,
    string? Notes = null);

public sealed record UpdateDocumentCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    string Title,
    DocumentKind Kind,
    DocumentSensitivity Sensitivity,
    int ExpectedVersion,
    string? Reference = null,
    string? Description = null);

public sealed record LinkDocumentCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    DocumentLinkTarget Target,
    Guid TargetId,
    string? Note = null);

public sealed record UnlinkDocumentCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    Guid LinkId);

public sealed record ArchiveDocumentCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    string Reason,
    int ExpectedVersion);

public sealed record RestoreDocumentCommand(
    OrganizationId OrganizationId,
    DocumentId DocumentId,
    int ExpectedVersion);

/// <summary>What recording a document produced.</summary>
/// <param name="Deduplicated">
/// Whether the bytes were already held. A storage fact, never an authorization one.
/// </param>
public sealed record RecordDocumentResult(
    DocumentId DocumentId,
    DocumentVersionId VersionId,
    string ContentHash,
    long ByteLength,
    bool Deduplicated);

// -------------------------------------------------------------------- handlers

/// <summary>
/// Records what the agency holds.
/// </summary>
/// <remarks>
/// Every write here goes through the same two-part shape: bytes are staged into the
/// content store first and rows are written second, so a failure leaves an
/// unreferenced file rather than a document nobody can open (ADR-0024).
/// </remarks>
public sealed class DocumentHandler
{
    private readonly IDocumentRepository _documents;
    private readonly IBlobRepository _blobs;
    private readonly IDocumentEventRepository _events;
    private readonly DocumentIngestion _ingestion;
    private readonly DocumentAuthorization _authorization;
    private readonly DocumentLinkValidator _links;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public DocumentHandler(
        IDocumentRepository documents,
        IBlobRepository blobs,
        IDocumentEventRepository events,
        DocumentIngestion ingestion,
        DocumentAuthorization authorization,
        DocumentLinkValidator links,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _blobs = blobs;
        _events = events;
        _ingestion = ingestion;
        _authorization = authorization;
        _links = links;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Records a document and its first version.</summary>
    public async Task<RecordDocumentResult> HandleAsync(
        RecordDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        // Every link is checked against a real row in this tenant before anything
        // is written, so a document is never created pointing at somebody else's
        // record (ADR-0025).
        if (command.Links is { Count: > 0 } links)
        {
            await _authorization.AuthorizeLinkAsync(command.OrganizationId, cancellationToken)
                .ConfigureAwait(false);

            foreach (DocumentLinkInput link in links)
            {
                await _links
                    .RequireTargetAsync(
                        command.OrganizationId, link.Target, link.TargetId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        StagedContent staged = await _ingestion
            .StageAsync(
                command.OrganizationId,
                command.Content,
                command.FileName,
                command.MediaType,
                actor,
                UploadPolicy.MaximumByteLength,
                cancellationToken)
            .ConfigureAwait(false);

        Document document = Document.Record(
            command.OrganizationId,
            command.Title,
            command.Kind,
            command.Sensitivity,
            actor,
            _clock.UtcNow,
            command.Reference,
            command.Description);

        DocumentVersion version = document.AddVersion(
            staged.BlobObjectId,
            staged.ContentHash,
            staged.ByteLength,
            staged.FileName,
            staged.MediaType,
            command.Source,
            actor,
            _clock.UtcNow,
            command.SourceExternalReference,
            command.Notes);

        _documents.Add(document);

        foreach (DocumentLinkInput link in command.Links ?? [])
        {
            document.Link(link.Target, link.TargetId, actor, _clock.UtcNow, link.Note);
        }

        await ExtractAsync(command.OrganizationId, version, staged, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Recorded,
            $"Document recorded: {document.Title}",
            actor,
            _clock.UtcNow,
            version.Id,
            $"{staged.FileName} ({staged.ByteLength:N0} bytes)"));

        _audit.Record(
            AuditAction.DocumentRecorded,
            entityType: nameof(Document),
            entityId: document.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite,

            // Identity, shape and classification. Never the filename, never the
            // digest, never a byte of content: audit is read by people who may hold
            // no document permission at all (ADR-0025).
            semanticDelta: new
            {
                Kind = document.Kind.ToString(),
                Sensitivity = document.Sensitivity.ToString(),
                staged.ByteLength,
                staged.MediaType,
                LinkCount = command.Links?.Count ?? 0,
            });

        await _ingestion
            .FinalizeAsync(command.OrganizationId, staged.IngestionId, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordDocumentResult(
            document.Id, version.Id, staged.ContentHash, staged.ByteLength, staged.Deduplicated);
    }

    /// <summary>
    /// Adds a version to an existing document.
    /// </summary>
    /// <remarks>
    /// The workflow an external editor produces: download, edit in Word, upload the
    /// result. There is no path that overwrites the version somebody downloaded,
    /// because that is the version they may have sent to a counterparty
    /// (ADR-0024).
    /// </remarks>
    public async Task<RecordDocumentResult> HandleAsync(
        AddDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        if (document.Version != command.ExpectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Document),
                document.Id.ToString(),
                command.ExpectedVersion,
                document.Version);
        }

        StagedContent staged = await _ingestion
            .StageAsync(
                command.OrganizationId,
                command.Content,
                command.FileName,
                command.MediaType,
                actor,
                UploadPolicy.MaximumByteLength,
                cancellationToken)
            .ConfigureAwait(false);

        DocumentVersion version = document.AddVersion(
            staged.BlobObjectId,
            staged.ContentHash,
            staged.ByteLength,
            staged.FileName,
            staged.MediaType,
            command.Source,
            actor,
            _clock.UtcNow,
            command.SourceExternalReference,
            command.Notes);

        await ExtractAsync(command.OrganizationId, version, staged, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.VersionAdded,
            $"Version {version.Sequence} recorded",
            actor,
            _clock.UtcNow,
            version.Id,
            $"{staged.FileName} ({staged.ByteLength:N0} bytes)"));

        _audit.Record(
            AuditAction.DocumentVersionAdded,
            entityType: nameof(DocumentVersion),
            entityId: version.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite,
            semanticDelta: new
            {
                DocumentId = document.Id.ToString(),
                version.Sequence,
                staged.ByteLength,
                staged.MediaType,
                staged.Deduplicated,
            });

        await _ingestion
            .FinalizeAsync(command.OrganizationId, staged.IngestionId, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordDocumentResult(
            document.Id, version.Id, staged.ContentHash, staged.ByteLength, staged.Deduplicated);
    }

    public async Task HandleAsync(
        UpdateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        // Both classifications are checked: the one it has, so a caller cannot
        // reclassify a document they may not read, and the one it is moving to, so
        // they cannot file it somewhere they could not follow it.
        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        if (command.Sensitivity != document.Sensitivity)
        {
            await _authorization
                .AuthorizeClassifyAsync(
                    command.OrganizationId, command.Sensitivity, cancellationToken)
                .ConfigureAwait(false);
        }

        DocumentSensitivity before = document.Sensitivity;

        document.Update(
            command.Title,
            command.Kind,
            command.Sensitivity,
            command.ExpectedVersion,
            _clock.UtcNow,
            command.Reference,
            command.Description);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.MetadataChanged,
            before == command.Sensitivity
                ? "Document details changed"
                : $"Reclassified from {before} to {command.Sensitivity}",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.DocumentUpdated,
            entityType: nameof(Document),
            entityId: document.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite,
            semanticDelta: new
            {
                Kind = document.Kind.ToString(),
                From = before.ToString(),
                To = command.Sensitivity.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        LinkDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeLinkAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        // The caller must be able to read the document they are linking, or a link
        // becomes a way to learn that a privileged document exists.
        await _authorization
            .AuthorizeReadAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await _links
            .RequireTargetAsync(
                command.OrganizationId, command.Target, command.TargetId, cancellationToken)
            .ConfigureAwait(false);

        DocumentLink link = document.Link(
            command.Target, command.TargetId, actor, _clock.UtcNow, command.Note);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Linked,
            $"Linked to a {command.Target}",
            actor,
            _clock.UtcNow,
            detail: command.Note));

        _audit.Record(
            AuditAction.DocumentLinked,
            entityType: nameof(DocumentLink),
            entityId: link.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsLink,
            semanticDelta: new
            {
                DocumentId = document.Id.ToString(),
                Target = command.Target.ToString(),
                TargetId = command.TargetId.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return link.Id;
    }

    public async Task HandleAsync(
        UnlinkDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeLinkAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeReadAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DocumentLink link = document.Unlink(command.LinkId, _clock.UtcNow);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Unlinked,
            $"Unlinked from a {link.Target}",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.DocumentUnlinked,
            entityType: nameof(DocumentLink),
            entityId: link.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsLink,
            semanticDelta: new
            {
                DocumentId = document.Id.ToString(),
                Target = link.Target.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a document out of ordinary use.
    /// </summary>
    /// <remarks>
    /// Archiving is not deletion, and the two are never joined. The versions stay,
    /// the bytes stay, and nothing in this path reaches the blob store, because a
    /// document somebody archived is still evidence of what the agency held
    /// (ADR-0024).
    /// </remarks>
    public async Task HandleAsync(
        ArchiveDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        document.Archive(command.Reason, command.ExpectedVersion, _clock.UtcNow);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Archived,
            "Document archived",
            actor,
            _clock.UtcNow,
            detail: command.Reason));

        _audit.Record(
            AuditAction.DocumentArchived,
            entityType: nameof(Document),
            entityId: document.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite,
            semanticDelta: new { VersionCount = document.Versions.Count },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        RestoreDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Document document = await RequireDocumentAsync(
            command.OrganizationId, command.DocumentId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        document.Restore(command.ExpectedVersion, _clock.UtcNow);

        _events.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Restored,
            "Document restored",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.DocumentRestored,
            entityType: nameof(Document),
            entityId: document.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExtractAsync(
        OrganizationId organizationId,
        DocumentVersion version,
        StagedContent staged,
        CancellationToken cancellationToken)
    {
        BlobObject? blob = await _blobs
            .FindAsync(organizationId, staged.BlobObjectId, cancellationToken)
            .ConfigureAwait(false);

        if (blob is null)
        {
            return;
        }

        await _ingestion
            .ExtractTextAsync(organizationId, version, blob.StorageKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Document> RequireDocumentAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken) =>
        await _documents.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Document), id.ToString());
}
