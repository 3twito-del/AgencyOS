using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Opportunities;

/// <summary>Opaque, immutable identifier for an <see cref="OpportunityTarget"/>.</summary>
public readonly record struct OpportunityTargetId(Guid Value)
{
    public static OpportunityTargetId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How far one target has come in the market.
/// </summary>
/// <remarks>
/// <para>
/// This is the pipeline, and it belongs to the target rather than the opportunity.
/// One pursuit routinely has a studio engaged, another passed and a third not yet
/// approached; a single stage across the whole pursuit would have to lie about at
/// least two of them (ADR-0020).
/// </para>
/// <para>
/// There is deliberately no <c>Submitted</c> stage. Whether material has gone out
/// is answered by the submission records themselves, and a stage saying the same
/// thing is a second source of truth that can disagree with them. Recording a
/// submission moves a contacted target to <see cref="Engaged"/>, and everything
/// else about submissions is derived.
/// </para>
/// </remarks>
public enum OpportunityTargetStage
{
    /// <summary>Named as somewhere to take this.</summary>
    Identified = 1,

    /// <summary>Cleared internally to approach.</summary>
    Approved = 2,

    /// <summary>Approached.</summary>
    Contacted = 3,

    /// <summary>Actively considering: material is with them, or a conversation is running.</summary>
    Engaged = 4,

    /// <summary>They have said they are interested.</summary>
    Interested = 5,

    /// <summary>
    /// Commercial discussion has moved past interest.
    /// </summary>
    /// <remarks>
    /// The boundary with M7 and nothing more. It says talks have become concrete
    /// without claiming an offer exists, because M6 has no way to describe one and
    /// inventing a placeholder would be worse than saying less (ADR-0020).
    /// </remarks>
    Advanced = 6,

    /// <summary>They said no.</summary>
    Passed = 7,

    /// <summary>The agency pulled out.</summary>
    Withdrawn = 8,

    /// <summary>Went cold and is not worth chasing further.</summary>
    Exhausted = 9,
}

/// <summary>What kind of thing a target event records.</summary>
public enum OpportunityTargetEventKind
{
    /// <summary>The target moved to a new stage.</summary>
    StageChanged = 1,

    /// <summary>They acknowledged receipt.</summary>
    Acknowledged = 2,

    /// <summary>They asked for more material.</summary>
    MoreMaterialRequested = 3,

    /// <summary>They asked to meet.</summary>
    MeetingRequested = 4,

    /// <summary>A note worth keeping that changed nothing.</summary>
    Noted = 5,
}

/// <summary>
/// Something that happened with a target.
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and the only place progression is recorded. The target row carries
/// the current stage; without these, a buyer who asked for more material, went
/// quiet and then passed would leave one row saying "Passed" and no account of how
/// it got there.
/// </para>
/// <para>
/// <see cref="SubmissionId"/> and <see cref="PitchId"/> record what caused the
/// event when something did. Nothing here is ever synthesized: silence from a
/// buyer produces no event at all, and "awaiting response" is derived from a
/// submission's date and the absence of anything after it.
/// </para>
/// </remarks>
public sealed class OpportunityTargetEvent
{
    private OpportunityTargetEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityTargetId OpportunityTargetId { get; private set; }

    public OpportunityTargetEventKind Kind { get; private set; }

    public OpportunityTargetStage? FromStage { get; private set; }

    public OpportunityTargetStage? ToStage { get; private set; }

    /// <summary>The submission this was a response to, when it was one.</summary>
    public SubmissionId? SubmissionId { get; private set; }

    /// <summary>The pitch this came out of, when it did.</summary>
    public OpportunityPitchId? PitchId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Note { get; private set; }

    internal static OpportunityTargetEvent Record(
        OrganizationId organizationId,
        OpportunityTargetId targetId,
        OpportunityTargetEventKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        OpportunityTargetStage? fromStage = null,
        OpportunityTargetStage? toStage = null,
        SubmissionId? submissionId = null,
        OpportunityPitchId? pitchId = null,
        string? note = null)
    {
        return new OpportunityTargetEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            OpportunityTargetId = targetId,
            Kind = kind,
            FromStage = fromStage,
            ToStage = toStage,
            SubmissionId = submissionId,
            PitchId = pitchId,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Note = Ensure.OptionalMax(note, nameof(note), 2000),
        };
    }
}

