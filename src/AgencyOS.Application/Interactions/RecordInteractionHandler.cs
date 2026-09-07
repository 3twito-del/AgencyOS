using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Interactions;

/// <param name="Party">Who took part.</param>
/// <param name="Role">Optional note about how they took part.</param>
public sealed record InteractionParticipantInput(RelationshipEndpoint Party, string? Role = null);

/// <param name="Title">What needs doing.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="Subject">Party the task concerns; defaults to the first participant.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record FollowUpTaskInput(
    string Title,
    DateTimeOffset? DueAt = null,
    TaskPriority Priority = TaskPriority.Normal,
    RelationshipEndpoint? Subject = null,
    string? Notes = null);

/// <param name="OrganizationId">Owning tenant.</param>
/// <param name="Type">Kind of contact.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Summary">One-line factual summary.</param>
/// <param name="Participants">Everyone involved. At least one.</param>
/// <param name="DetailedNotes">Longer, subjective account.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
public sealed record RecordInteractionCommand(
    OrganizationId OrganizationId,
    InteractionType Type,
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyList<InteractionParticipantInput> Participants,
    string? DetailedNotes = null,
    FollowUpTaskInput? FollowUp = null);

/// <param name="InteractionId">The interaction that was recorded.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was requested.</param>
public sealed record RecordInteractionResult(InteractionId InteractionId, TaskItemId? FollowUpTaskId);

/// <summary>
/// Records what happened and, optionally, what happens next.
/// </summary>
/// <remarks>
/// <para>
/// The centre of M2. "Met Sarah at dinner, send her the screenplay Monday" is one
/// thought, so it is one command: the interaction and its follow-up task are added
/// to a single unit of work and committed by one save. EF Core wraps a single save
/// in one transaction, so either both exist or neither does.
/// </para>
/// <para>
/// That matters beyond tidiness. An interaction recorded without its follow-up
/// leaves the user believing they captured a commitment they did not; a task
/// created without its interaction is a reminder with no context. Partial state
/// here is actively misleading, which is why it is not reachable.
/// </para>
/// </remarks>
public sealed class RecordInteractionHandler
{
    private readonly IInteractionRepository _interactions;
    private readonly ITaskRepository _tasks;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RecordInteractionHandler(
        IInteractionRepository interactions,
        ITaskRepository tasks,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _interactions = interactions;
        _tasks = tasks;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RecordInteractionResult> HandleAsync(
        RecordInteractionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Participants);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.InteractionsRecord, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Participants.Count == 0)
        {
            throw new DomainException("An interaction must involve at least one participant.");
        }

        // Every participant must exist, be active, and belong to this tenant.
        foreach (InteractionParticipantInput participant in command.Participants)
        {
            await _guard.RequirePartyAsync(command.OrganizationId, participant.Party, cancellationToken)
                .ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        Interaction interaction = Interaction.Record(
            command.OrganizationId,
            command.Type,
            command.OccurredAt,
            command.Summary,
            [.. command.Participants.Select(p => (p.Party, p.Role))],
            actor,
            now,
            command.DetailedNotes);

        _interactions.Add(interaction);

        _audit.Record(
            AuditAction.InteractionRecorded,
            entityType: nameof(Interaction),
            entityId: interaction.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.InteractionsRecord,
            semanticDelta: new
            {
                Type = interaction.Type.ToString(),
                interaction.OccurredAt,
                interaction.Summary,
                Participants = interaction.Participants.Select(p => p.Party.ToString()).ToArray(),
            });

        TaskItemId? followUpId = null;

        if (command.FollowUp is { } followUp)
        {
            // Defaults to the first participant, because "call Sarah back" is about
            // Sarah, and making the user restate that would be friction for nothing.
            RelationshipEndpoint subject = followUp.Subject ?? command.Participants[0].Party;

            await _guard.RequirePartyAsync(command.OrganizationId, subject, cancellationToken)
                .ConfigureAwait(false);

            TaskItem task = TaskItem.Create(
                command.OrganizationId,
                followUp.Title,
                actor,
                now,
                followUp.Priority,
                followUp.DueAt,
                subject,
                interaction.Id,
                assignedTo: actor,
                followUp.Notes);

            _tasks.Add(task);
            followUpId = task.Id;

            _audit.Record(
                AuditAction.TaskCreated,
                entityType: nameof(TaskItem),
                entityId: task.Id.ToString(),
                organizationId: command.OrganizationId,
                permission: Permission.InteractionsRecord,
                semanticDelta: new
                {
                    task.Title,
                    Priority = task.Priority.ToString(),
                    task.DueAt,
                    Subject = subject.ToString(),
                    SourceInteractionId = interaction.Id.ToString(),
                },
                reason: "Follow-up captured while recording an interaction.");
        }

        // One save, one transaction: the interaction and its follow-up are a single
        // fact about the world.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordInteractionResult(interaction.Id, followUpId);
    }
}
