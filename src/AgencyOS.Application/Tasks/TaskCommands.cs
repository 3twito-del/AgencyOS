using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Tasks;

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Subject">Party the task concerns.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record CreateTaskCommand(
    OrganizationId OrganizationId,
    string Title,
    TaskPriority Priority = TaskPriority.Normal,
    DateTimeOffset? DueAt = null,
    RelationshipEndpoint? Subject = null,
    string? Notes = null);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="TaskId">Task to transition.</param>
public sealed record CompleteTaskCommand(OrganizationId OrganizationId, TaskItemId TaskId);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="TaskId">Task to transition.</param>
public sealed record ReopenTaskCommand(OrganizationId OrganizationId, TaskItemId TaskId);

/// <summary>Creates a standalone task.</summary>
/// <remarks>
/// Most tasks arrive attached to an interaction through
/// <c>RecordInteractionHandler</c>. This is the path for work that has no
/// conversation behind it.
/// </remarks>
public sealed class CreateTaskHandler
{
    private readonly ITaskRepository _tasks;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskHandler(
        ITaskRepository tasks,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<TaskItemId> HandleAsync(
        CreateTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.TasksWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Subject is { } subject)
        {
            await _guard.RequirePartyAsync(command.OrganizationId, subject, cancellationToken)
                .ConfigureAwait(false);
        }

        TaskItem task = TaskItem.Create(
            command.OrganizationId,
            command.Title,
            actor,
            _clock.UtcNow,
            command.Priority,
            command.DueAt,
            command.Subject,
            sourceInteractionId: null,
            assignedTo: actor,
            command.Notes);

        _tasks.Add(task);

        _audit.Record(
            AuditAction.TaskCreated,
            entityType: nameof(TaskItem),
            entityId: task.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TasksWrite,
            semanticDelta: new
            {
                task.Title,
                Priority = task.Priority.ToString(),
                task.DueAt,
                Subject = task.Subject?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return task.Id;
    }
}

/// <summary>Marks a task done.</summary>
public sealed class CompleteTaskHandler
{
    private readonly ITaskRepository _tasks;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteTaskHandler(
        ITaskRepository tasks,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(CompleteTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.TasksWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        TaskItem task =
            await _tasks.FindAsync(command.OrganizationId, command.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TaskItem), command.TaskId.ToString());

        task.Complete(actor, _clock.UtcNow);

        _audit.Record(
            AuditAction.TaskCompleted,
            entityType: nameof(TaskItem),
            entityId: task.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TasksWrite,
            semanticDelta: new { task.Title, task.CompletedAt });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Returns a completed task to open.</summary>
public sealed class ReopenTaskHandler
{
    private readonly ITaskRepository _tasks;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ReopenTaskHandler(
        ITaskRepository tasks,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(ReopenTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.TasksWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        TaskItem task =
            await _tasks.FindAsync(command.OrganizationId, command.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TaskItem), command.TaskId.ToString());

        task.Reopen(_clock.UtcNow);

        _audit.Record(
            AuditAction.TaskReopened,
            entityType: nameof(TaskItem),
            entityId: task.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.TasksWrite,
            semanticDelta: new { task.Title });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