/// <summary>
/// One external party a pursuit is directed at.
/// </summary>
/// <remarks>
/// <para>
/// A company or a person, never both. A contact person is meaningful only against
/// a company - "the executive we deal with at Northgate" - and is refused against a
/// person target, where it would be either the same person or somebody with no
/// stated relationship to them.
/// </para>
/// <para>
/// Identity is not duplicated here. The name shown for a target comes from the
/// Person or Company row, so renaming a company renames it everywhere rather than
/// leaving stale copies scattered through the pipeline.
/// </para>
/// </remarks>
public sealed class OpportunityTarget
{
    /// <summary>Stages from which a target can still move.</summary>
    public static IReadOnlySet<OpportunityTargetStage> OpenStages { get; } =
        new HashSet<OpportunityTargetStage>
        {
            OpportunityTargetStage.Identified,
            OpportunityTargetStage.Approved,
            OpportunityTargetStage.Contacted,
            OpportunityTargetStage.Engaged,
            OpportunityTargetStage.Interested,
            OpportunityTargetStage.Advanced,
        };

    /// <summary>
    /// The complete transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Stated as data so the machine can be read at once and enumerated
    /// exhaustively. Engaged and Interested move both ways, because a buyer who
    /// said they were interested and then went back to reading is an ordinary
    /// thing rather than a contradiction.
    /// </remarks>
    public static IReadOnlyDictionary<OpportunityTargetStage, IReadOnlySet<OpportunityTargetStage>>
        AllowedTransitions
    { get; } = new Dictionary<OpportunityTargetStage, IReadOnlySet<OpportunityTargetStage>>
    {
        [OpportunityTargetStage.Identified] = Freeze(
            OpportunityTargetStage.Approved,
            OpportunityTargetStage.Withdrawn),

        [OpportunityTargetStage.Approved] = Freeze(
            OpportunityTargetStage.Contacted,
            OpportunityTargetStage.Withdrawn),

        [OpportunityTargetStage.Contacted] = Freeze(
            OpportunityTargetStage.Engaged,
            OpportunityTargetStage.Interested,
            OpportunityTargetStage.Passed,
            OpportunityTargetStage.Withdrawn,
            OpportunityTargetStage.Exhausted),

        [OpportunityTargetStage.Engaged] = Freeze(
            OpportunityTargetStage.Interested,
            OpportunityTargetStage.Passed,
            OpportunityTargetStage.Withdrawn,
            OpportunityTargetStage.Exhausted),

        [OpportunityTargetStage.Interested] = Freeze(
            OpportunityTargetStage.Advanced,
            OpportunityTargetStage.Engaged,
            OpportunityTargetStage.Passed,
            OpportunityTargetStage.Withdrawn),

        // Talks can still collapse after they have become concrete. What M6 must
        // not do is describe what was on the table - that is M7.
        [OpportunityTargetStage.Advanced] = Freeze(
            OpportunityTargetStage.Passed,
            OpportunityTargetStage.Withdrawn),

        [OpportunityTargetStage.Passed] = Freeze(),
        [OpportunityTargetStage.Withdrawn] = Freeze(),
        [OpportunityTargetStage.Exhausted] = Freeze(),
    };

    private readonly List<OpportunityTargetEvent> _events = [];

    private OpportunityTarget()
    {
    }

    public OpportunityTargetId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    /// <summary>The company being approached, when the target is a company.</summary>
    public CompanyId? CompanyId { get; private set; }

    /// <summary>The person being approached, when the target is a person.</summary>
    public PersonId? PersonId { get; private set; }

    /// <summary>The individual dealt with at a company target.</summary>
    public PersonId? ContactPersonId { get; private set; }

    public OpportunityTargetStage Stage { get; private set; }

