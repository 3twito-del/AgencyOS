using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="Prediction"/>.</summary>
public readonly record struct PredictionId(Guid Value)
{
    public static PredictionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Where a prediction stands.</summary>
public enum PredictionStatus
{
    /// <summary>Open, with a deadline that has not passed.</summary>
    Open = 1,

    /// <summary>The deadline passed and nobody has resolved it yet.</summary>
    /// <remarks>
    /// Derived at read time rather than stored, because it changes with the clock
    /// and a stored value would be wrong between one write and the next.
    /// </remarks>
    AwaitingResolution = 2,

    /// <summary>Resolved. The outcome says which way.</summary>
    Resolved = 3,

    /// <summary>Withdrawn before its deadline. Not a failed forecast.</summary>
    Cancelled = 4,
}

/// <summary>
/// What actually happened.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Unresolvable"/> is a real answer, not a polite failure. A question
/// that turned out to be ambiguous, or whose outcome nobody can establish, is a
/// badly-written prediction rather than a wrong one. It is excluded from
/// calibration entirely — scoring it as a half-right guess would be inventing a
/// number (ADR-0030).
/// </para>
/// </remarks>
public enum PredictionOutcome
{
    /// <summary>The predicted event happened by the deadline.</summary>
    Yes = 1,

    /// <summary>It did not.</summary>
    No = 2,

    /// <summary>Nobody can say. Never scored, never counted as wrong.</summary>
    Unresolvable = 3,
}

/// <summary>
/// A falsifiable statement about a future outcome, with a stated probability.
/// </summary>
/// <remarks>
/// <para>
/// The fourth link in the chain, and deliberately the narrowest. M11 predictions
/// are <strong>binary</strong>: "will X happen by Y". Continuous and multi-outcome
/// forecasts need scoring rules, aggregation and a way to express partial credit,
/// none of which has a caller yet — and a v1 that guessed at them would be a worse
/// foundation than one that refused (ADR-0030).
/// </para>
/// <para>
/// <strong>The probability is a person's assertion.</strong> Not a model output,
/// not a statistical inference, and never AgencyOS's own view. Every surface names
/// the forecaster and the date, because "65%" with no author is a number that
/// acquires false authority the moment somebody quotes it.
/// </para>
/// <para>
/// The current probability is <em>derived from the latest revision</em> rather than
/// stored, on the M9 precedent about balances: a stored copy is a second truth that
/// drifts, and here it would also erase the forecasting history that makes
/// calibration possible.
/// </para>
/// </remarks>
public sealed class Prediction
{
    private readonly List<PredictionRevision> _revisions = [];
    private readonly List<PredictionSubject> _subjects = [];
    private readonly List<PredictionEvidence> _evidence = [];

    private Prediction()
    {
    }

    public PredictionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The falsifiable statement.</summary>
    public string Statement { get; private set; } = string.Empty;

    /// <summary>What would count as the event happening, if that needs saying.</summary>
    public string? ResolutionCriteria { get; private set; }

    /// <summary>The moment by which the event must happen for the answer to be Yes.</summary>
    public DateTimeOffset ResolvesBy { get; private set; }

    /// <summary>Whose forecast this is.</summary>
    public UserId OwnerUserId { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    public PredictionOutcome? Outcome { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public UserId? ResolvedBy { get; private set; }

    public string? ResolutionNote { get; private set; }

    /// <summary>Set only when the prediction was withdrawn before its deadline.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelledReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public int Version { get; private set; }

    /// <summary>Every forecast ever stated, oldest first. Append-only.</summary>
    public IReadOnlyList<PredictionRevision> Revisions => _revisions;

    public IReadOnlyList<PredictionSubject> Subjects => _subjects;

    public IReadOnlyList<PredictionEvidence> Evidence => _evidence;

    /// <summary>The latest stated probability. Derived, never stored.</summary>
    public decimal CurrentProbability =>
        _revisions.Count == 0 ? 0m : _revisions[^1].Probability;

    /// <summary>The latest forecast, whole.</summary>
    public PredictionRevision? LatestRevision =>
        _revisions.Count == 0 ? null : _revisions[^1];

    public bool IsResolved => Outcome is not null;

    public bool IsCancelled => CancelledAt is not null;

    /// <summary>Whether a new forecast can still be recorded.</summary>
    public bool IsOpen => !IsResolved && !IsCancelled;

    /// <summary>Where the prediction stands, as of a moment.</summary>
    /// <remarks>
    /// Takes the clock as an argument rather than reading it, so a projection is
    /// reproducible and a test does not depend on wall-clock time.
    /// </remarks>
    public PredictionStatus StatusAt(DateTimeOffset now)
    {
        if (IsCancelled)
        {
            return PredictionStatus.Cancelled;
        }

        if (IsResolved)
        {
            return PredictionStatus.Resolved;
        }

        return now > ResolvesBy ? PredictionStatus.AwaitingResolution : PredictionStatus.Open;
    }

    /// <summary>
    /// The Brier score, once resolved Yes or No.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>(probability - outcome)²</c>, where outcome is 1 or 0. Zero is a perfect
    /// forecast and one is confidently wrong. It is computed from the
    /// <em>latest</em> forecast before resolution, which is the number the
    /// forecaster actually stood behind at the end.
    /// </para>
    /// <para>
    /// Null for an unresolved, cancelled or unresolvable prediction. Scoring an
    /// unresolvable question would manufacture a result from an absence
    /// (ADR-0030).
    /// </para>
    /// </remarks>
    public decimal? BrierScore
    {
        get
        {
            if (Outcome is not (PredictionOutcome.Yes or PredictionOutcome.No))
            {
                return null;
            }

            if (_revisions.Count == 0)
            {
                return null;
            }

            decimal actual = Outcome == PredictionOutcome.Yes ? 1m : 0m;
            decimal error = CurrentProbability - actual;

            return error * error;
        }
    }

    public static Prediction Create(
        OrganizationId organizationId,
        string statement,
        DateTimeOffset resolvesBy,
        decimal probability,
        IntelligenceSensitivity sensitivity,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? resolutionCriteria = null,
        string? rationale = null)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        if (resolvesBy <= now)
        {
            throw new DomainException(
                "A prediction resolves in the future. A deadline that has already passed "
                    + "is a question about the past, which is a signal.");
        }

        Prediction prediction = new()
        {
            Id = PredictionId.New(),
            OrganizationId = organizationId,
            Statement = Ensure.NotBlankMax(statement, nameof(statement), 2000),
            ResolutionCriteria = Ensure.OptionalMax(
                resolutionCriteria, nameof(resolutionCriteria), 4000),
            ResolvesBy = resolvesBy,
            OwnerUserId = ownerUserId,
            Sensitivity = sensitivity,
            CreatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        // The opening forecast is a revision, so the calibration history is
        // complete from the first row rather than from the first change.
        prediction._revisions.Add(PredictionRevision.Create(
            organizationId,
            prediction.Id,
            sequence: 1,
            probability,
            createdBy,
            now,
            rationale));

        return prediction;
    }

    /// <summary>
    /// States a new forecast, preserving every earlier one.
    /// </summary>
    /// <remarks>
    /// The whole point of the model. Overwriting 65% with 45% would let the record
    /// claim the analyst always thought 45%, which destroys both the honesty and
    /// the calibration (ADR-0030).
    /// </remarks>
    public PredictionRevision Revise(
        decimal probability,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion,
        string? rationale = null)
    {
        RequireVersion(expectedVersion);

        if (!IsOpen)
        {
            throw new DomainException(
                IsResolved
                    ? "That prediction is resolved. A forecast after the answer is known is "
                        + "not a forecast."
                    : "That prediction was cancelled.");
        }

        PredictionRevision revision = PredictionRevision.Create(
            OrganizationId,
            Id,
            _revisions.Count + 1,
            probability,
            actor,
            now,
            rationale);

        _revisions.Add(revision);

        Version++;

        return revision;
    }

    /// <summary>
    /// Records what happened.
    /// </summary>
    /// <remarks>
    /// Explicit and human. Nothing resolves a prediction from outside data: the
    /// system has no way to know that a limited series was ordered, and guessing
    /// would corrupt the one number calibration depends on.
    /// </remarks>
    public void Resolve(
        PredictionOutcome outcome,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion,
        string? note = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException($"'{outcome}' is not an outcome.");
        }

        if (IsResolved)
        {
            throw new DomainException("That prediction is already resolved.");
        }

        if (IsCancelled)
        {
            throw new DomainException("That prediction was cancelled and has no outcome.");
        }

        Outcome = outcome;
        ResolvedAt = now;
        ResolvedBy = actor;
        ResolutionNote = Ensure.OptionalMax(note, nameof(note), 2000);

        Version++;
    }

    /// <summary>Withdraws the prediction before its deadline.</summary>
    /// <remarks>
    /// Not an outcome and never scored. A question that stopped mattering is not a
    /// forecast the analyst got wrong.
    /// </remarks>
    public void Cancel(string reason, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (IsResolved)
        {
            throw new DomainException("A resolved prediction is not cancelled.");
        }

        if (IsCancelled)
        {
            throw new DomainException("That prediction is already cancelled.");
        }

        CancelledAt = now;
        CancelledReason = Ensure.NotBlankMax(reason, nameof(reason), 2000);

        Version++;
    }

    public PredictionSubject AddSubject(
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_subjects.Any(x => x.Kind == kind && x.SubjectId == subjectId))
        {
            throw new DomainException("That subject is already named on this prediction.");
        }

        PredictionSubject subject = PredictionSubject.Create(
            OrganizationId, Id, kind, subjectId, addedBy, now, note);

        _subjects.Add(subject);

        return subject;
    }

    public void RemoveSubject(Guid subjectRowId)
    {
        PredictionSubject subject = _subjects.SingleOrDefault(x => x.Id == subjectRowId)
            ?? throw new DomainException("That subject is not on this prediction.");

        _subjects.Remove(subject);
    }

    /// <summary>Cites a source or a signal, usually as resolution evidence.</summary>
    public PredictionEvidence AddEvidence(
        IntelligenceSourceId? sourceId,
        SignalId? signalId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if ((sourceId is null) == (signalId is null))
        {
            throw new DomainException(
                "Evidence names exactly one thing: a source or a signal.");
        }

        if (sourceId is { } source && _evidence.Any(x => x.SourceId == source))
        {
            throw new DomainException("That source is already cited on this prediction.");
        }

        if (signalId is { } signal && _evidence.Any(x => x.SignalId == signal))
        {
            throw new DomainException("That signal is already cited on this prediction.");
        }

        PredictionEvidence evidence = PredictionEvidence.Create(
            OrganizationId, Id, sourceId, signalId, addedBy, now, note);

        _evidence.Add(evidence);

        return evidence;
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Prediction), Id.ToString(), expectedVersion, Version);
        }
    }
}

