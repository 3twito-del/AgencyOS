using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Projects;

/// <summary>Opaque, immutable identifier for a <see cref="ProjectRole"/>.</summary>
public readonly record struct ProjectRoleId(Guid Value)
{
    public static ProjectRoleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The kind of position a project role describes.
/// </summary>
/// <remarks>
/// One vocabulary, one table. A table per profession would multiply the schema by
/// the number of jobs in the industry and make "which roles on this project are
/// unfilled" a union across all of them.
/// </remarks>
public enum ProjectRoleType
{
    Writer = 1,
    Director = 2,
    Actor = 3,
    Producer = 4,
    ExecutiveProducer = 5,
    Showrunner = 6,
    Creator = 7,
    Composer = 8,
    Cinematographer = 9,
    Host = 10,
    Other = 99,
}

/// <summary>Whether a role still needs somebody.</summary>
/// <remarks>
/// Occupancy is derived from attachments rather than set by hand:
/// <see cref="Filled"/> means something currently holds the role, and it reverts
/// to <see cref="Open"/> when the last attachment ends. Letting a user set it
/// directly would produce roles marked filled with nobody attached, which is
/// exactly what the "missing director" views must never show.
/// </remarks>
public enum ProjectRoleStatus
{
    Open = 1,
    Filled = 2,

    /// <summary>Deliberately parked. Not being cast, but not abandoned.</summary>
    OnHold = 3,

    /// <summary>No longer part of the project.</summary>
    Closed = 4,
}

/// <summary>
/// A position within a project, which may or may not be occupied.
/// </summary>
/// <remarks>
/// <para>
/// A role is not the person in it, and that separation is structural rather than
/// conventional: the occupant arrives only through an <see cref="Attachment"/>. It
/// is what lets a project say "we need a director" - a sentence with no person in
/// it at all - and what lets the same role record survive one director leaving and
/// another arriving.
/// </para>
/// <para>
/// <see cref="IsExclusive"/> is opt-in. Claiming exclusivity wrongly blocks
/// legitimate data entry; omitting it wrongly only fails to catch a duplicate. A
/// convenience constraint should fail open.
/// </para>
/// </remarks>
public sealed class ProjectRole
{
    private ProjectRole()
    {
    }

    public ProjectRoleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public ProjectRoleType Type { get; private set; }

    /// <summary>
    /// A character name, or a description of the position.
    /// </summary>
    /// <remarks>
    /// "Detective Sarah Okonjo" for a performance; "Second unit" for a directing
    /// role. Optional, because "Director" often needs nothing more said.
    /// </remarks>
    public string? Label { get; private set; }

    public ProjectRoleStatus Status { get; private set; }

    /// <summary>Whether only one party may hold this role at a time.</summary>
    public bool IsExclusive { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets a value indicating whether this role still needs somebody.</summary>
    public bool IsOpen => Status == ProjectRoleStatus.Open;

    internal static ProjectRole Create(
        OrganizationId organizationId,
        ProjectId projectId,
        ProjectRoleType type,
        DateTimeOffset now,
        string? label,
        bool isExclusive,
        string? notes)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Unknown project role type '{type}'.");
        }

        return new ProjectRole
        {
            Id = ProjectRoleId.New(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Type = type,
            Label = Ensure.OptionalMax(label, nameof(label), 200),
            Status = ProjectRoleStatus.Open,
            IsExclusive = isExclusive,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Updates the descriptive fields. Occupancy is not settable by hand.</summary>
    public void Update(string? label, bool isExclusive, string? notes, DateTimeOffset now)
    {
        Label = Ensure.OptionalMax(label, nameof(label), 200);
        IsExclusive = isExclusive;
        Notes = Ensure.OptionalMax(notes, nameof(notes), 2000);
        UpdatedAt = now;
    }

    /// <summary>Parks a role without removing it.</summary>
    public void Hold(DateTimeOffset now)
    {
        if (Status == ProjectRoleStatus.Closed)
        {
            throw new DomainException("A closed role cannot be put on hold. Reopen it first.");
        }

        Status = ProjectRoleStatus.OnHold;
        UpdatedAt = now;
    }

    /// <summary>Closes a role that is no longer part of the project.</summary>
    public void Close(DateTimeOffset now)
    {
        Status = ProjectRoleStatus.Closed;
        UpdatedAt = now;
    }

    /// <summary>Reopens a closed or parked role.</summary>
    public void Reopen(DateTimeOffset now)
    {
        Status = ProjectRoleStatus.Open;
        UpdatedAt = now;
    }

    /// <summary>Sets occupancy from the attachments that actually exist.</summary>
    internal void SetOccupied(bool occupied, DateTimeOffset now)
    {
        // A closed role stays closed. Somebody closed it deliberately, and an
        // attachment ending is not a reason to reopen it.
        if (Status == ProjectRoleStatus.Closed)
        {
            return;
        }

        ProjectRoleStatus target = occupied ? ProjectRoleStatus.Filled : ProjectRoleStatus.Open;

        if (Status == target)
        {
            return;
        }

        Status = target;
        UpdatedAt = now;
    }

    /// <summary>A human label for error messages.</summary>
    public string Describe() => Label is null ? Type.ToString() : $"{Type} ({Label})";
}
