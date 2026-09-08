using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Intelligence;

// -------------------------------------------------------------------- commands

/// <summary>One subject an intelligence object is about.</summary>
public sealed record SubjectInput(
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    string? Note = null);

/// <summary>
/// Records a piece of evidence.
/// </summary>
/// <remarks>
/// Exactly one of the five shapes. The identity fields are mutually exclusive and
/// the handler refuses anything else, because a source that is both a document and
/// a URL is a record nobody can interpret later (ADR-0030).
/// </remarks>
public sealed record RecordSourceCommand(
    OrganizationId OrganizationId,
    IntelligenceSourceKind Kind,
    string Title,
    IntelligenceSensitivity Sensitivity,
    DocumentVersionId? DocumentVersionId = null,
    CommunicationMessageId? MessageId = null,
    string? Url = null,
    string? Publisher = null,
    string? Author = null,
    string? ExternalReference = null,
    DateTimeOffset? PublishedAt = null,
    DateTimeOffset? ObservedAt = null,
    string? Notes = null);

public sealed record AssessSourceReliabilityCommand(
    OrganizationId OrganizationId,
    IntelligenceSourceId SourceId,
    SourceReliability Reliability,
    int ExpectedVersion,
    string? Rationale = null);

public sealed record UpdateSourceCommand(
    OrganizationId OrganizationId,
    IntelligenceSourceId SourceId,
    string Title,
    IntelligenceSensitivity Sensitivity,
    int ExpectedVersion,
    string? Notes = null);

/// <summary>One source cited when a signal is first recorded.</summary>
public sealed record SignalEvidenceInput(
    IntelligenceSourceId SourceId,
    SignalEvidenceRole Role = SignalEvidenceRole.Primary,
    string? Excerpt = null,
    string? Locator = null);

/// <summary>
/// Records an atomic intelligence claim.
/// </summary>
/// <remarks>
/// At least one source is required at creation. A signal is never allowed to exist
/// without provenance, so the check happens before the row is written rather than
/// as a later cleanup (ADR-0030).
/// </remarks>
public sealed record RecordSignalCommand(
    OrganizationId OrganizationId,
    string Title,
    string Claim,
    SignalKind Kind,
    IntelligenceSensitivity Sensitivity,
    IReadOnlyList<SignalEvidenceInput> Evidence,
    IReadOnlyList<SubjectInput>? Subjects = null,
    DateTimeOffset? OccurredAt = null,
    DateTimeOffset? ObservedAt = null,
    SignalConfidence Confidence = SignalConfidence.Unstated,
    string? Notes = null);

public sealed record UpdateSignalCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    string Title,
    string Claim,
    SignalKind Kind,
    IntelligenceSensitivity Sensitivity,
    SignalConfidence Confidence,
    int ExpectedVersion,
    string? Notes = null);

public sealed record ChangeSignalVerificationCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    SignalVerification Verification,
    int ExpectedVersion,
    string? Note = null);

public sealed record LinkSignalEvidenceCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    IntelligenceSourceId SourceId,
    SignalEvidenceRole Role,
    int ExpectedVersion,
    string? Excerpt = null,
    string? Locator = null);

public sealed record UnlinkSignalEvidenceCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    Guid EvidenceId,
    int ExpectedVersion);

public sealed record AddSignalSubjectCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);

public sealed record RemoveSignalSubjectCommand(
    OrganizationId OrganizationId,
    SignalId SignalId,
    Guid SubjectRowId,
    int ExpectedVersion);

// -------------------------------------------------------------------- handlers

/// <summary>
/// Records evidence.
/// </summary>
/// <remarks>
/// The bottom of the epistemic chain, and the only handler that touches M10. It
/// references canonical document versions and messages and copies nothing from
/// them: there is no second document store here, and a source pointing at a
/// document does not grant any access to it (ADR-0025, ADR-0030).
/// </remarks>
public sealed class IntelligenceSourceHandler
{
    private readonly IIntelligenceSourceRepository _sources;
    private readonly IDocumentRepository _documents;
    private readonly ICommunicationMessageRepository _messages;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public IntelligenceSourceHandler(
        IIntelligenceSourceRepository sources,
        IDocumentRepository documents,
        ICommunicationMessageRepository messages,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _sources = sources;
        _documents = documents;
        _messages = messages;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<IntelligenceSourceId> HandleAsync(
        RecordSourceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        IntelligenceSource source = command.Kind switch
        {
            IntelligenceSourceKind.DocumentVersion => await FromDocumentAsync(
                command, actor, now, cancellationToken).ConfigureAwait(false),

            IntelligenceSourceKind.Message => await FromMessageAsync(
                command, actor, now, cancellationToken).ConfigureAwait(false),

            IntelligenceSourceKind.ExternalUrl => IntelligenceSource.FromExternalUrl(
                command.OrganizationId,
                command.Url ?? throw new DomainException("A URL source needs a URL."),
                command.Title,
                actor,
                now,
                command.Sensitivity,
                command.Publisher,
                command.Author,
                command.PublishedAt,
                command.ObservedAt,
                command.ExternalReference,
                command.Notes),

            IntelligenceSourceKind.ManualObservation => IntelligenceSource.FromObservation(
                command.OrganizationId,
                command.Title,
                actor,
                now,
                command.Sensitivity,
                command.ObservedAt,
                command.Notes),

            IntelligenceSourceKind.Other => IntelligenceSource.FromOther(
                command.OrganizationId,
                command.Title,
                actor,
                now,
                command.Sensitivity,
                command.ObservedAt,
                command.PublishedAt,
                command.ExternalReference,
                command.Notes),

            _ => throw new DomainException($"'{command.Kind}' is not a kind of source."),
        };

        _sources.Add(source);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Source,
            source.Id.Value,
            IntelligenceEventKind.SourceRecorded,
            $"Source recorded: {source.Title}",
            actor,
            now,
            source.Kind.ToString()));

