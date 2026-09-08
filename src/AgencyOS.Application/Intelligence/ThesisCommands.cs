using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Intelligence;

// -------------------------------------------------------------------- commands

public sealed record CreateThesisCommand(
    OrganizationId OrganizationId,
    string Title,
    string Proposition,
    IntelligenceSensitivity Sensitivity,
    Guid? OwnerUserId = null,
    ThesisConfidence Confidence = ThesisConfidence.Unstated,
    string? Rationale = null,
    IReadOnlyList<SubjectInput>? Subjects = null);

public sealed record ActivateThesisCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    int ExpectedVersion);

/// <summary>
/// Records a changed position.
/// </summary>
/// <remarks>
/// Appends a revision rather than editing the current text. The system has to be
/// able to answer "what did we believe six months ago", and an edit would quietly
/// rewrite the agency's own memory (ADR-0030).
/// </remarks>
public sealed record ReviseThesisCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    string Proposition,
    ThesisConfidence Confidence,
    int ExpectedVersion,
    string? Rationale = null,
    string? ChangeNote = null);

public sealed record CloseThesisCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    string Reason,
    int ExpectedVersion,
    ThesisId? SupersededBy = null);

public sealed record LinkThesisEvidenceCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    SignalId SignalId,
    ThesisEvidenceStance Stance,
    int ExpectedVersion,
    string? Note = null);

public sealed record UnlinkThesisEvidenceCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    Guid EvidenceId,
    int ExpectedVersion);

public sealed record AddThesisSubjectCommand(
    OrganizationId OrganizationId,
    ThesisId ThesisId,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);

// --------------------------------------------------------------------- handler

/// <summary>
/// Creates and revises analyst interpretations.
/// </summary>
/// <remarks>
/// Nothing here calculates whether a thesis is true. Evidence is attached with a
/// stance and organized for a person to weigh; the system counts nothing and
/// concludes nothing (ADR-0030).
/// </remarks>
public sealed class ThesisHandler
{
    private readonly IThesisRepository _theses;
    private readonly ISignalRepository _signals;
    private readonly IIntelligenceSubjectValidator _subjects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ThesisHandler(
        IThesisRepository theses,
        ISignalRepository signals,
        IIntelligenceSubjectValidator subjects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _theses = theses;
        _signals = signals;
        _subjects = subjects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ThesisId> HandleAsync(
        CreateThesisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            await RequireSubjectAsync(
                command.OrganizationId, subject, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        Thesis thesis = Thesis.Create(
            command.OrganizationId,
            command.Title,
            command.Proposition,
            command.Sensitivity,

            // The owner defaults to whoever wrote it. An interpretation belongs to
            // somebody, and an ownerless thesis is one nobody will revisit.
            command.OwnerUserId is { } owner ? new UserId(owner) : actor,
            actor,
            now,
            command.Confidence,
            command.Rationale);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            thesis.AddSubject(subject.Kind, subject.SubjectId, actor, now, subject.Note);
        }

        _theses.Add(thesis);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Thesis,
            thesis.Id.Value,
            IntelligenceEventKind.ThesisCreated,
            $"Thesis created: {thesis.Title}",
            actor,
            now));

        _audit.Record(
            AuditAction.ThesisCreated,
            entityType: nameof(Thesis),
            entityId: thesis.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,

            // Never the proposition. What the agency privately believes does not
            // belong in a log read by people holding no intelligence grant.
            semanticDelta: new
            {
                Sensitivity = thesis.Sensitivity.ToString(),
                Confidence = thesis.Confidence.ToString(),
                SubjectCount = thesis.Subjects.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return thesis.Id;
    }

    public async Task HandleAsync(
        ActivateThesisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        thesis.Activate(now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Thesis,
            thesis.Id.Value,
            IntelligenceEventKind.ThesisActivated,
            "Thesis activated.",
            actor,
            now));

        _audit.Record(
            AuditAction.ThesisActivated,
            entityType: nameof(Thesis),
            entityId: thesis.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        ReviseThesisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        ThesisRevision revision = thesis.Revise(
            command.Proposition,
            command.Confidence,
            actor,
            now,
            command.ExpectedVersion,
            command.Rationale,
            command.ChangeNote);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Thesis,
            thesis.Id.Value,
            IntelligenceEventKind.ThesisRevised,
            $"Revised to revision {revision.Sequence}.",
            actor,
            now,
            command.ChangeNote));

        _audit.Record(
            AuditAction.ThesisRevised,
            entityType: nameof(Thesis),
            entityId: thesis.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new
            {
                revision.Sequence,
                Confidence = command.Confidence.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return revision.Id;
    }

    /// <summary>Retires a thesis, or supersedes it with a named successor.</summary>
    public async Task HandleAsync(
        CloseThesisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        if (command.SupersededBy is { } successor)
        {
            // The successor has to be real and in this tenant, or the trail from
            // an old belief to the current one would dead-end.
            _ = await RequireAsync(command.OrganizationId, successor, cancellationToken)
                .ConfigureAwait(false);

            thesis.Supersede(successor, command.Reason, now, command.ExpectedVersion);
        }
        else
        {
            thesis.Retire(command.Reason, now, command.ExpectedVersion);
        }

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Thesis,
            thesis.Id.Value,
            command.SupersededBy is null
                ? IntelligenceEventKind.ThesisRetired
                : IntelligenceEventKind.ThesisSuperseded,
            command.SupersededBy is null ? "Thesis retired." : "Thesis superseded.",
            actor,
            now,
            command.Reason));

        _audit.Record(
            command.SupersededBy is null
                ? AuditAction.ThesisRetired
                : AuditAction.ThesisSuperseded,
            entityType: nameof(Thesis),
            entityId: thesis.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        LinkThesisEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        if (!await _signals
            .ExistsAsync(command.OrganizationId, command.SignalId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new DomainException("That signal is not in this organization.");
        }

        DateTimeOffset now = _clock.UtcNow;

        ThesisEvidence evidence = thesis.AddEvidence(
            command.SignalId, command.Stance, actor, now, command.Note);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Thesis,
            thesis.Id.Value,
            IntelligenceEventKind.ThesisEvidenceLinked,
            $"Signal cited as {command.Stance}.",
            actor,
            now));

        _audit.Record(
            AuditAction.ThesisEvidenceLinked,
            entityType: nameof(Thesis),
            entityId: thesis.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new
            {
                SignalId = command.SignalId.ToString(),
                Stance = command.Stance.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return evidence.Id;
    }

    public async Task HandleAsync(
        UnlinkThesisEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        thesis.RemoveEvidence(command.EvidenceId);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddThesisSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Thesis thesis = await RequireAsync(
            command.OrganizationId, command.ThesisId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSubjectAsync(
            command.OrganizationId,
            new SubjectInput(command.Kind, command.SubjectId, command.Note),
            cancellationToken).ConfigureAwait(false);

        ThesisSubject subject = thesis.AddSubject(
            command.Kind, command.SubjectId, actor, _clock.UtcNow, command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return subject.Id;
    }

    private async Task RequireSubjectAsync(
        OrganizationId organizationId,
        SubjectInput subject,
        CancellationToken cancellationToken)
    {
        if (!await _subjects
            .ExistsAsync(organizationId, subject.Kind, subject.SubjectId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new DomainException(
                $"There is no {subject.Kind} with that identifier in this organization.");
        }
    }

    private async Task<Thesis> RequireAsync(
        OrganizationId organizationId,
        ThesisId id,
        CancellationToken cancellationToken) =>
        await _theses.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Thesis), id.ToString());
}
