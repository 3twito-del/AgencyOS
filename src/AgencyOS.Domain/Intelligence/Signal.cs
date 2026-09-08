using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="Signal"/>.</summary>
public readonly record struct SignalId(Guid Value)
{
    public static SignalId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How sensitive a piece of intelligence is, as a person classified it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never inferred.</strong> Not from the words in a claim, not from the
/// subject, not from the source. The same rule M8 applied to contract clauses and
/// M10 applied to files, applied here for the same reason: a system that guessed
/// would be wrong in both directions, marking gossip confidential and leaving a
/// recruiting thesis open (ADR-0022, ADR-0025, ADR-0030).
/// </para>
/// <para>
/// A separate enum from <c>DocumentSensitivity</c>. They overlap and they are not
/// the same question: a document is classified by what it contains, and a signal by
/// what knowing it would reveal. <see cref="SourceSensitive"/> has no document
/// equivalent at all — it protects who told us rather than what they said.
/// </para>
/// </remarks>
public enum IntelligenceSensitivity
{
    /// <summary>Ordinary working intelligence. Anybody with the read grant.</summary>
    Internal = 1,

    /// <summary>Commercially sensitive: strategy, recruiting interest, market reads.</summary>
    Confidential = 2,

    /// <summary>
    /// The claim is ordinary; who supplied it is not.
    /// </summary>
    /// <remarks>
    /// The case an intelligence system exists to handle honestly. Somebody spoke on
    /// the understanding it would not be traced back, and the protection has to
    /// cover the provenance rather than the fact.
    /// </remarks>
    SourceSensitive = 3,

    /// <summary>Restricted to the people explicitly granted it.</summary>
    Restricted = 4,
}

/// <summary>What kind of thing a signal reports.</summary>
public enum SignalKind
{
    /// <summary>Somebody moved, was hired, was promoted or left.</summary>
    PersonnelMove = 1,

    /// <summary>A project was set up, ordered, greenlit, moved or shelved.</summary>
    ProjectStatus = 2,

    /// <summary>A company bought, sold, merged, restructured or raised.</summary>
    CorporateAction = 3,

    /// <summary>A deal was reported: an acquisition, an option, a package.</summary>
    DealActivity = 4,

    /// <summary>Somebody stated an appetite, a mandate or a direction.</summary>
    MarketAppetite = 5,

    /// <summary>A person's representation, availability or standing changed.</summary>
    TalentActivity = 6,

    /// <summary>A credit, award, festival placement or reception.</summary>
    CreditOrRecognition = 7,

    /// <summary>Something a person observed that fits none of the above.</summary>
    Observation = 99,
}

/// <summary>
/// Where a claim stands, as people here have assessed it.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>Verified</c>. AgencyOS cannot verify anything about the outside
/// world, and a status saying it had would be the single most misleading word in
/// the milestone. <see cref="Corroborated"/> is the strongest thing the system can
/// honestly say, and it means a person looked at independent evidence and said so
/// (ADR-0030).
/// </para>
/// <para>
/// Nothing moves between these automatically. Two trade outlets running the same
/// wire story are one source wearing two hats, and no count of URLs corroborates
/// anything.
/// </para>
/// </remarks>
public enum SignalVerification
{
    /// <summary>Recorded, and nobody has assessed it further.</summary>
    Unverified = 1,

    /// <summary>A person judged independent evidence to support it, and said so.</summary>
    Corroborated = 2,

    /// <summary>Somebody has recorded a reason to doubt it.</summary>
    Disputed = 3,

    /// <summary>The claim was withdrawn. The row stays; the claim is no longer stood behind.</summary>
    Retracted = 4,
}

/// <summary>
/// How confident the recorder is in this particular claim.
/// </summary>
/// <remarks>
/// Distinct from the reliability of any source behind it. A highly reliable outlet
/// can report something the recorder still reads as speculative, and a rumour from
/// a weak source can be one the recorder is sure of for other reasons
/// (ADR-0030).
/// </remarks>
public enum SignalConfidence
{
    /// <summary>Nobody has said.</summary>
    Unstated = 0,

    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>How a piece of evidence bears on a signal.</summary>
public enum SignalEvidenceRole
{
    /// <summary>The evidence the claim was read out of.</summary>
    Primary = 1,

    /// <summary>Independent evidence that supports the same claim.</summary>
    Corroborating = 2,

    /// <summary>Evidence that contradicts or complicates it.</summary>
    Contradicting = 3,

    /// <summary>Background that helps a reader place the claim.</summary>
    Context = 4,
}

/// <summary>
/// One atomic intelligence claim: what happened, was reported or was observed.
/// </summary>
/// <remarks>
/// <para>
/// The second link in the chain. A source exists; a signal is what somebody read
/// out of it. They are separate rows because they fail separately — the article
/// can be real and the claim wrong, and collapsing them would make that
/// unsayable (ADR-0030).
/// </para>
/// <para>
/// A signal is <strong>not</strong> a business-state change. "Executive X expressed
/// positive interest in Project Y" is evidence somebody said that; moving an
/// opportunity target to Interested is an M6 command a person issues. M10
/// established that rule for linked correspondence and M11 keeps it.
/// </para>
/// <para>
/// <strong>Provenance is required.</strong> A signal is created with at least one
/// source and can never be left with none. A claim nobody can trace is a rumour
/// wearing a record's clothes.
/// </para>
/// </remarks>
public sealed class Signal
{
    private readonly List<SignalSubject> _subjects = [];
    private readonly List<SignalEvidence> _evidence = [];

    private Signal()
    {
    }

    public SignalId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>A short name for lists.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The claim itself, stated plainly.</summary>
    public string Claim { get; private set; } = string.Empty;

    public SignalKind Kind { get; private set; }

    /// <summary>
    /// When the thing happened, where that is known.
    /// </summary>
    /// <remarks>
    /// Often unknown, and null says so. A hiring reported on Friday may have
    /// happened weeks earlier, and guessing would date the agency's own record
    /// wrongly.
    /// </remarks>
    public DateTimeOffset? OccurredAt { get; private set; }

    /// <summary>When somebody here observed or was told the claim.</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    public SignalVerification Verification { get; private set; }

    /// <summary>Why the verification status is what it is.</summary>
    public string? VerificationNote { get; private set; }

    public UserId? VerificationChangedBy { get; private set; }

    public DateTimeOffset? VerificationChangedAt { get; private set; }

    public SignalConfidence Confidence { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<SignalSubject> Subjects => _subjects;

    public IReadOnlyList<SignalEvidence> Evidence => _evidence;

    /// <summary>Whether the claim is still stood behind.</summary>
    public bool IsStandingClaim => Verification != SignalVerification.Retracted;

    public static Signal Record(
        OrganizationId organizationId,
        string title,
        string claim,
        SignalKind kind,
        IntelligenceSensitivity sensitivity,
        UserId recordedBy,
        DateTimeOffset now,
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? observedAt = null,
        SignalConfidence confidence = SignalConfidence.Unstated,
        string? notes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not a kind of signal.");
        }

        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        if (!Enum.IsDefined(confidence))
        {
            throw new DomainException($"'{confidence}' is not a confidence.");
        }

        DateTimeOffset observed = observedAt ?? now;

        // Something cannot be observed before it happened. The gap in the other
        // direction is ordinary and says nothing.
        if (occurredAt is { } occurred && occurred > observed)
        {
            throw new DomainException(
                "A signal cannot have been observed before the thing it reports happened.");
        }

        return new Signal
        {
            Id = SignalId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 300),
            Claim = Ensure.NotBlankMax(claim, nameof(claim), 2000),
            Kind = kind,
            OccurredAt = occurredAt,
            ObservedAt = observed,

            // Unverified, always. One source is not corroboration and the system
            // will not start a claim anywhere stronger than "somebody recorded it".
            Verification = SignalVerification.Unverified,
            Confidence = confidence,
            Sensitivity = sensitivity,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
            Version = 1,
        };
    }

    /// <summary>Names something this signal is about.</summary>
    public SignalSubject AddSubject(
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_subjects.Any(x => x.Kind == kind && x.SubjectId == subjectId))
        {
            throw new DomainException(
                "That subject is already named on this signal.");
        }

        SignalSubject subject = SignalSubject.Create(
            OrganizationId, Id, kind, subjectId, addedBy, now, note);

        _subjects.Add(subject);

        return subject;
    }

    public void RemoveSubject(Guid subjectRowId)
    {
        SignalSubject subject = _subjects.SingleOrDefault(x => x.Id == subjectRowId)
            ?? throw new DomainException("That subject is not on this signal.");

        _subjects.Remove(subject);
    }

    /// <summary>Attaches evidence, with what it does for the claim.</summary>
    public SignalEvidence AddEvidence(
        IntelligenceSourceId sourceId,
        SignalEvidenceRole role,
        UserId addedBy,
        DateTimeOffset now,
        string? excerpt = null,
        string? locator = null)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"'{role}' is not a way evidence can bear on a claim.");
        }

        if (_evidence.Any(x => x.SourceId == sourceId))
        {
            throw new DomainException("That source is already attached to this signal.");
        }

        SignalEvidence evidence = SignalEvidence.Create(
            OrganizationId, Id, sourceId, role, addedBy, now, excerpt, locator);

        _evidence.Add(evidence);

        return evidence;
    }

    /// <summary>
    /// Detaches evidence, refusing to leave the claim with none.
    /// </summary>
    /// <remarks>
    /// The invariant the whole model rests on. A signal with no provenance is a
    /// rumour that has been promoted to a record, and there is no way to tell
    /// afterwards which it was (ADR-0030).
    /// </remarks>
    public void RemoveEvidence(Guid evidenceId)
    {
        SignalEvidence evidence = _evidence.SingleOrDefault(x => x.Id == evidenceId)
            ?? throw new DomainException("That evidence is not on this signal.");

        if (_evidence.Count == 1)
        {
            throw new DomainException(
                "A signal keeps at least one source. Retract the signal instead of "
                    + "leaving the claim with no provenance.");
        }

        _evidence.Remove(evidence);
    }

    /// <summary>
    /// Records that a person judged independent evidence to support the claim.
    /// </summary>
    /// <remarks>
    /// An explicit act, never a consequence of the evidence count. Two outlets
    /// running one wire story are one source, and only a person can tell that from
    /// two newsrooms that each made a call.
    /// </remarks>
    public void Corroborate(UserId actor, DateTimeOffset now, int expectedVersion, string? note = null)
    {
        RequireVersion(expectedVersion);
        RequireNotRetracted();

        Verification = SignalVerification.Corroborated;

        Note(actor, now, note);
    }

    /// <summary>Records a reason to doubt the claim.</summary>
    public void Dispute(UserId actor, DateTimeOffset now, int expectedVersion, string note)
    {
        RequireVersion(expectedVersion);
        RequireNotRetracted();

        Verification = SignalVerification.Disputed;

        Note(actor, now, Ensure.NotBlankMax(note, nameof(note), 2000));
    }

    /// <summary>
    /// Withdraws the claim.
    /// </summary>
    /// <remarks>
    /// Terminal, and not a deletion. The row stays, everything built on it stays
    /// visible, and anybody reading a thesis that cited this signal can see that
    /// its evidence was withdrawn — which is the point.
    /// </remarks>
    public void Retract(UserId actor, DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);

        if (Verification == SignalVerification.Retracted)
        {
            throw new DomainException("That signal has already been retracted.");
        }

        Verification = SignalVerification.Retracted;

        Note(actor, now, Ensure.NotBlankMax(reason, nameof(reason), 2000));
    }

    /// <summary>Corrects what was recorded, without changing where it stands.</summary>
    public void Update(
        string title,
        string claim,
        SignalKind kind,
        IntelligenceSensitivity sensitivity,
        SignalConfidence confidence,
        int expectedVersion,
        string? notes = null)
    {
        RequireVersion(expectedVersion);
        RequireNotRetracted();

        Title = Ensure.NotBlankMax(title, nameof(title), 300);
        Claim = Ensure.NotBlankMax(claim, nameof(claim), 2000);
        Kind = Enum.IsDefined(kind) ? kind : throw new DomainException($"'{kind}' is not a kind of signal.");
        Sensitivity = Enum.IsDefined(sensitivity)
            ? sensitivity
            : throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        Confidence = Enum.IsDefined(confidence)
            ? confidence
            : throw new DomainException($"'{confidence}' is not a confidence.");
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        Version++;
    }

    private void Note(UserId actor, DateTimeOffset now, string? note)
    {
        VerificationNote = Ensure.OptionalMax(note, nameof(note), 2000);
        VerificationChangedBy = actor;
        VerificationChangedAt = now;

        Version++;
    }

    private void RequireNotRetracted()
    {
        if (Verification == SignalVerification.Retracted)
        {
            throw new DomainException(
                "That signal was retracted. A withdrawn claim is not edited back into use.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Signal), Id.ToString(), expectedVersion, Version);
        }
    }
}

