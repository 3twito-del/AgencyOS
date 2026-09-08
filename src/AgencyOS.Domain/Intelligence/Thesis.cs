using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="Thesis"/>.</summary>
public readonly record struct ThesisId(Guid Value)
{
    public static ThesisId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where a thesis stands.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>True</c> and no <c>False</c>. A thesis is an interpretation, and
/// interpretations do not become historical falsehoods — they stop being held, get
/// overtaken, or are replaced by a better reading of the same facts. Marking one
/// "False" would also destroy the reason to keep it: knowing what the agency
/// believed in March is useful precisely because it turned out to be wrong
/// (ADR-0030).
/// </para>
/// </remarks>
public enum ThesisStatus
{
    /// <summary>Being written. Not yet something the agency holds.</summary>
    Draft = 1,

    /// <summary>Currently held.</summary>
    Active = 2,

    /// <summary>No longer held. The reasoning stays readable.</summary>
    Retired = 3,

    /// <summary>Replaced by another thesis, which is named.</summary>
    Superseded = 4,
}

/// <summary>How strongly the analyst currently holds the interpretation.</summary>
/// <remarks>
/// Not a probability. A thesis is not an event that resolves, so a percentage would
/// be a number nobody could ever check. A prediction is where falsifiable
/// probability belongs (ADR-0030).
/// </remarks>
public enum ThesisConfidence
{
    Unstated = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>What a signal does to a thesis.</summary>
/// <remarks>
/// Three stances rather than a score. Five weak signals do not mechanically
/// outweigh one strong contradiction, and a system that counted them would say they
/// did. Evidence here is organized for a person to weigh, not summed (ADR-0030).
/// </remarks>
public enum ThesisEvidenceStance
{
    /// <summary>Reason to hold the thesis.</summary>
    Supports = 1,

    /// <summary>Reason to doubt it.</summary>
    Challenges = 2,

    /// <summary>Neither, but a reader needs it to understand the thesis.</summary>
    Context = 3,
}

/// <summary>
/// A structured analyst interpretation: what we believe, and why.
/// </summary>
/// <remarks>
/// <para>
/// The third link in the chain, and the first one that is somebody's judgment
/// rather than a report of the world. A signal says a thing was reported; a thesis
/// says what somebody here thinks it means (ADR-0030).
/// </para>
/// <para>
/// <strong>The reasoning is never overwritten.</strong> Materially changing the
/// proposition, the confidence or the rationale appends a
/// <see cref="ThesisRevision"/>, so the system can answer "what did we believe six
/// months ago" and not only "what does it say now". A thesis that quietly rewrote
/// itself would make every past decision look better informed than it was.
/// </para>
/// </remarks>
public sealed class Thesis
{
    private readonly List<ThesisRevision> _revisions = [];
    private readonly List<ThesisSubject> _subjects = [];
    private readonly List<ThesisEvidence> _evidence = [];

    private Thesis()
    {
    }

    public ThesisId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>The current proposition. Every earlier one is in the revisions.</summary>
    public string Proposition { get; private set; } = string.Empty;

    /// <summary>The current reasoning. Every earlier one is in the revisions.</summary>
    public string? Rationale { get; private set; }

    public ThesisStatus Status { get; private set; }

    public ThesisConfidence Confidence { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    /// <summary>Whose interpretation this is.</summary>
    public UserId OwnerUserId { get; private set; }

    /// <summary>The thesis that replaced this one, when it was superseded.</summary>
    public ThesisId? SupersededByThesisId { get; private set; }

    /// <summary>Why it was retired or superseded.</summary>
    public string? ClosedReason { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public int Version { get; private set; }

    /// <summary>Every stated position, oldest first. Append-only.</summary>
    public IReadOnlyList<ThesisRevision> Revisions => _revisions;

    public IReadOnlyList<ThesisSubject> Subjects => _subjects;

    public IReadOnlyList<ThesisEvidence> Evidence => _evidence;

    /// <summary>Whether the agency still holds this interpretation.</summary>
    public bool IsHeld => Status is ThesisStatus.Draft or ThesisStatus.Active;

    public static Thesis Create(
        OrganizationId organizationId,
        string title,
        string proposition,
        IntelligenceSensitivity sensitivity,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        ThesisConfidence confidence = ThesisConfidence.Unstated,
        string? rationale = null)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        if (!Enum.IsDefined(confidence))
        {
            throw new DomainException($"'{confidence}' is not a confidence.");
        }

        Thesis thesis = new()
        {
            Id = ThesisId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 300),
            Proposition = Ensure.NotBlankMax(proposition, nameof(proposition), 4000),
            Rationale = Ensure.OptionalMax(rationale, nameof(rationale), 8000),
            Status = ThesisStatus.Draft,
            Confidence = confidence,
            Sensitivity = sensitivity,
            OwnerUserId = ownerUserId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        // The opening position is a revision like any other, so the history is
        // complete from the first row rather than starting at the first edit.
        thesis._revisions.Add(ThesisRevision.Create(
            organizationId,
            thesis.Id,
            sequence: 1,
            thesis.Proposition,
            thesis.Rationale,
            confidence,
            createdBy,
            now,
            "Thesis created."));

        return thesis;
    }

    /// <summary>Puts a drafted thesis into use.</summary>
    public void Activate(DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != ThesisStatus.Draft)
        {
            throw new DomainException($"A thesis is only activated from draft, not from {Status}.");
        }

        Status = ThesisStatus.Active;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Records a changed position, preserving the previous one.
    /// </summary>
    /// <remarks>
    /// Appends rather than edits. The current fields on the aggregate are a
    /// convenience for reading; the revisions are the record (ADR-0030).
    /// </remarks>
    public ThesisRevision Revise(
        string proposition,
        ThesisConfidence confidence,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion,
        string? rationale = null,
        string? changeNote = null)
    {
        RequireVersion(expectedVersion);
        RequireHeld();

        if (!Enum.IsDefined(confidence))
        {
            throw new DomainException($"'{confidence}' is not a confidence.");
        }

        Proposition = Ensure.NotBlankMax(proposition, nameof(proposition), 4000);
        Rationale = Ensure.OptionalMax(rationale, nameof(rationale), 8000);
        Confidence = confidence;
        UpdatedAt = now;
        Version++;

        ThesisRevision revision = ThesisRevision.Create(
            OrganizationId,
            Id,
            _revisions.Count + 1,
            Proposition,
            Rationale,
            confidence,
            actor,
            now,
            changeNote);

        _revisions.Add(revision);

        return revision;
    }

    /// <summary>Stops holding the thesis. The reasoning stays readable.</summary>
    public void Retire(string reason, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireHeld();

        Status = ThesisStatus.Retired;
        ClosedReason = Ensure.NotBlankMax(reason, nameof(reason), 2000);
        ClosedAt = now;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Replaces this thesis with a named successor.</summary>
    public void Supersede(
        ThesisId successor,
        string reason,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireHeld();

        if (successor == Id)
        {
            throw new DomainException("A thesis cannot supersede itself.");
        }

        Status = ThesisStatus.Superseded;
        SupersededByThesisId = successor;
        ClosedReason = Ensure.NotBlankMax(reason, nameof(reason), 2000);
        ClosedAt = now;
        UpdatedAt = now;
        Version++;
    }

    public ThesisSubject AddSubject(
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_subjects.Any(x => x.Kind == kind && x.SubjectId == subjectId))
        {
            throw new DomainException("That subject is already named on this thesis.");
        }

        ThesisSubject subject = ThesisSubject.Create(
            OrganizationId, Id, kind, subjectId, addedBy, now, note);

        _subjects.Add(subject);

        return subject;
    }

    public void RemoveSubject(Guid subjectRowId)
    {
        ThesisSubject subject = _subjects.SingleOrDefault(x => x.Id == subjectRowId)
            ?? throw new DomainException("That subject is not on this thesis.");

        _subjects.Remove(subject);
    }

    /// <summary>Cites a signal, saying what it does to the thesis.</summary>
    public ThesisEvidence AddEvidence(
        SignalId signalId,
        ThesisEvidenceStance stance,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (!Enum.IsDefined(stance))
        {
            throw new DomainException($"'{stance}' is not a stance a signal can take.");
        }

        if (_evidence.Any(x => x.SignalId == signalId))
        {
            throw new DomainException("That signal is already cited on this thesis.");
        }

        ThesisEvidence evidence = ThesisEvidence.Create(
            OrganizationId, Id, signalId, stance, addedBy, now, note);

        _evidence.Add(evidence);

        return evidence;
    }

    public void RemoveEvidence(Guid evidenceId)
    {
        ThesisEvidence evidence = _evidence.SingleOrDefault(x => x.Id == evidenceId)
            ?? throw new DomainException("That evidence is not on this thesis.");

        _evidence.Remove(evidence);
    }

    private void RequireHeld()
    {
        if (!IsHeld)
        {
            throw new DomainException(
                $"That thesis is {Status.ToString().ToLowerInvariant()}. A closed thesis is "
                    + "not revised; a new one supersedes it.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Thesis), Id.ToString(), expectedVersion, Version);
        }
    }
}

/// <summary>
/// One stated position of a thesis, at a moment, by a person.
/// </summary>
/// <remarks>
/// Immutable once written. This is what makes "what did we believe in March"
/// answerable, and editing one would quietly rewrite the agency's own memory
/// (ADR-0030).
/// </remarks>
public sealed class ThesisRevision
{
    private ThesisRevision()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ThesisId ThesisId { get; private set; }

    /// <summary>Position in the history, from one. Unique per thesis.</summary>
    public int Sequence { get; private set; }

    public string Proposition { get; private set; } = string.Empty;

    public string? Rationale { get; private set; }

    public ThesisConfidence Confidence { get; private set; }

    /// <summary>What changed, in the reviser's words.</summary>
    public string? ChangeNote { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    internal static ThesisRevision Create(
        OrganizationId organizationId,
        ThesisId thesisId,
        int sequence,
        string proposition,
        string? rationale,
        ThesisConfidence confidence,
        UserId recordedBy,
        DateTimeOffset now,
        string? changeNote) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ThesisId = thesisId,
            Sequence = sequence,
            Proposition = proposition,
            Rationale = rationale,
            Confidence = confidence,
            ChangeNote = Ensure.OptionalMax(changeNote, nameof(changeNote), 2000),
            RecordedAt = now,
            RecordedBy = recordedBy,
        };
}

/// <summary>A signal cited on a thesis, and what it does to it.</summary>
public sealed class ThesisEvidence
{
    private ThesisEvidence()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ThesisId ThesisId { get; private set; }

    public SignalId SignalId { get; private set; }

    public ThesisEvidenceStance Stance { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    internal static ThesisEvidence Create(
        OrganizationId organizationId,
        ThesisId thesisId,
        SignalId signalId,
        ThesisEvidenceStance stance,
        UserId addedBy,
        DateTimeOffset now,
        string? note) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ThesisId = thesisId,
            SignalId = signalId,
            Stance = stance,
            Note = Ensure.OptionalMax(note, nameof(note), 2000),
            AddedAt = now,
            AddedBy = addedBy,
        };
}
