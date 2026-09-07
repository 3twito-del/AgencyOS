using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Domain.Opportunities;

/// <summary>Opaque, immutable identifier for a <see cref="Submission"/>.</summary>
public readonly record struct SubmissionId(Guid Value)
{
    public static SubmissionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>How a submission reached its target.</summary>
/// <remarks>
/// Recorded as reported. AgencyOS did not transmit anything and does not know
/// whether it arrived; the channel says what the agent says they used.
/// </remarks>
public enum SubmissionChannel
{
    Email = 1,
    Portal = 2,
    Courier = 3,
    InPerson = 4,
    Phone = 5,
    Other = 99,
}

/// <summary>
/// A material as it stood when it was submitted.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is the point. Materials get retitled and redrafted, and without a
/// copy of what the fields said at the time, editing a material six months later
/// would silently rewrite the record of what an agent believed they sent. The
/// foreign key keeps the link; the snapshot keeps the history.
/// </para>
/// <para>
/// It is metadata only. AgencyOS holds no document and makes no claim about the
/// bytes that left the building - there is no hash here, because there is nothing
/// to hash until M10 owns document versions (ADR-0020).
/// </para>
/// </remarks>
public sealed class SubmissionMaterial
{
    private SubmissionMaterial()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public SubmissionId SubmissionId { get; private set; }

    public MaterialId MaterialId { get; private set; }

    /// <summary>The material's title when it was submitted.</summary>
    public string TitleAtSubmission { get; private set; } = string.Empty;

    /// <summary>Its type when it was submitted.</summary>
    public MaterialType TypeAtSubmission { get; private set; }

    /// <summary>Its version label when it was submitted, when it had one.</summary>
    public string? VersionLabelAtSubmission { get; private set; }

    /// <summary>Why this material was included.</summary>
    public string? Note { get; private set; }

    public int Position { get; private set; }

    internal static SubmissionMaterial Create(
        OrganizationId organizationId,
        SubmissionId submissionId,
        MaterialId materialId,
        string titleAtSubmission,
        MaterialType typeAtSubmission,
        string? versionLabelAtSubmission,
        int position,
        string? note)
    {
        return new SubmissionMaterial
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            SubmissionId = submissionId,
            MaterialId = materialId,
            TitleAtSubmission = Ensure.NotBlankMax(
                titleAtSubmission, nameof(titleAtSubmission), 512),
            TypeAtSubmission = typeAtSubmission,
            VersionLabelAtSubmission = Ensure.OptionalMax(
                versionLabelAtSubmission, nameof(versionLabelAtSubmission), 128),
            Position = position,
            Note = Ensure.OptionalMax(note, nameof(note), 1000),
        };
    }
}

/// <summary>
/// A record that material was presented to a target.
/// </summary>
/// <remarks>
/// <para>
/// AgencyOS records that a submission happened. It does not make submissions:
/// nothing here sent an email, uploaded to a portal or verified delivery, and the
/// commands are named <c>RecordSubmission</c> rather than <c>Send</c> so the
/// surface cannot imply otherwise. What is stored is the agent's assertion, which
/// is exactly what an agency's own file would contain (ADR-0020).
/// </para>
/// <para>
/// <see cref="ExternalReference"/> is a seam for M10, when a communications
/// integration can point at a real message. It carries whatever identifier the
/// agent has today - a portal reference, a message id they pasted - and AgencyOS
/// attaches no meaning to it.
/// </para>
/// </remarks>
public sealed class Submission
{
    private readonly List<SubmissionMaterial> _materials = [];

    private Submission()
    {
    }

    public SubmissionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    public OpportunityTargetId OpportunityTargetId { get; private set; }

    /// <summary>When the agent says it went.</summary>
    public DateTimeOffset SentAt { get; private set; }

    /// <summary>The colleague who sent it.</summary>
    public UserId SentByUserId { get; private set; }

    public SubmissionChannel Channel { get; private set; }

    /// <summary>What it was called: a subject line, a covering description.</summary>
    public string? Subject { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// When a response is expected, so silence becomes visible.
    /// </summary>
    /// <remarks>
    /// The only thing that makes "awaiting response" answerable. No response is not
    /// an event and is never recorded as one; it is derived from this date, the
    /// sent date and the absence of anything after it.
    /// </remarks>
    public DateOnly? ResponseExpectedBy { get; private set; }

    /// <summary>An external identifier the agent has, if any. AgencyOS assigns it no meaning.</summary>
    public string? ExternalReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<SubmissionMaterial> Materials => _materials;

    public static Submission Record(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunityTargetId targetId,
        DateTimeOffset sentAt,
        UserId sentByUserId,
        SubmissionChannel channel,
        UserId createdBy,
        DateTimeOffset now,
        string? subject = null,
        string? notes = null,
        DateOnly? responseExpectedBy = null,
        string? externalReference = null)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new DomainException($"Unknown submission channel '{channel}'.");
        }

        if (sentAt > now.AddDays(1))
        {
            throw new DomainException(
                "A submission cannot be recorded as sent in the future. Record it when it goes.");
        }

        if (responseExpectedBy is { } expected
            && expected < DateOnly.FromDateTime(sentAt.UtcDateTime))
        {
            throw new DomainException(
                $"A response cannot be expected on {expected:yyyy-MM-dd}, before the submission "
                    + $"went on {sentAt:yyyy-MM-dd}.");
        }

        return new Submission
        {
            Id = SubmissionId.New(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            OpportunityTargetId = targetId,
            SentAt = sentAt,
            SentByUserId = sentByUserId,
            Channel = channel,
            Subject = Ensure.OptionalMax(subject, nameof(subject), 512),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            ResponseExpectedBy = responseExpectedBy,
            ExternalReference = Ensure.OptionalMax(
                externalReference, nameof(externalReference), 512),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    /// <summary>
    /// Adds a material, capturing how it read at this moment.
    /// </summary>
    /// <remarks>
    /// The caller supplies the current values because the submission does not hold
    /// the material; taking them here rather than reading them later is what makes
    /// the snapshot a snapshot.
    /// </remarks>
    public SubmissionMaterial AddMaterial(
        MaterialId materialId,
        string title,
        MaterialType type,
        string? versionLabel,
        string? note = null)
    {
        if (_materials.Any(x => x.MaterialId == materialId))
        {
            throw new DomainException(
                "That material is already part of this submission.");
        }

        SubmissionMaterial material = SubmissionMaterial.Create(
            OrganizationId,
            Id,
            materialId,
            title,
            type,
            versionLabel,
            _materials.Count,
            note);

        _materials.Add(material);

        return material;
    }

    /// <summary>Corrects the descriptive fields. The snapshot and the sent time stand.</summary>
    /// <remarks>
    /// Deliberately narrow. A submission is a record of something that happened, so
    /// what can be edited afterwards is what the agent wrote about it - not when it
    /// went, not to whom, and not what was in it.
    /// </remarks>
    public void Amend(
        string? subject,
        string? notes,
        DateOnly? responseExpectedBy,
        string? externalReference,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (responseExpectedBy is { } expected
            && expected < DateOnly.FromDateTime(SentAt.UtcDateTime))
        {
            throw new DomainException(
                $"A response cannot be expected on {expected:yyyy-MM-dd}, before the submission "
                    + $"went on {SentAt:yyyy-MM-dd}.");
        }

        Subject = Ensure.OptionalMax(subject, nameof(subject), 512);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);
        ResponseExpectedBy = responseExpectedBy;
        ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 512);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Submission),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }
}