    /// <summary>Who owns this target, when it differs from the opportunity's owner.</summary>
    public UserId? OwnerUserId { get; private set; }

    /// <summary>When somebody should next act on this target.</summary>
    public DateOnly? NextActionOn { get; private set; }

    public string? Notes { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<OpportunityTargetEvent> Events => _events;

    /// <summary>Gets a value indicating whether this target can still move.</summary>
    public bool IsOpen => OpenStages.Contains(Stage);

    /// <summary>Gets a value indicating whether the target is a company rather than a person.</summary>
    public bool IsCompanyTarget => CompanyId is not null;

    public static OpportunityTarget Create(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CompanyId? companyId,
        PersonId? personId,
        PersonId? contactPersonId,
        DateTimeOffset now,
        UserId createdBy,
        UserId? ownerUserId = null,
        DateOnly? nextActionOn = null,
        string? notes = null)
    {
        if (companyId is null == personId is null)
        {
            throw new DomainException(
                "A target is a company or a person, not both and not neither.");
        }

        if (contactPersonId is not null && companyId is null)
        {
            throw new DomainException(
                "A contact belongs to a company target. For a person target, the person is "
                    + "who you are dealing with.");
        }

        if (contactPersonId is not null && contactPersonId == personId)
        {
            throw new DomainException("A person cannot be their own contact.");
        }

        OpportunityTarget target = new()
        {
            Id = OpportunityTargetId.New(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            CompanyId = companyId,
            PersonId = personId,
            ContactPersonId = contactPersonId,
            Stage = OpportunityTargetStage.Identified,
            OwnerUserId = ownerUserId,
            NextActionOn = nextActionOn,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        target._events.Add(OpportunityTargetEvent.Record(
            organizationId,
            target.Id,
            OpportunityTargetEventKind.StageChanged,
            now,
            now,
            createdBy,
            toStage: OpportunityTargetStage.Identified));

        return target;
    }

    /// <summary>Updates the working fields without moving the target through the pipeline.</summary>
    public void Update(
        PersonId? contactPersonId,
        UserId? ownerUserId,
        DateOnly? nextActionOn,
        string? notes,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (contactPersonId is not null && !IsCompanyTarget)
        {
            throw new DomainException(
                "A contact belongs to a company target. For a person target, the person is "
                    + "who you are dealing with.");
        }

        ContactPersonId = contactPersonId;
        OwnerUserId = ownerUserId;
        NextActionOn = nextActionOn;
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        Touch(now);
    }

    /// <summary>
    /// Moves the target to a new stage.
    /// </summary>
    /// <remarks>
    /// Already being at the target stage returns without complaint, so a retried
    /// command lands where the first attempt did.
    /// </remarks>
    public void MoveTo(
        OpportunityTargetStage stage,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        SubmissionId? submissionId = null,
        OpportunityPitchId? pitchId = null,
        string? note = null)
    {
        RequireVersion(expectedVersion);

        MoveToInternal(stage, occurredAt, now, changedBy, submissionId, pitchId, note);
    }

    /// <summary>
    /// Records something a target did that did not move them along.
    /// </summary>
    /// <remarks>
    /// Acknowledging receipt or asking for more material is real and worth keeping,
    /// and neither means the buyer has decided anything.
    /// </remarks>
    public OpportunityTargetEvent RecordEvent(
        OpportunityTargetEventKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        UserId recordedBy,
        int expectedVersion,
        SubmissionId? submissionId = null,
        OpportunityPitchId? pitchId = null,
        string? note = null)
    {
        RequireVersion(expectedVersion);

        if (kind == OpportunityTargetEventKind.StageChanged)
        {
            throw new DomainException(
                "A stage change is recorded by moving the target, so the two cannot disagree.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown target event kind '{kind}'.");
        }

        RequireNotTerminal();

        OpportunityTargetEvent entry = OpportunityTargetEvent.Record(
            OrganizationId,
            Id,
            kind,
            occurredAt,
            now,
            recordedBy,
            submissionId: submissionId,
            pitchId: pitchId,
            note: note);

        _events.Add(entry);

        Touch(now);

        return entry;
    }

    /// <summary>
    /// Moves a contacted target along because material went to them.
    /// </summary>
    /// <remarks>
    /// Called when a submission is recorded. This is the only place the pipeline
    /// reacts to a submission, and it is a real transition with an event rather
    /// than a flag: whether anything was sent stays answerable from the submission
    /// rows alone.
    /// </remarks>
    public void NoteSubmission(
        SubmissionId submissionId,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        UserId recordedBy)
    {
        RequireNotTerminal();

        if (Stage is OpportunityTargetStage.Identified or OpportunityTargetStage.Approved)
        {
            throw new DomainException(
                $"This target is {Stage}. Record the approach before recording what was sent.");
        }

        if (Stage == OpportunityTargetStage.Contacted)
        {
            MoveToInternal(
                OpportunityTargetStage.Engaged, occurredAt, now, recordedBy, submissionId, null, null);

            return;
        }

        // Already engaged or further along: the submission is recorded, the stage
        // does not move backwards, and the event trail still shows it happened.
        _events.Add(OpportunityTargetEvent.Record(
            OrganizationId,
            Id,
            OpportunityTargetEventKind.Noted,
            occurredAt,
            now,
            recordedBy,
            submissionId: submissionId));

        Touch(now);
    }

    /// <summary>Records that a pitch happened, without inventing a stage move.</summary>
    public void NotePitch(
        OpportunityPitchId pitchId,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        UserId recordedBy)
    {
        RequireNotTerminal();

        if (Stage == OpportunityTargetStage.Identified)
        {
            throw new DomainException(
                "This target has not been approved yet. Approve it before recording a pitch.");
        }

        if (Stage is OpportunityTargetStage.Approved or OpportunityTargetStage.Contacted)
        {
            MoveToInternal(
                OpportunityTargetStage.Engaged, occurredAt, now, recordedBy, null, pitchId, null);

            return;
        }

        _events.Add(OpportunityTargetEvent.Record(
            OrganizationId,
            Id,
            OpportunityTargetEventKind.Noted,
            occurredAt,
            now,
            recordedBy,
            pitchId: pitchId));

        Touch(now);
    }

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(OpportunityTarget),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }

    /// <summary>Refuses activity against a target that has finished.</summary>
    public void RequireNotTerminal()
    {
        if (!IsOpen)
        {
            throw new DomainException(
                $"This target is {Stage} and is kept as it stood. "
                    + "Add a new target if the conversation has genuinely restarted.");
        }
    }

    private void MoveToInternal(
        OpportunityTargetStage stage,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        UserId changedBy,
        SubmissionId? submissionId,
        OpportunityPitchId? pitchId,
        string? note)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new DomainException($"Unknown target stage '{stage}'.");
        }

        if (Stage == stage)
        {
            return;
        }

        if (!AllowedTransitions[Stage].Contains(stage))
        {
            throw new DomainException(
                $"A target cannot move from {Stage} to {stage}. "
                    + $"From {Stage} it can move to: {Describe(AllowedTransitions[Stage])}.");
        }

        OpportunityTargetStage from = Stage;

        Stage = stage;

        if (!OpenStages.Contains(stage))
        {
            ClosedOn = DateOnly.FromDateTime(occurredAt.UtcDateTime);

            // A finished target has no next action. Leaving one would keep it in
            // every follow-up list forever.
            NextActionOn = null;
        }

        _events.Add(OpportunityTargetEvent.Record(
            OrganizationId,
            Id,
            OpportunityTargetEventKind.StageChanged,
            occurredAt,
            now,
            changedBy,
            fromStage: from,
            toStage: stage,
            submissionId: submissionId,
            pitchId: pitchId,
            note: note));

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string Describe(IReadOnlySet<OpportunityTargetStage> stages) =>
        stages.Count == 0 ? "nothing, it is finished" : string.Join(", ", stages);

    private static IReadOnlySet<OpportunityTargetStage> Freeze(
        params OpportunityTargetStage[] stages) => new HashSet<OpportunityTargetStage>(stages);
}