/// <summary>
/// A source attached to a signal, and what it does for the claim.
/// </summary>
/// <remarks>
/// The excerpt is an analyst's selection, not a copy of the source. Whole document
/// and message bodies stay in M10 where their permissions live; duplicating them
/// here would create a second copy with weaker guards (ADR-0025, ADR-0030).
/// </remarks>
public sealed class SignalEvidence
{
    private SignalEvidence()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public SignalId SignalId { get; private set; }

    public IntelligenceSourceId SourceId { get; private set; }

    public SignalEvidenceRole Role { get; private set; }

    /// <summary>A short passage a person chose. Never the whole source.</summary>
    public string? Excerpt { get; private set; }

    /// <summary>Where in the source it was: a page, a paragraph, a timestamp.</summary>
    public string? Locator { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    internal static SignalEvidence Create(
        OrganizationId organizationId,
        SignalId signalId,
        IntelligenceSourceId sourceId,
        SignalEvidenceRole role,
        UserId addedBy,
        DateTimeOffset now,
        string? excerpt,
        string? locator) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            SignalId = signalId,
            SourceId = sourceId,
            Role = role,
            Excerpt = Ensure.OptionalMax(excerpt, nameof(excerpt), 2000),
            Locator = Ensure.OptionalMax(locator, nameof(locator), 200),
            AddedAt = now,
            AddedBy = addedBy,
        };
}
