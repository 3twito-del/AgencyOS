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

/// <summary>
/// Records and assesses intelligence claims.
/// </summary>
/// <remarks>
/// <para>
/// Two rules run through every method here. A signal always has provenance, checked
/// before the row is written rather than repaired afterwards. And nothing about a
/// signal's standing changes on its own: corroborating and disputing are commands a
/// person issues, never consequences of how much evidence accumulated
/// (ADR-0030).
/// </para>
/// <para>
/// Recording a signal changes no business state anywhere. An email saying "we love
/// the project" supports a claim that somebody wrote that; moving an opportunity
/// target to Interested is an M6 command. M10 established that rule for linked
/// correspondence and M11 keeps it (ADR-0026).
/// </para>
/// </remarks>
public sealed class SignalHandler
{
    private readonly ISignalRepository _signals;
    private readonly IIntelligenceSourceRepository _sources;
    private readonly IIntelligenceSubjectValidator _subjects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SignalHandler(
        ISignalRepository signals,
        IIntelligenceSourceRepository sources,
        IIntelligenceSubjectValidator subjects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _signals = signals;
        _sources = sources;
        _subjects = subjects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<SignalId> HandleAsync(
        RecordSignalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        // Provenance, before anything is written. A claim with no traceable source
        // is a rumour that has been promoted to a record, and the check belongs
        // here rather than in a later sweep (ADR-0030).
        if (command.Evidence is null || command.Evidence.Count == 0)
        {
            throw new DomainException(
                "A signal names at least one source. Record the evidence first, even if it "
                    + "is only an observation somebody made.");
        }

        await RequireSourcesAsync(
            command.OrganizationId, command.Evidence, cancellationToken).ConfigureAwait(false);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            await RequireSubjectAsync(
                command.OrganizationId, subject, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        Signal signal = Signal.Record(
            command.OrganizationId,
            command.Title,
            command.Claim,
            command.Kind,
            command.Sensitivity,
            actor,
            now,
            command.OccurredAt,
            command.ObservedAt,
            command.Confidence,
            command.Notes);

        foreach (SignalEvidenceInput evidence in command.Evidence)
        {
            signal.AddEvidence(
                evidence.SourceId, evidence.Role, actor, now, evidence.Excerpt, evidence.Locator);
        }

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            signal.AddSubject(subject.Kind, subject.SubjectId, actor, now, subject.Note);
        }

        _signals.Add(signal);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SignalRecorded,
            $"Signal recorded: {signal.Title}",
            actor,
            now,
            $"{signal.Evidence.Count} source(s), {signal.Subjects.Count} subject(s)"));

