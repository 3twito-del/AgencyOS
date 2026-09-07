using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Domain.Opportunities;

/// <summary>Opaque, immutable identifier for an <see cref="OpportunityPitch"/>.</summary>
public readonly record struct OpportunityPitchId(Guid Value)
{
    public static OpportunityPitchId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What kind of pitch it was.</summary>
public enum PitchKind
{
    /// <summary>A general introduction of the project or client.</summary>
    Introductory = 1,

    /// <summary>A full presentation.</summary>
    Formal = 2,

    /// <summary>A follow-up conversation on something already presented.</summary>
    FollowUp = 3,

    /// <summary>Raised in passing, in a meeting about something else.</summary>
    Incidental = 4,
}

/// <summary>
/// How a pitch left things.
/// </summary>
/// <remarks>
/// Deliberately restrained. There is no OfferReceived, Countered, Accepted or
/// DealClosed here: those describe economics, M6 has no way to record economics,
/// and an outcome the system cannot substantiate is worse than a narrower one that
/// it can. A pitch that genuinely produced an offer leaves the target
/// <c>Advanced</c> and waits for M7 (ADR-0020).
/// </remarks>
public enum PitchOutcome
{
    /// <summary>They took it away.</summary>
    NoDecision = 1,

    /// <summary>They want to talk again.</summary>
    FollowUpRequested = 2,

    /// <summary>They asked for more material.</summary>
    MoreMaterialRequested = 3,

    /// <summary>They said they are interested.</summary>
    Interested = 4,

    /// <summary>They said no.</summary>
    Passed = 5,
}

/// <summary>A material referenced in a pitch, as it stood at the time.</summary>
/// <remarks>
/// The same snapshot reasoning as a submission's materials: what was shown in the
/// room should not change because somebody retitled the file afterwards.
/// </remarks>
public sealed class PitchMaterial
{
    private PitchMaterial()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityPitchId PitchId { get; private set; }

    public MaterialId MaterialId { get; private set; }

    public string TitleAtPitch { get; private set; } = string.Empty;

    public MaterialType TypeAtPitch { get; private set; }

    public string? VersionLabelAtPitch { get; private set; }

    public int Position { get; private set; }

    internal static PitchMaterial Create(
        OrganizationId organizationId,
        OpportunityPitchId pitchId,
        MaterialId materialId,
        string titleAtPitch,
        MaterialType typeAtPitch,
        string? versionLabelAtPitch,
        int position)
    {
        return new PitchMaterial
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            PitchId = pitchId,
            MaterialId = materialId,
            TitleAtPitch = Ensure.NotBlankMax(titleAtPitch, nameof(titleAtPitch), 512),
            TypeAtPitch = typeAtPitch,
            VersionLabelAtPitch = Ensure.OptionalMax(
                versionLabelAtPitch, nameof(versionLabelAtPitch), 128),
            Position = position,
        };
    }
}

/// <summary>
/// The commercial meaning of one interaction.
/// </summary>
/// <remarks>
/// <para>
/// A pitch is not a second record of a meeting. One real-world event has one
/// <see cref="Interaction"/> identity - the same row the M2 timeline shows, with
/// the same participants and the same time - and this adds what the interaction
/// alone cannot say: which pursuit it belonged to, which target it was aimed at,
/// what was shown, and how it left things (ADR-0020).
/// </para>
/// <para>
/// One pitch per interaction, enforced by a unique index. Two pitches on one
/// meeting would mean the meeting happened twice.
/// </para>
/// </remarks>
public sealed class OpportunityPitch
{
    private readonly List<PitchMaterial> _materials = [];

    private OpportunityPitch()
    {
    }

    public OpportunityPitchId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    public OpportunityTargetId OpportunityTargetId { get; private set; }

    /// <summary>The interaction this pitch is the commercial reading of.</summary>
    public InteractionId InteractionId { get; private set; }

    public PitchKind Kind { get; private set; }

    public PitchOutcome Outcome { get; private set; }

    /// <summary>What was pitched, in a line.</summary>
    public string? Subject { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>When it happened. Mirrors the interaction, which remains authoritative.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<PitchMaterial> Materials => _materials;

    public static OpportunityPitch Record(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunityTargetId targetId,
        InteractionId interactionId,
        PitchKind kind,
        PitchOutcome outcome,
        DateTimeOffset occurredAt,
        UserId createdBy,
        DateTimeOffset now,
        string? subject = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown pitch kind '{kind}'.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException($"Unknown pitch outcome '{outcome}'.");
        }

        return new OpportunityPitch
        {
            Id = OpportunityPitchId.New(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            OpportunityTargetId = targetId,
            InteractionId = interactionId,
            Kind = kind,
            Outcome = outcome,
            Subject = Ensure.OptionalMax(subject, nameof(subject), 512),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            OccurredAt = occurredAt,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    /// <summary>Adds a material that was shown, as it stood at the time.</summary>
    public PitchMaterial AddMaterial(
        MaterialId materialId,
        string title,
        MaterialType type,
        string? versionLabel)
    {
        if (_materials.Any(x => x.MaterialId == materialId))
        {
            throw new DomainException("That material is already referenced in this pitch.");
        }

        PitchMaterial material = PitchMaterial.Create(
            OrganizationId, Id, materialId, title, type, versionLabel, _materials.Count);

        _materials.Add(material);

        return material;
    }

    /// <summary>
    /// Corrects what the pitch produced.
    /// </summary>
    /// <remarks>
    /// The outcome is the one field worth revising: agents record a pitch straight
    /// after the room and learn what it actually meant a day later. When and with
    /// whom belong to the interaction and are amended there.
    /// </remarks>
    public void Amend(
        PitchOutcome outcome,
        string? subject,
        string? notes,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException($"Unknown pitch outcome '{outcome}'.");
        }

        Outcome = outcome;
        Subject = Ensure.OptionalMax(subject, nameof(subject), 512);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(OpportunityPitch),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }
}

/// <summary>
/// The pursuit a task belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A separate table rather than two more nullable columns on <c>TaskItem</c>. That
/// row already carries a person, a company and a source interaction; adding an
/// opportunity and a target would make five, and the milestone after that would
/// make seven. An ever-widening nullable-identifier row is a shape that gets worse
/// with every use.
/// </para>
/// <para>
/// It does not replace <c>TaskItem.Subject</c>. "Who this task is about" and
/// "which pursuit it belongs to" are different facts: a follow-up after pitching
/// Northgate on Ada Reyes is about Ada and belongs to the opportunity and that
/// target. Collapsing them would lose one of the two (ADR-0020).
/// </para>
/// </remarks>
public sealed class OpportunityTaskLink
{
    private OpportunityTaskLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The task. A task belongs to at most one pursuit.</summary>
    public TaskItemId TaskItemId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    /// <summary>The target it concerns, when it concerns one in particular.</summary>
    public OpportunityTargetId? OpportunityTargetId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static OpportunityTaskLink Create(
        OrganizationId organizationId,
        TaskItemId taskItemId,
        OpportunityId opportunityId,
        OpportunityTargetId? targetId,
        DateTimeOffset now)
    {
        if (taskItemId.Value == Guid.Empty)
        {
            throw new DomainException("A task link must name a task.");
        }

        return new OpportunityTaskLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            TaskItemId = taskItemId,
            OpportunityId = opportunityId,
            OpportunityTargetId = targetId,
            LinkedAt = now,
        };
    }
}
