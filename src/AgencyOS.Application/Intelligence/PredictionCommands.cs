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

public sealed record CreatePredictionCommand(
    OrganizationId OrganizationId,
    string Statement,
    DateTimeOffset ResolvesBy,
    decimal Probability,
    IntelligenceSensitivity Sensitivity,
    Guid? OwnerUserId = null,
    string? ResolutionCriteria = null,
    string? Rationale = null,
    IReadOnlyList<SubjectInput>? Subjects = null);

/// <summary>
/// States a new forecast.
/// </summary>
/// <remarks>
/// Appends. The earlier probability is never replaced, because the record of what
/// somebody believed and when is the only thing that makes calibration meaningful
/// (ADR-0030).
/// </remarks>
public sealed record RecordPredictionRevisionCommand(
    OrganizationId OrganizationId,
    PredictionId PredictionId,
    decimal Probability,
    int ExpectedVersion,
    string? Rationale = null);

public sealed record ResolvePredictionCommand(
    OrganizationId OrganizationId,
    PredictionId PredictionId,
    PredictionOutcome Outcome,
    int ExpectedVersion,
    string? Note = null,
    IReadOnlyList<PredictionEvidenceInput>? Evidence = null);

public sealed record CancelPredictionCommand(
    OrganizationId OrganizationId,
    PredictionId PredictionId,
    string Reason,
    int ExpectedVersion);

/// <summary>A source or a signal cited on a prediction. Exactly one of the two.</summary>
public sealed record PredictionEvidenceInput(
    IntelligenceSourceId? SourceId = null,
    SignalId? SignalId = null,
    string? Note = null);

public sealed record AddPredictionSubjectCommand(
    OrganizationId OrganizationId,
    PredictionId PredictionId,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);

// --------------------------------------------------------------------- handler

/// <summary>
/// Creates, revises and resolves forecasts.
/// </summary>
/// <remarks>
/// <para>
/// Every probability here is an assertion by a named person at a stated time. It
/// is not a model output, not a statistical inference and not AgencyOS's own view,
/// and the forecaster travels with the number everywhere it is shown.
/// </para>
/// <para>
/// Resolution is human and explicit. Nothing resolves a prediction from outside
/// data: the system has no way to know a limited series was ordered, and guessing
/// would corrupt the one number calibration rests on (ADR-0030).
/// </para>
/// </remarks>
public sealed class PredictionHandler
{
    private readonly IPredictionRepository _predictions;
    private readonly IIntelligenceSourceRepository _sources;
    private readonly ISignalRepository _signals;
    private readonly IIntelligenceSubjectValidator _subjects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public PredictionHandler(
        IPredictionRepository predictions,
        IIntelligenceSourceRepository sources,
        ISignalRepository signals,
        IIntelligenceSubjectValidator subjects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _predictions = predictions;
        _sources = sources;
        _signals = signals;
        _subjects = subjects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<PredictionId> HandleAsync(
        CreatePredictionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeForecastAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            await RequireSubjectAsync(
                command.OrganizationId, subject, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        Prediction prediction = Prediction.Create(
            command.OrganizationId,
            command.Statement,
            command.ResolvesBy,
            command.Probability,
            command.Sensitivity,
            command.OwnerUserId is { } owner ? new UserId(owner) : actor,
            actor,
            now,
            command.ResolutionCriteria,
            command.Rationale);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            prediction.AddSubject(subject.Kind, subject.SubjectId, actor, now, subject.Note);
        }

        _predictions.Add(prediction);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Prediction,
            prediction.Id.Value,
            IntelligenceEventKind.PredictionCreated,
            $"Prediction opened at {Percent(command.Probability)}.",
            actor,
            now,
            $"Resolves by {command.ResolvesBy:u}"));