        _audit.Record(
            AuditAction.SignalRecorded,
            entityType: nameof(Signal),
            entityId: signal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,

            // Counts and classification, never the claim. The audit trail is read
            // by people holding no intelligence grant (ADR-0030).
            semanticDelta: new
            {
                Kind = signal.Kind.ToString(),
                Sensitivity = signal.Sensitivity.ToString(),
                EvidenceCount = signal.Evidence.Count,
                SubjectCount = signal.Subjects.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return signal.Id;
    }

    public async Task HandleAsync(
        UpdateSignalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        signal.Update(
            command.Title,
            command.Claim,
            command.Kind,
            command.Sensitivity,
            command.Confidence,
            command.ExpectedVersion,
            command.Notes);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SignalUpdated,
            "Signal corrected.",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.SignalUpdated,
            entityType: nameof(Signal),
            entityId: signal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Sensitivity = command.Sensitivity.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Corroborates, disputes or retracts a claim.
    /// </summary>
    /// <remarks>
    /// One entry point for the three, because they are the same act — a person
    /// stating where the claim now stands and why — and splitting them into three
    /// handlers would triple the places the rule "never automatic" has to hold.
    /// </remarks>
    public async Task HandleAsync(
        ChangeSignalVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        (IntelligenceEventKind eventKind, string auditAction, string summary) =
            command.Verification switch
            {
                SignalVerification.Corroborated => (
                    IntelligenceEventKind.SignalCorroborated,
                    AuditAction.SignalCorroborated,
                    "Corroborated."),

                SignalVerification.Disputed => (
                    IntelligenceEventKind.SignalDisputed,
                    AuditAction.SignalDisputed,
                    "Disputed."),

                SignalVerification.Retracted => (
                    IntelligenceEventKind.SignalRetracted,
                    AuditAction.SignalRetracted,
                    "Retracted."),

                _ => throw new DomainException(
                    "A signal is corroborated, disputed or retracted. Unverified is where it "
                        + "starts, not somewhere it is moved back to."),
            };

        switch (command.Verification)
        {
            case SignalVerification.Corroborated:
                signal.Corroborate(actor, now, command.ExpectedVersion, command.Note);
                break;

            case SignalVerification.Disputed:
                signal.Dispute(
                    actor,
                    now,
                    command.ExpectedVersion,
                    command.Note ?? throw new DomainException(
                        "Disputing a claim states the reason to doubt it."));
                break;

            default:
                signal.Retract(
                    actor,
                    now,
                    command.ExpectedVersion,
                    command.Note ?? throw new DomainException(
                        "Retracting a claim states why it was withdrawn."));
                break;
        }

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            eventKind,
            summary,
            actor,
            now,
            command.Note));

        _audit.Record(
            auditAction,
            entityType: nameof(Signal),
            entityId: signal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            reason: command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        LinkSignalEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSourcesAsync(
            command.OrganizationId,
            [new SignalEvidenceInput(command.SourceId, command.Role)],
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        SignalEvidence evidence = signal.AddEvidence(
            command.SourceId, command.Role, actor, now, command.Excerpt, command.Locator);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SignalEvidenceAdded,
            $"Evidence added as {command.Role}.",
            actor,
            now));

        _audit.Record(
            AuditAction.SignalEvidenceLinked,
            entityType: nameof(Signal),
            entityId: signal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new
            {
                SourceId = command.SourceId.ToString(),
                Role = command.Role.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return evidence.Id;
    }

    public async Task HandleAsync(
        UnlinkSignalEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        // The aggregate refuses to leave the claim with no provenance. That check
        // lives there rather than here so no future caller can route around it.
        signal.RemoveEvidence(command.EvidenceId);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SignalEvidenceRemoved,
            "Evidence removed.",
            actor,
            _clock.UtcNow));

        _audit.Record(
            AuditAction.SignalEvidenceUnlinked,
            entityType: nameof(Signal),
            entityId: signal.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddSignalSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSubjectAsync(
            command.OrganizationId,
            new SubjectInput(command.Kind, command.SubjectId, command.Note),
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        SignalSubject subject = signal.AddSubject(
            command.Kind, command.SubjectId, actor, now, command.Note);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SubjectAdded,
            $"Subject added: {command.Kind}.",
            actor,
            now));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return subject.Id;
    }

    public async Task HandleAsync(
        RemoveSignalSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Signal signal = await RequireAsync(
            command.OrganizationId, command.SignalId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        signal.RemoveSubject(command.SubjectRowId);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Signal,
            signal.Id.Value,
            IntelligenceEventKind.SubjectRemoved,
            "Subject removed.",
            actor,
            _clock.UtcNow));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refuses evidence that is not in this tenant.</summary>
    /// <remarks>
    /// <para>
    /// The composite foreign key enforces it too. This exists so the refusal is a
    /// sentence rather than a constraint violation at save time (ADR-0025).
    /// </para>
    /// <para>
    /// Answered as missing rather than as malformed, on M10's precedent. The
    /// request is well formed; the source named in it does not exist here, and
    /// saying which of those two it is would confirm the source exists somewhere
    /// else.
    /// </para>
    /// </remarks>
    private async Task RequireSourcesAsync(
        OrganizationId organizationId,
        IReadOnlyList<SignalEvidenceInput> evidence,
        CancellationToken cancellationToken)
    {
        IntelligenceSourceId[] ids = [.. evidence.Select(x => x.SourceId).Distinct()];

        if (!await _sources.AllExistAsync(organizationId, ids, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new EntityNotFoundException(
                nameof(IntelligenceSource), string.Join(", ", ids.Select(x => x.ToString())));
        }
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
            throw new EntityNotFoundException(
                subject.Kind.ToString(), subject.SubjectId.ToString());
        }
    }

    private async Task<Signal> RequireAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken) =>
        await _signals.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Signal), id.ToString());
}
