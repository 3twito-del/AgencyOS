using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Domain.Projects;

/// <summary>Opaque, immutable identifier for a <see cref="Project"/>.</summary>
public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of work a project is.
/// </summary>
/// <remarks>
/// <para>
/// An int-backed enum rather than a lookup table, deliberately. The usual argument
/// for a catalog is that new mediums would otherwise force destructive migrations;
/// for an int-backed enum they do not. The column is already <c>integer</c>, so
/// adding a member is a code change with no DDL at all - which is how
/// <c>CompanyType</c>, <c>CreditType</c> and <c>MaterialType</c> have worked since
/// M2 (ADR-0018).
/// </para>
/// <para>
/// A catalog would buy per-tenant, edit-at-runtime vocabularies that nothing has
/// asked for, and cost a join on every read, the loss of compile-time exhaustiveness,
/// and saved views that break when somebody deletes a row.
/// </para>
/// </remarks>
public enum ProjectType
{
    FeatureFilm = 1,
    TelevisionSeries = 2,
    LimitedSeries = 3,
    Pilot = 4,
    ShortFilm = 5,
    Documentary = 6,
    Book = 7,
    Podcast = 8,
    Stage = 9,
    Digital = 10,
    BrandEntertainment = 11,
    Other = 99,
}

/// <summary>
/// Where a project stands as a record the agency is working.
/// </summary>
/// <remarks>
/// Operational, not creative. This says whether anybody is working the project and
/// how it finished; <see cref="DevelopmentStage"/> says where the work itself has
/// got to. Merging the two would make "cancelled" and "in pre-production" mutually
/// exclusive, when the useful fact is usually that a project was cancelled
/// <em>during</em> pre-production (ADR-0018).
/// </remarks>
public enum ProjectStatus
{
    /// <summary>Being worked.</summary>
    Active = 1,

    /// <summary>Not being worked, but not finished either. Dormant rather than dead.</summary>
    Inactive = 2,

    /// <summary>Finished. A sequel or a new season is a new project.</summary>
    Completed = 3,

    /// <summary>Stopped deliberately. Revival happens, so this is not terminal.</summary>
    Cancelled = 4,

    /// <summary>Filed away. Terminal.</summary>
    Archived = 5,
}

/// <summary>
/// How far the work itself has progressed.
/// </summary>
/// <remarks>
/// Ordered, but movement is not one-way. Projects genuinely fall out of
/// pre-production back into development when financing or a cast attachment
/// collapses, and a model that forbade it would only teach people to lie to the
/// system. What is constrained instead is that the stage stops moving once the
/// status says the project has stopped (ADR-0018).
/// </remarks>
public enum DevelopmentStage
{
    Concept = 1,
    Development = 2,
    Packaging = 3,
    PreProduction = 4,
    Production = 5,
    PostProduction = 6,
    Released = 7,
}

/// <summary>What kind of change a project event records.</summary>
public enum ProjectChangeKind
{
    Created = 1,
    StatusChanged = 2,
    StageChanged = 3,
}

/// <summary>
/// A recorded change to a project's status or stage.
/// </summary>
/// <remarks>
/// Append-only. The project row carries only the current values, so without these
/// a project that went to production and back to development would leave no trace
/// of ever having been in production (ADR-0012 keeps this distinct from the audit
/// trail, which answers a security question rather than a domain one).
/// </remarks>
public sealed class ProjectEvent
{
    private ProjectEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public ProjectChangeKind Kind { get; private set; }

    /// <summary>Status before the change, when this event records one.</summary>
    public ProjectStatus? FromStatus { get; private set; }

    public ProjectStatus? ToStatus { get; private set; }

    /// <summary>Stage before the change, when this event records one.</summary>
    public DevelopmentStage? FromStage { get; private set; }