        _audit.Record(
            AuditAction.PredictionCreated,
            entityType: nameof(Prediction),
            entityId: prediction.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligencePredictionsWrite,

            // The probability and the deadline, never the statement or the
            // rationale. A forecast about a named client's future is exactly the
            // kind of thing that should not appear in an exported log (ADR-0030).
            semanticDelta: new
            {
                command.Probability,
                command.ResolvesBy,
                Sensitivity = prediction.Sensitivity.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return prediction.Id;
    }

    public async Task<Guid> HandleAsync(
        RecordPredictionRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Prediction prediction = await RequireAsync(
            command.OrganizationId, command.PredictionId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeForecastAsync(
                command.OrganizationId, prediction.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        decimal previous = prediction.CurrentProbability;

        PredictionRevision revision = prediction.Revise(
            command.Probability, actor, now, command.ExpectedVersion, command.Rationale);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Prediction,
            prediction.Id.Value,
            IntelligenceEventKind.PredictionRevised,
            $"Forecast moved from {Percent(previous)} to {Percent(command.Probability)}.",
            actor,
            now,
            command.Rationale));

        _audit.Record(
            AuditAction.PredictionRevised,
            entityType: nameof(Prediction),
            entityId: prediction.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligencePredictionsWrite,
            semanticDelta: new
            {
                revision.Sequence,
                From = previous,
                To = command.Probability,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return revision.Id;
    }

    public async Task HandleAsync(
        ResolvePredictionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Prediction prediction = await RequireAsync(
            command.OrganizationId, command.PredictionId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeForecastAsync(
                command.OrganizationId, prediction.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        foreach (PredictionEvidenceInput evidence in command.Evidence ?? [])
        {
            await RequireEvidenceAsync(command.OrganizationId, evidence, cancellationToken)
                .ConfigureAwait(false);

            prediction.AddEvidence(
                evidence.SourceId, evidence.SignalId, actor, now, evidence.Note);
        }

        prediction.Resolve(command.Outcome, actor, now, command.ExpectedVersion, command.Note);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Prediction,
            prediction.Id.Value,
            IntelligenceEventKind.PredictionResolved,
            command.Outcome switch
            {
                PredictionOutcome.Yes => "Resolved: it happened.",
                PredictionOutcome.No => "Resolved: it did not happen.",

                // Said as itself. Not a failure, and not scored.
                _ => "Resolved: nobody can establish what happened.",
            },
            actor,
            now,
            command.Note));

        _audit.Record(
            AuditAction.PredictionResolved,
            entityType: nameof(Prediction),
            entityId: prediction.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligencePredictionsWrite,
            semanticDelta: new
            {
                Outcome = command.Outcome.ToString(),
                FinalProbability = prediction.CurrentProbability,
                prediction.BrierScore,
            },
            reason: command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        CancelPredictionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Prediction prediction = await RequireAsync(
            command.OrganizationId, command.PredictionId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeForecastAsync(
                command.OrganizationId, prediction.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        prediction.Cancel(command.Reason, now, command.ExpectedVersion);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.Prediction,
            prediction.Id.Value,
            IntelligenceEventKind.PredictionCancelled,
            "Prediction withdrawn before its deadline.",
            actor,
            now,
            command.Reason));

        _audit.Record(
            AuditAction.PredictionCancelled,
            entityType: nameof(Prediction),
            entityId: prediction.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligencePredictionsWrite,
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddPredictionSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Prediction prediction = await RequireAsync(
            command.OrganizationId, command.PredictionId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeForecastAsync(
                command.OrganizationId, prediction.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSubjectAsync(
            command.OrganizationId,
            new SubjectInput(command.Kind, command.SubjectId, command.Note),
            cancellationToken).ConfigureAwait(false);

        PredictionSubject subject = prediction.AddSubject(
            command.Kind, command.SubjectId, actor, _clock.UtcNow, command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return subject.Id;
    }

    /// <summary>Renders a probability the way a person stated it.</summary>
    private static string Percent(decimal probability) =>
        $"{decimal.Round(probability * 100m, 2)}%";

    private async Task RequireEvidenceAsync(
        OrganizationId organizationId,
        PredictionEvidenceInput evidence,
        CancellationToken cancellationToken)
    {
        if (evidence.SourceId is { } source)
        {
            if (!await _sources.AllExistAsync(organizationId, [source], cancellationToken)
                .ConfigureAwait(false))
            {
                throw new DomainException("That source is not in this organization.");
            }
        }

        if (evidence.SignalId is { } signal)
        {
            if (!await _signals.ExistsAsync(organizationId, signal, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new DomainException("That signal is not in this organization.");
            }
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
            throw new DomainException(
                $"There is no {subject.Kind} with that identifier in this organization.");
        }
    }

    private async Task<Prediction> RequireAsync(
        OrganizationId organizationId,
        PredictionId id,
        CancellationToken cancellationToken) =>
        await _predictions.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Prediction), id.ToString());
}
