using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Projects;

/// <summary>Opaque, immutable identifier for an <see cref="Attachment"/>.</summary>
public readonly record struct AttachmentId(Guid Value)
{
    public static AttachmentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Who is attached: a person or a company, never both and never neither.
/// </summary>
/// <remarks>
/// A company attached as producer is an ordinary fact, so the party cannot simply
/// be a person. Making it a choice rather than two nullable arguments means the
/// impossible combinations cannot be expressed by a caller at all; the database
/// enforces the same thing with a check constraint for anything that reaches it by
/// another route.
/// </remarks>
public readonly record struct AttachmentParty
{
    private AttachmentParty(PersonId? personId, CompanyId? companyId)
    {
        PersonId = personId;
        CompanyId = companyId;
    }

    public PersonId? PersonId { get; }

    public CompanyId? CompanyId { get; }

    /// <summary>Gets a value indicating whether a party has been chosen at all.</summary>
    public bool IsSpecified => PersonId is not null || CompanyId is not null;

    public static AttachmentParty Person(PersonId personId) => new(personId, null);

    public static AttachmentParty Company(CompanyId companyId) => new(null, companyId);

    public override string ToString() =>
        PersonId is { } person ? person.ToString() : CompanyId?.ToString() ?? "(unspecified)";
}

/// <summary>
/// How firm an attachment is.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is a claim about the world, not about the agency's intentions.
/// There is deliberately no <c>Targeted</c> state: "we want Ada for this" is
/// something the agency wants, not something that is true of the project, and
/// recording it as an attachment would make the project's roster a mix of fact and
/// hope that nothing downstream could separate. Wanting somebody lives as a
/// proposed package element, and becomes an opportunity in M6 (ADR-0019).
/// </para>
/// <para>
/// <see cref="InDiscussion"/> is included because it is reported fact - several
/// directors can be in discussions for one job, which is why it does not hold an
/// exclusive role.
/// </para>
/// </remarks>
public enum AttachmentStatus
{
    /// <summary>In talks. Real, reported, and not yet a commitment.</summary>
    InDiscussion = 1,

    /// <summary>Attached.</summary>
    Attached = 2,

    /// <summary>Attached subject to a condition, typically financing or a start date.</summary>
    Conditional = 3,

    /// <summary>Ran its course.</summary>
    Ended = 4,

    /// <summary>Pulled out.</summary>
    Withdrawn = 5,
}

/// <summary>A recorded change to an attachment's status.</summary>
/// <remarks>
/// Append-only, for the same reason representation events are: the row carries
/// only the current status, and without these "they were attached and then pulled
/// out in March" would be unrecoverable.
/// </remarks>
public sealed class AttachmentEvent
{
    private AttachmentEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AttachmentId AttachmentId { get; private set; }

    public AttachmentStatus? FromStatus { get; private set; }

    public AttachmentStatus ToStatus { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static AttachmentEvent Record(
        OrganizationId organizationId,
        AttachmentId attachmentId,
        AttachmentStatus? fromStatus,
        AttachmentStatus toStatus,
        DateOnly occurredOn,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new AttachmentEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            AttachmentId = attachmentId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            OccurredOn = occurredOn,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// A person's or company's commitment to a role on a project.
/// </summary>
/// <remarks>
/// <para>
/// Effective-dated and never deleted. An attachment that ended is history the
/// agency needs: who directed this before it fell apart is exactly the sort of
/// question a representation system is asked.
/// </para>
/// <para>
/// This is not a generic professional relationship. M2's relationships say who
/// works where; an attachment says who is doing what on one specific project, for
/// a period, in a named role.
/// </para>
/// </remarks>
public sealed class Attachment
{
    /// <summary>
    /// The complete transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Attached and Conditional move freely between each other: a condition being
    /// met or newly imposed is an ordinary event. Ended and Withdrawn are terminal
    /// - somebody coming back later is a new attachment, which preserves that
    /// there was a gap.
    /// </remarks>
    public static IReadOnlyDictionary<AttachmentStatus, IReadOnlySet<AttachmentStatus>> AllowedTransitions
    { get; } = new Dictionary<AttachmentStatus, IReadOnlySet<AttachmentStatus>>
    {
        [AttachmentStatus.InDiscussion] = Freeze(
            AttachmentStatus.Attached,
            AttachmentStatus.Conditional,
            AttachmentStatus.Ended,
            AttachmentStatus.Withdrawn),

        [AttachmentStatus.Attached] = Freeze(
            AttachmentStatus.Conditional,
            AttachmentStatus.Ended,
            AttachmentStatus.Withdrawn),

        [AttachmentStatus.Conditional] = Freeze(
            AttachmentStatus.Attached,
            AttachmentStatus.Ended,
            AttachmentStatus.Withdrawn),

        [AttachmentStatus.Ended] = Freeze(),
        [AttachmentStatus.Withdrawn] = Freeze(),
    };

    private readonly List<AttachmentEvent> _events = [];

    private Attachment()
    {
    }

    public AttachmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public ProjectRoleId ProjectRoleId { get; private set; }

    /// <summary>The attached person, when the party is a person.</summary>
    public PersonId? PersonId { get; private set; }

    /// <summary>The attached company, when the party is a company.</summary>
    public CompanyId? CompanyId { get; private set; }

    public AttachmentStatus Status { get; private set; }

    /// <summary>
    /// Whether the role held is exclusive, copied from the role.
    /// </summary>
    /// <remarks>
    /// Denormalized deliberately, and the only denormalization in M5. PostgreSQL
    /// forbids a subquery in an index predicate, so a partial unique index cannot
    /// consult <c>project_roles</c>; without this column the "one holder of an
    /// exclusive role" invariant could only be enforced by a trigger that locks the
    /// role row, or not at all in the database. The flag is kept in step by the
    /// project aggregate, which owns both sides and refuses to declare a role
    /// exclusive while more than one party already holds it.
    /// </remarks>
    public bool RoleIsExclusive { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    /// <summary>Where the information came from, when it is worth recording.</summary>
    public string? Source { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<AttachmentEvent> Events => _events;

    /// <summary>The party, reassembled from whichever column is set.</summary>
    public AttachmentParty Party =>
        PersonId is { } person
            ? AttachmentParty.Person(person)
            : CompanyId is { } company
                ? AttachmentParty.Company(company)
                : default;

    /// <summary>Gets a value indicating whether this attachment has not yet ended.</summary>
    public bool IsCurrent =>
        Status is not (AttachmentStatus.Ended or AttachmentStatus.Withdrawn);

    /// <summary>
    /// Gets a value indicating whether this attachment occupies its role.
    /// </summary>
    /// <remarks>
    /// Being in discussion does not. Several people can be talking about the same
    /// directing job, and treating that as occupancy would both block legitimate
    /// records and make a role look filled when nobody has committed.
    /// </remarks>
    public bool HoldsTheRole => HoldsRole(Status);

    /// <summary>Whether a status counts as occupying an exclusive role.</summary>
    public static bool HoldsRole(AttachmentStatus status) =>
        status is AttachmentStatus.Attached or AttachmentStatus.Conditional;

    internal static Attachment Create(
        OrganizationId organizationId,
        ProjectId projectId,
        ProjectRoleId projectRoleId,
        AttachmentParty party,
        AttachmentStatus status,
        bool roleIsExclusive,
        DateOnly startsOn,
        DateTimeOffset now,
        UserId createdBy,
        DateOnly? endsOn,
        string? source,
        string? notes)
    {
        if (!party.IsSpecified)
        {
            throw new DomainException(
                "An attachment must name either a person or a company.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new DomainException($"Unknown attachment status '{status}'.");
        }

        if (status is AttachmentStatus.Ended or AttachmentStatus.Withdrawn)
        {
            throw new DomainException(
                "An attachment cannot be created in a terminal state. Record how it began, "
                    + "then end it, so the history says what actually happened.");
        }

        RequireValidPeriod(startsOn, endsOn);

        Attachment attachment = new()
        {
            Id = AttachmentId.New(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            ProjectRoleId = projectRoleId,
            PersonId = party.PersonId,
            CompanyId = party.CompanyId,
            Status = status,
            RoleIsExclusive = roleIsExclusive,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Source = Ensure.OptionalMax(source, nameof(source), 400),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        attachment._events.Add(AttachmentEvent.Record(
            organizationId, attachment.Id, null, status, startsOn, now, createdBy, reason: null));

        return attachment;
    }

    /// <summary>
    /// Moves the attachment to a new status.
    /// </summary>
    /// <remarks>
    /// Already being at the target returns without complaint, so a retried command
    /// lands where the first attempt did.
    /// </remarks>
    public void ChangeStatus(
        AttachmentStatus target,
        DateOnly occurredOn,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"Unknown attachment status '{target}'.");
        }

        if (Status == target)
        {
            return;
        }

        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new DomainException(
                $"An attachment cannot move from {Status} to {target}. "
                    + $"From {Status} it can move to: {Describe(AllowedTransitions[Status])}.");
        }

        AttachmentStatus from = Status;

        Status = target;

        if (target is AttachmentStatus.Ended or AttachmentStatus.Withdrawn)
        {
            // Ending closes the period rather than deleting the row. An attachment
            // that happened stays having happened.
            if (occurredOn < StartsOn)
            {
                throw new DomainException(
                    $"An attachment cannot end on {occurredOn:yyyy-MM-dd}, before it started "
                        + $"on {StartsOn:yyyy-MM-dd}.");
            }

            EndsOn = occurredOn;
        }

        _events.Add(AttachmentEvent.Record(
            OrganizationId, Id, from, target, occurredOn, now, changedBy, reason));

        Touch(now);
    }

    /// <summary>Updates the descriptive fields without changing the commitment.</summary>
    public void Update(string? source, string? notes, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        Source = Ensure.OptionalMax(source, nameof(source), 400);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        Touch(now);
    }

    /// <summary>Keeps the copied exclusivity flag in step with the role.</summary>
    /// <remarks>
    /// Called by the project when a role's exclusivity changes. Not a user-visible
    /// mutation and not version-guarded: it is a consequence of a change the caller
    /// already made, on a record they did not edit.
    /// </remarks>
    internal void SetRoleExclusivity(bool isExclusive) => RoleIsExclusive = isExclusive;

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Attachment),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static void RequireValidPeriod(DateOnly startsOn, DateOnly? endsOn)
    {
        if (endsOn is { } end && end < startsOn)
        {
            throw new DomainException(
                $"An attachment cannot end on {end:yyyy-MM-dd}, before it starts on "
                    + $"{startsOn:yyyy-MM-dd}.");
        }
    }

    private static string Describe(IReadOnlySet<AttachmentStatus> statuses) =>
        statuses.Count == 0 ? "nothing, it is terminal" : string.Join(", ", statuses);

    private static IReadOnlySet<AttachmentStatus> Freeze(params AttachmentStatus[] statuses) =>
        new HashSet<AttachmentStatus>(statuses);
}