        _audit.Record(
            AuditAction.IntelligenceSourceRecorded,
            entityType: nameof(IntelligenceSource),
            entityId: source.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,

            // The kind and the classification, never the title or the notes. An
            // audit delta is exported and read by people who hold no intelligence
            // grant (ADR-0030).
            semanticDelta: new
            {
                Kind = source.Kind.ToString(),
                Sensitivity = source.Sensitivity.ToString(),
                source.IsHeldByAgencyOS,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return source.Id;
    }

    public async Task HandleAsync(
        AssessSourceReliabilityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntelligenceSource source = await RequireAsync(
            command.OrganizationId, command.SourceId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, source.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        source.AssessReliability(
            command.Reliability, actor, now, command.ExpectedVersion, command.Rationale);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Source,
            source.Id.Value,
            IntelligenceEventKind.SourceReliabilityAssessed,
            $"Reliability assessed as {command.Reliability}.",
            actor,
            now,
            command.Rationale));

        _audit.Record(
            AuditAction.IntelligenceSourceAssessed,
            entityType: nameof(IntelligenceSource),
            entityId: source.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Reliability = command.Reliability.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        UpdateSourceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IntelligenceSource source = await RequireAsync(
            command.OrganizationId, command.SourceId, cancellationToken).ConfigureAwait(false);

        // Both the classification it has and the one it is moving to, so a writer
        // cannot lift something out of their own reach or push it into a
        // classification they could not read.
        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, source.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        source.Update(command.Title, command.Sensitivity, command.ExpectedVersion, command.Notes);

        _audit.Record(
            AuditAction.IntelligenceSourceUpdated,
            entityType: nameof(IntelligenceSource),
            entityId: source.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Sensitivity = command.Sensitivity.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Proves the document version exists here before a source points at it.
    /// </summary>
    /// <remarks>
    /// Existence only. Pointing intelligence at a document does not require the
    /// right to read the document, and does not grant it either — the excerpt is
    /// where that question is decided, and it is re-checked there on every read
    /// (ADR-0030).
    /// </remarks>
    private async Task<IntelligenceSource> FromDocumentAsync(
        RecordSourceCommand command,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DocumentVersionId versionId = command.DocumentVersionId
            ?? throw new DomainException("A document source needs a document version.");

        DocumentVersion? version = await _documents
            .FindVersionAsync(command.OrganizationId, versionId, cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            throw new EntityNotFoundException(
                nameof(DocumentVersion), versionId.ToString());
        }

        return IntelligenceSource.FromDocumentVersion(
            command.OrganizationId,
            versionId,
            command.Title,
            actor,
            now,
            command.Sensitivity,
            command.ObservedAt,

            // M10 already knows when it was handed the bytes. Copying that rather
            // than asking the caller keeps one answer to the question.
            command.PublishedAt ?? version.RecordedAt,
            command.Notes);
    }

    private async Task<IntelligenceSource> FromMessageAsync(
        RecordSourceCommand command,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CommunicationMessageId messageId = command.MessageId
            ?? throw new DomainException("A message source needs a message.");

        CommunicationMessage? message = await _messages
            .FindAsync(command.OrganizationId, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            throw new EntityNotFoundException(
                nameof(CommunicationMessage), messageId.ToString());
        }

        return IntelligenceSource.FromMessage(
            command.OrganizationId,
            messageId,
            command.Title,
            actor,
            now,
            command.Sensitivity,
            command.ObservedAt ?? message.SynchronizedAt,

            // The message's own canonical time, rather than a second copy that
            // could disagree with M10 (ADR-0026).
            command.PublishedAt ?? message.SentAt ?? message.ReceivedAt,
            command.Notes);
    }

    private async Task<IntelligenceSource> RequireAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        CancellationToken cancellationToken) =>
        await _sources.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(IntelligenceSource), id.ToString());
}