/// <summary>
/// One forecast, at a moment, by a person.
/// </summary>
/// <remarks>
/// Immutable once written. The calibration record is exactly this list, and an
/// editable revision would let a forecaster improve their own history
/// (ADR-0030).
/// </remarks>
public sealed class PredictionRevision
{
    /// <summary>The most precision a stated forecast can carry.</summary>
    /// <remarks>
    /// Four decimal places — one basis point. Finer would be false precision on a
    /// number a person picked, and coarser would lose the difference between 2% and
    /// 3% at the tails where it matters most.
    /// </remarks>
    public const int ProbabilityScale = 4;

    private PredictionRevision()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PredictionId PredictionId { get; private set; }

    /// <summary>Position in the history, from one. Unique per prediction.</summary>
    public int Sequence { get; private set; }

    /// <summary>
    /// The stated probability, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// <c>decimal</c>, never a float. A forecast is a value somebody typed and
    /// later compared against an outcome, and binary floating point would make two
    /// equal forecasts unequal (ADR-0023, ADR-0030).
    /// </remarks>
    public decimal Probability { get; private set; }

    /// <summary>Why, in the forecaster's words.</summary>
    public string? Rationale { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    internal static PredictionRevision Create(
        OrganizationId organizationId,
        PredictionId predictionId,
        int sequence,
        decimal probability,
        UserId recordedBy,
        DateTimeOffset now,
        string? rationale) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PredictionId = predictionId,
            Sequence = sequence,
            Probability = RequireProbability(probability),
            Rationale = Ensure.OptionalMax(rationale, nameof(rationale), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
        };

    /// <summary>Refuses anything that is not a probability.</summary>
    private static decimal RequireProbability(decimal probability)
    {
        if (probability < 0m || probability > 1m)
        {
            throw new DomainException(
                $"A probability is between 0 and 1. {probability} is not one.");
        }

        if (decimal.Round(probability, ProbabilityScale) != probability)
        {
            throw new DomainException(
                $"A probability carries at most {ProbabilityScale} decimal places. "
                    + "Finer is precision nobody stated.");
        }

        return probability;
    }
}

/// <summary>A source or signal cited on a prediction.</summary>
public sealed class PredictionEvidence
{
    private PredictionEvidence()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PredictionId PredictionId { get; private set; }

    public IntelligenceSourceId? SourceId { get; private set; }

    public SignalId? SignalId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    internal static PredictionEvidence Create(
        OrganizationId organizationId,
        PredictionId predictionId,
        IntelligenceSourceId? sourceId,
        SignalId? signalId,
        UserId addedBy,
        DateTimeOffset now,
        string? note) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PredictionId = predictionId,
            SourceId = sourceId,
            SignalId = signalId,
            Note = Ensure.OptionalMax(note, nameof(note), 2000),
            AddedAt = now,
            AddedBy = addedBy,
        };
}
