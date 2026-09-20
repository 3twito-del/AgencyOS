using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;

namespace AgencyOS.Domain.Tasks;

/// <summary>Opaque, immutable identifier for a <see cref="TaskItem"/>.</summary>
public readonly record struct TaskItemId(Guid Value)
{
    public static TaskItemId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Lifecycle state of a task.</summary>
public enum TaskState
{
    Open = 1,
    Completed = 2,
}

/// <summary>How urgently a task wants attention.</summary>
public enum TaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>
/// Something that needs doing, usually the next move after an interaction.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>TaskItem</c> rather than <c>Task</c> so it does not collide with
/// <see cref="System.Threading.Tasks.Task"/> in every file that touches it. The
/// table is <c>tasks</c> and the contract calls it a task.
/// </para>
/// <para>
/// Completion is a state machine, not a flag: an open task has no completion
/// instant and a completed task always has one. Both halves are enforced here and
/// by a database check constraint, because a task that claims to be done without
/// saying when is a record nobody can trust.
/// </para>
/// </remarks>
public sealed class TaskItem
{
    private TaskItem()
    {
    }

    public TaskItemId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public TaskState State { get; private set; }

    public TaskPriority Priority { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>Person the task concerns, when it concerns one.</summary>
    public PersonId? RelatedPersonId { get; private set; }

    /// <summary>Company the task concerns, when it concerns one.</summary>
    public CompanyId? RelatedCompanyId { get; private set; }

    /// <summary>The interaction this task came out of, when it came out of one.</summary>
    public InteractionId? SourceInteractionId { get; private set; }

    public UserId? AssignedTo { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public UserId? CompletedBy { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, incremented on every mutation.
    /// </summary>
    /// <remarks>
    /// An explicit column rather than PostgreSQL's <c>xmin</c>: a concurrency
    /// token is part of the client contract and must outlive the storage engine.
    /// See <c>docs/adr/ADR-0014-concurrency-and-idempotency.md</c>.
    /// </remarks>
    public int Version { get; private set; }

    /// <summary>
    /// Fails unless the caller observed the current version.
    /// </summary>
    /// <remarks>
    /// This is what turns a blind overwrite into a detected conflict. A client
    /// that has been offline sends the version it last saw; if the record moved
    /// on, the write is refused rather than silently applied.
    /// </remarks>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(GetType().Name, Id.ToString(), expectedVersion, Version);
        }
    }

    /// <summary>The party this task concerns, when it concerns one.</summary>
    public RelationshipEndpoint? Subject
    {
        get
        {
            if (RelatedPersonId.HasValue)
            {
                return RelationshipEndpoint.ForPerson(RelatedPersonId.Value);
            }

            return RelatedCompanyId.HasValue
                ? RelationshipEndpoint.ForCompany(RelatedCompanyId.Value)
                : null;
        }
    }

    /// <summary>Determines whether the task is open and past its due date.</summary>
    public bool IsOverdueAt(DateTimeOffset instant) =>
        State == TaskState.Open && DueAt is { } due && due < instant;

    public static TaskItem Create(
        OrganizationId organizationId,
        string title,
        UserId createdBy,
        DateTimeOffset now,
        TaskPriority priority = TaskPriority.Normal,
        DateTimeOffset? dueAt = null,
        RelationshipEndpoint? subject = null,
        InteractionId? sourceInteractionId = null,
        UserId? assignedTo = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"Unknown task priority '{priority}'.");
        }

        return new TaskItem
        {
            Id = TaskItemId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 512),
            State = TaskState.Open,
            Priority = priority,
            DueAt = dueAt,
            RelatedPersonId = subject?.AsPerson,
            RelatedCompanyId = subject?.AsCompany,
            SourceInteractionId = sourceInteractionId,
            AssignedTo = assignedTo,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            CompletedAt = null,
            CompletedBy = null,
            Version = 1,
        };
    }

    /// <summary>
    /// Makes somebody accountable for the task, or nobody.
    /// </summary>
    /// <remarks>
    /// Assignment is accountability and is deliberately separate from
    /// <see cref="CreatedBy"/>, which is provenance, and from
    /// <see cref="Subject"/>, which is who the task is about. Clearing it is a real
    /// state — work can genuinely be unowned, and saying so is more honest than
    /// leaving the last assignee's name on something they have handed back.
    /// Whether the assignee is a member of this organization is the handler's
    /// question, because the entity cannot see the membership table.
    /// </remarks>
    public void AssignTo(UserId? assignee, DateTimeOffset now)
    {
        if (State == TaskState.Completed)
        {
            throw new DomainException(
                "This task is completed, so it cannot be assigned to anybody.");
        }

        if (AssignedTo == assignee)
        {
            return;
        }

        AssignedTo = assignee;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Marks the task done, recording who and when.</summary>
    public void Complete(UserId completedBy, DateTimeOffset now)
    {
        if (State == TaskState.Completed)
        {
            throw new DomainException("Task is already completed.");
        }

        State = TaskState.Completed;
        CompletedAt = now;
        CompletedBy = completedBy;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Returns a completed task to open, clearing the completion stamp.
    /// </summary>
    /// <remarks>
    /// Clearing <see cref="CompletedAt"/> is the whole point: leaving it set would
    /// produce an open task that still claims a completion instant, which is
    /// exactly the inconsistency the state machine exists to prevent.
    /// </remarks>
    public void Reopen(DateTimeOffset now)
    {
        if (State == TaskState.Open)
        {
            throw new DomainException("Task is already open.");
        }

        State = TaskState.Open;
        CompletedAt = null;
        CompletedBy = null;
        UpdatedAt = now;
        Version++;
    }
}