    public DevelopmentStage? ToStage { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static ProjectEvent Record(
        OrganizationId organizationId,
        ProjectId projectId,
        ProjectChangeKind kind,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason,
        ProjectStatus? fromStatus = null,
        ProjectStatus? toStatus = null,
        DevelopmentStage? fromStage = null,
        DevelopmentStage? toStage = null)
    {
        return new ProjectEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            FromStage = fromStage,
            ToStage = toStage,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// A canonical entertainment project the agency is developing, packaging or tracking.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from a <c>Credit</c>, which records what somebody was credited on, and
/// from a <c>Material</c>, which is an artifact. A project can have credits and
/// materials attached to it, and exists whether or not it has either.
/// </para>
/// <para>
/// The project owns its roles, company participation, source-property links and
/// material links. Attachments are owned too, because the exclusive-role invariant
/// is a statement about a project's roster and can only be checked by something
/// that can see the whole roster.
/// </para>
/// </remarks>
public sealed class Project
{
    /// <summary>
    /// Statuses in which the creative stage no longer moves.
    /// </summary>
    /// <remarks>
    /// A cancelled project did not carry on developing after it was cancelled, and
    /// letting somebody advance its stage would erase how far it actually got -
    /// which is usually the most useful thing the record still says.
    /// </remarks>
    public static IReadOnlySet<ProjectStatus> StageFrozenStatuses { get; } =
        new HashSet<ProjectStatus>
        {
            ProjectStatus.Completed,
            ProjectStatus.Cancelled,
            ProjectStatus.Archived,
        };

    /// <summary>
    /// The complete status transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated as data rather than scattered through methods, so the machine can be
    /// read at once and enumerated exhaustively by a test.
    /// </para>
    /// <para>
    /// <see cref="ProjectStatus.Cancelled"/> can return to <see cref="ProjectStatus.Active"/>
    /// because a cancelled project coming back is an ordinary industry event.
    /// <see cref="ProjectStatus.Completed"/> cannot: a finished film does not
    /// un-finish, and the thing that follows it is a different project.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<ProjectStatus, IReadOnlySet<ProjectStatus>> AllowedStatusTransitions
    { get; } = new Dictionary<ProjectStatus, IReadOnlySet<ProjectStatus>>
    {
        [ProjectStatus.Active] = Freeze(
            ProjectStatus.Inactive,
            ProjectStatus.Completed,
            ProjectStatus.Cancelled,
            ProjectStatus.Archived),

        [ProjectStatus.Inactive] = Freeze(
            ProjectStatus.Active,
            ProjectStatus.Completed,
            ProjectStatus.Cancelled,
            ProjectStatus.Archived),

        [ProjectStatus.Completed] = Freeze(ProjectStatus.Archived),

        [ProjectStatus.Cancelled] = Freeze(ProjectStatus.Active, ProjectStatus.Archived),

        // Terminal. Filing something away is the last operational act on it.
        [ProjectStatus.Archived] = Freeze(),
    };

    private readonly List<ProjectEvent> _events = [];
    private readonly List<ProjectRole> _roles = [];
    private readonly List<Attachment> _attachments = [];
    private readonly List<ProjectCompanyParticipation> _companyParticipations = [];
    private readonly List<ProjectSourceProperty> _sourceProperties = [];
    private readonly List<ProjectMaterialLink> _materials = [];

    private Project()
    {
    }

    public ProjectId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>An internal or provisional title, when it differs from the one in use.</summary>
    public string? WorkingTitle { get; private set; }

    public ProjectType Type { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DevelopmentStage Stage { get; private set; }

    public string? Logline { get; private set; }

    public string? Synopsis { get; private set; }

    /// <summary>
    /// The company most associated with the project, when there is an obvious one.
    /// </summary>
    /// <remarks>
    /// A convenience for display, not the project's company structure. Studios,
    /// networks, financiers and distributors are recorded as
    /// <see cref="ProjectCompanyParticipation"/>, because a project routinely has
    /// several and this field can only hold one.
    /// </remarks>
    public CompanyId? PrimaryCompanyId { get; private set; }

    /// <summary>Year the project is dated to, when meaningful.</summary>
    public int? Year { get; private set; }

    /// <summary>The internal owner of the project, when one is assigned.</summary>
    public UserId? LeadUserId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ProjectEvent> Events => _events;

    public IReadOnlyCollection<ProjectRole> Roles => _roles;

    public IReadOnlyCollection<Attachment> Attachments => _attachments;

    public IReadOnlyCollection<ProjectCompanyParticipation> CompanyParticipations => _companyParticipations;

    public IReadOnlyCollection<ProjectSourceProperty> SourceProperties => _sourceProperties;

    public IReadOnlyCollection<ProjectMaterialLink> Materials => _materials;

    /// <summary>Gets a value indicating whether the creative stage can still move.</summary>
    public bool StageIsFrozen => StageFrozenStatuses.Contains(Status);

    /// <summary>Roles still to be filled.</summary>
    public IEnumerable<ProjectRole> OpenRoles =>
        _roles.Where(x => x.Status == ProjectRoleStatus.Open);

    /// <summary>Attachments that currently hold.</summary>
    public IEnumerable<Attachment> CurrentAttachments => _attachments.Where(x => x.IsCurrent);

    /// <summary>Company participation that currently holds.</summary>
    public IEnumerable<ProjectCompanyParticipation> CurrentParticipations =>
        _companyParticipations.Where(x => x.IsOpen);

    public static Project Create(
        OrganizationId organizationId,
        string title,
        ProjectType type,
        UserId createdBy,
        DateTimeOffset now,
        string? workingTitle = null,
        DevelopmentStage stage = DevelopmentStage.Concept,
        string? logline = null,
        string? synopsis = null,
        CompanyId? primaryCompanyId = null,
        int? year = null,
        UserId? leadUserId = null,
        string? notes = null)
    {
        RequireKnownType(type);
        RequireKnownStage(stage);

        Project project = new()
        {
            Id = ProjectId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 400),
            WorkingTitle = Ensure.OptionalMax(workingTitle, nameof(workingTitle), 400),
            Type = type,
            Status = ProjectStatus.Active,
            Stage = stage,
            Logline = Ensure.OptionalMax(logline, nameof(logline), 1000),
            Synopsis = Ensure.OptionalMax(synopsis, nameof(synopsis), 8000),
            PrimaryCompanyId = primaryCompanyId,
            Year = ValidYear(year),
            LeadUserId = leadUserId,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        project._events.Add(ProjectEvent.Record(
            organizationId,
            project.Id,
            ProjectChangeKind.Created,
            now,
            createdBy,
            reason: null,
            toStatus: ProjectStatus.Active,
            toStage: stage));

        return project;
    }

    /// <summary>Updates the descriptive fields. Status and stage change by their own commands.</summary>
    public void Update(
        string title,
        ProjectType type,
        DateTimeOffset now,
        int expectedVersion,
        string? workingTitle = null,
        string? logline = null,
        string? synopsis = null,
        CompanyId? primaryCompanyId = null,
        int? year = null,
        UserId? leadUserId = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);
        RequireKnownType(type);

        Title = Ensure.NotBlankMax(title, nameof(title), 400);
        WorkingTitle = Ensure.OptionalMax(workingTitle, nameof(workingTitle), 400);
        Type = type;
        Logline = Ensure.OptionalMax(logline, nameof(logline), 1000);
        Synopsis = Ensure.OptionalMax(synopsis, nameof(synopsis), 8000);
        PrimaryCompanyId = primaryCompanyId;
        Year = ValidYear(year);
        LeadUserId = leadUserId;
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        Touch(now);
    }

    /// <summary>
    /// Moves the project to a new operational status.
    /// </summary>
    /// <remarks>
    /// Already being at the target is not an error. A retried command must land on
    /// the same state as the first attempt rather than failing the second time,
    /// which is what makes the command safe to repeat (ADR-0014).
    /// </remarks>
    public void ChangeStatus(
        ProjectStatus target,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"Unknown project status '{target}'.");
        }

        if (Status == target)
        {
            return;
        }

        if (!AllowedStatusTransitions[Status].Contains(target))
        {
            throw new DomainException(
                $"A project cannot move from {Status} to {target}. "
                    + $"From {Status} it can move to: "
                    + $"{Describe(AllowedStatusTransitions[Status])}.");
        }

        ProjectStatus from = Status;

        Status = target;

        _events.Add(ProjectEvent.Record(
            OrganizationId,
            Id,
            ProjectChangeKind.StatusChanged,
            now,
            changedBy,
            reason,
            fromStatus: from,
            toStatus: target));

        Touch(now);
    }

    /// <summary>
    /// Moves the project to a different development stage.
    /// </summary>
    /// <remarks>
    /// Any stage may follow any other while the project is still live, because
    /// regression is a real event rather than a data-entry mistake. The rule that
    /// does bite is that a finished, cancelled or archived project's stage no
    /// longer moves.
    /// </remarks>
    public void ChangeStage(
        DevelopmentStage target,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        string? reason = null)
    {
        RequireVersion(expectedVersion);
        RequireKnownStage(target);

        if (Stage == target)
        {
            return;
        }

        if (StageIsFrozen)
        {
            throw new DomainException(
                $"A {Status} project's development stage cannot change. It stopped at {Stage}, "
                    + "and that is worth keeping. Reactivate the project first if work has resumed.");
        }

        DevelopmentStage from = Stage;

        Stage = target;

        _events.Add(ProjectEvent.Record(
            OrganizationId,
            Id,
            ProjectChangeKind.StageChanged,
            now,
            changedBy,
            reason,
            fromStage: from,
            toStage: target));

        Touch(now);
    }

    // ------------------------------------------------------------------ roles

    public ProjectRole AddRole(
        ProjectRoleType type,
        DateTimeOffset now,
        int expectedVersion,
        string? label = null,
        bool isExclusive = false,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        ProjectRole role = ProjectRole.Create(OrganizationId, Id, type, now, label, isExclusive, notes);

        _roles.Add(role);

        Touch(now);

        return role;
    }

    public ProjectRole RequireRole(ProjectRoleId roleId)
    {
        return _roles.FirstOrDefault(x => x.Id == roleId)
            ?? throw new DomainException("That role does not belong to this project.");
    }

    /// <summary>
    /// Updates a role, keeping its attachments' copy of the exclusivity flag true.
    /// </summary>
    /// <remarks>
    /// Declaring a role exclusive while two parties already hold it is refused here
    /// rather than left to the database. The index would reject it too, but as an
    /// opaque constraint violation; this says what is actually wrong.
    /// </remarks>
    public void UpdateRole(
        ProjectRoleId roleId,
        string? label,
        bool isExclusive,
        string? notes,
        DateTimeOffset now)
    {
        ProjectRole role = RequireRole(roleId);

        if (isExclusive && !role.IsExclusive)
        {
            int holders = _attachments.Count(x => x.ProjectRoleId == roleId && x.HoldsTheRole);

            if (holders > 1)
            {
                throw new DomainException(
                    $"{role.Describe()} cannot be made exclusive: {holders} parties currently hold it. "
                        + "End all but one attachment first.");
            }
        }

        role.Update(label, isExclusive, notes, now);

        foreach (Attachment attachment in _attachments.Where(x => x.ProjectRoleId == roleId))
        {
            attachment.SetRoleExclusivity(isExclusive);
        }

        Touch(now);
    }

    // ------------------------------------------------------------ attachments

    /// <summary>
    /// Records that somebody or some company is attached to a role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exclusive-role invariant is checked here because it is a statement about
    /// the project's whole roster: whether a second person can direct depends on
    /// every other attachment to that role. A partial unique index enforces the
    /// same thing in the database, so two concurrent transactions cannot both
    /// believe they were first.
    /// </para>
    /// <para>
    /// Only real-world attachment belongs here. Wanting somebody for a role is the
    /// agency's intention, and lives as a proposed package element (ADR-0019).
    /// </para>
    /// </remarks>
    public Attachment Attach(
        ProjectRoleId roleId,
        AttachmentParty party,
        AttachmentStatus status,
        DateOnly startsOn,
        DateTimeOffset now,
        UserId createdBy,
        int expectedVersion,
        DateOnly? endsOn = null,
        string? source = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        ProjectRole role = RequireRole(roleId);

        if (role.Status == ProjectRoleStatus.Closed)
        {
            throw new DomainException(
                "That role is closed. Reopen it before attaching somebody to it.");
        }

        if (role.IsExclusive && Attachment.HoldsRole(status))
        {
            Attachment? incumbent = _attachments.FirstOrDefault(
                x => x.ProjectRoleId == roleId && x.HoldsTheRole);

            if (incumbent is not null && !incumbent.Party.Equals(party))
            {
                throw new DomainException(
                    $"{role.Describe()} is an exclusive role and is already held. "
                        + "End the existing attachment before recording another.");
            }
        }

        Attachment attachment = Attachment.Create(
            OrganizationId,
            Id,
            roleId,
            party,
            status,
            role.IsExclusive,
            startsOn,
            now,
            createdBy,
            endsOn,
            source,
            notes);

        _attachments.Add(attachment);

        // Occupancy is recomputed from the attachments that now exist, never
        // asserted. Marking the role filled here would make a role look taken the
        // moment somebody was merely in discussions for it - and several people can
        // legitimately be in talks for one job at once.
        SetOccupancy(role, now);

        Touch(now);

        return attachment;
    }

    public Attachment RequireAttachment(AttachmentId attachmentId)
    {
        return _attachments.FirstOrDefault(x => x.Id == attachmentId)
            ?? throw new DomainException("That attachment does not belong to this project.");
    }

    /// <summary>Recomputes whether a role still has somebody holding it.</summary>
    /// <remarks>
    /// Called after an attachment ends. A role whose only attachment just ended is
    /// open again, and leaving it marked Filled would make every "missing director"
    /// view wrong.
    /// </remarks>
    public void RefreshRoleOccupancy(ProjectRoleId roleId, DateTimeOffset now)
    {
        ProjectRole role = RequireRole(roleId);

        SetOccupancy(role, now);

        Touch(now);
    }

    /// <summary>Sets a role's occupancy from the attachments that actually hold it.</summary>
    private void SetOccupancy(ProjectRole role, DateTimeOffset now)
    {
        bool held = _attachments.Any(x => x.ProjectRoleId == role.Id && x.HoldsTheRole);

        role.SetOccupied(held, now);
    }

    // --------------------------------------------------------- participations

    public ProjectCompanyParticipation AddParticipation(
        CompanyId companyId,
        ProjectCompanyCapacity capacity,
        DateOnly startsOn,
        DateTimeOffset now,
        int expectedVersion,
        DateOnly? endsOn = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        if (_companyParticipations.Any(x => x.IsOpen && x.CompanyId == companyId && x.Capacity == capacity))
        {
            throw new DomainException(
                $"That company is already recorded as {capacity} on this project.");
        }

        ProjectCompanyParticipation participation = ProjectCompanyParticipation.Create(
            OrganizationId, Id, companyId, capacity, startsOn, now, endsOn, notes);

        _companyParticipations.Add(participation);

        Touch(now);

        return participation;
    }

    public void EndParticipation(Guid participationId, DateOnly endsOn, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ProjectCompanyParticipation participation =
            _companyParticipations.FirstOrDefault(x => x.Id == participationId)
                ?? throw new DomainException("That company participation does not belong to this project.");

        participation.End(endsOn);

        Touch(now);
    }

    // ------------------------------------------------------- source and links

    public void LinkSourceProperty(
        SourcePropertyId sourcePropertyId,
        DateTimeOffset now,
        UserId linkedBy,
        int expectedVersion,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        if (_sourceProperties.Any(x => x.SourcePropertyId == sourcePropertyId))
        {
            return;
        }

        _sourceProperties.Add(ProjectSourceProperty.Create(
            OrganizationId, Id, sourcePropertyId, now, linkedBy, notes));

        Touch(now);
    }

    public void UnlinkSourceProperty(SourcePropertyId sourcePropertyId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ProjectSourceProperty? link =
            _sourceProperties.FirstOrDefault(x => x.SourcePropertyId == sourcePropertyId);

        if (link is null)
        {
            return;
        }

        _sourceProperties.Remove(link);

        Touch(now);
    }

    public void LinkMaterial(
        MaterialId materialId,
        DateTimeOffset now,
        UserId linkedBy,
        int expectedVersion,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        if (_materials.Any(x => x.MaterialId == materialId))
        {
            return;
        }

        _materials.Add(ProjectMaterialLink.Create(OrganizationId, Id, materialId, now, linkedBy, notes));

        Touch(now);
    }

    public void UnlinkMaterial(MaterialId materialId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ProjectMaterialLink? link = _materials.FirstOrDefault(x => x.MaterialId == materialId);

        if (link is null)
        {
            return;
        }

        _materials.Remove(link);

        Touch(now);
    }

    // --------------------------------------------------------------- plumbing

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Project),
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

    private static int? ValidYear(int? year)
    {
        if (year is null)
        {
            return null;
        }

        if (year is < 1850 or > 2200)
        {
            throw new DomainException(
                $"A project year of {year} is outside the range this system accepts (1850-2200).");
        }

        return year;
    }

    private static void RequireKnownType(ProjectType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Unknown project type '{type}'.");
        }
    }

    private static void RequireKnownStage(DevelopmentStage stage)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new DomainException($"Unknown development stage '{stage}'.");
        }
    }

    private static string Describe(IReadOnlySet<ProjectStatus> statuses) =>
        statuses.Count == 0 ? "nothing, it is terminal" : string.Join(", ", statuses);

    private static IReadOnlySet<ProjectStatus> Freeze(params ProjectStatus[] statuses) =>
        new HashSet<ProjectStatus>(statuses);
}
