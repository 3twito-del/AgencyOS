using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Interactions;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Opportunities;

/// <param name="MaterialId">The material sent or shown.</param>
/// <param name="Note">Why it was included.</param>
public sealed record SubmissionMaterialInput(MaterialId MaterialId, string? Note = null);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
public sealed record OpportunityFollowUpInput(
    string Title,
    DateTimeOffset? DueAt = null,
    UserId? AssignedTo = null,
    string? Notes = null);

/// <param name="SentAt">When the agent says it went.</param>
/// <param name="ExpectedVersion">The <em>target's</em> version.</param>
public sealed record RecordSubmissionCommand(
    OrganizationId OrganizationId,
    OpportunityTargetId TargetId,
    DateTimeOffset SentAt,
    SubmissionChannel Channel,
    IReadOnlyList<SubmissionMaterialInput> Materials,
    string? Subject,
    string? Notes,
    DateOnly? ResponseExpectedBy,
    string? ExternalReference,
    OpportunityFollowUpInput? FollowUp,
    int ExpectedVersion);

/// <param name="SubmissionId">The submission recorded.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordSubmissionResult(SubmissionId SubmissionId, TaskItemId? FollowUpTaskId);

/// <param name="Participants">Who was there. Becomes the interaction's participants.</param>
/// <param name="ExpectedVersion">The <em>target's</em> version.</param>
public sealed record RecordPitchCommand(
    OrganizationId OrganizationId,
    OpportunityTargetId TargetId,
    InteractionType InteractionType,
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyList<InteractionParticipantInput> Participants,
    PitchKind Kind,
    PitchOutcome Outcome,
    IReadOnlyList<SubmissionMaterialInput> Materials,
    string? Subject,
    string? Notes,
    string? DetailedNotes,
    OpportunityFollowUpInput? FollowUp,
    int ExpectedVersion);

/// <param name="PitchId">The pitch recorded.</param>
/// <param name="InteractionId">The single interaction it is the commercial reading of.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordPitchResult(
    OpportunityPitchId PitchId,
    InteractionId InteractionId,
    TaskItemId? FollowUpTaskId);

/// <summary>
/// Records that material went to a target, and what happens next.
/// </summary>
/// <remarks>
/// <para>
/// AgencyOS records; it does not send. Nothing here transmits anything, and the
/// command is named for what it does so the surface cannot imply otherwise
/// (ADR-0020). What is stored is the agent's assertion that a submission occurred.
/// </para>
/// <para>
/// The submission, the target's progression, the material snapshots and the
/// follow-up task all commit together. The user pressed one button; a state where
/// the submission exists and the follow-up silently does not would be worse than
/// failing outright, because nobody would know to chase it.
/// </para>
/// </remarks>
public sealed class RecordSubmissionHandler
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IOpportunityTargetRepository _targets;
    private readonly ISubmissionRepository _submissions;
    private readonly IOpportunityPitchRepository _links;
    private readonly IMaterialRepository _materials;
    private readonly ITaskRepository _tasks;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RecordSubmissionHandler(
        IOpportunityRepository opportunities,
        IOpportunityTargetRepository targets,
        ISubmissionRepository submissions,
        IOpportunityPitchRepository links,
        IMaterialRepository materials,
        ITaskRepository tasks,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _opportunities = opportunities;
        _targets = targets;
        _submissions = submissions;
        _links = links;
        _materials = materials;
        _tasks = tasks;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RecordSubmissionResult> HandleAsync(
        RecordSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.SubmissionsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityTarget target = await MarketActivitySupport
            .RequireTargetAsync(_targets, command.OrganizationId, command.TargetId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await MarketActivitySupport
            .RequireOpportunityAsync(
                _opportunities, command.OrganizationId, target.OpportunityId, cancellationToken)
            .ConfigureAwait(false);

        opportunity.RequireMarketActive();

        target.RequireVersion(command.ExpectedVersion);

        Submission submission = Submission.Record(
            command.OrganizationId,
            target.OpportunityId,
            target.Id,
            command.SentAt,
            actor,
            command.Channel,
            actor,
            _clock.UtcNow,
            command.Subject,
            command.Notes,
            command.ResponseExpectedBy,
            command.ExternalReference);

        // The snapshot is taken here, from the material as it reads right now.
        // Reading it later would defeat the point: a retitled screenplay would
        // rewrite what the agent believed they sent.
        foreach (SubmissionMaterialInput input in command.Materials ?? [])
        {
            Material material = await MarketActivitySupport
                .RequireMaterialAsync(
                    _materials, command.OrganizationId, input.MaterialId, cancellationToken)
                .ConfigureAwait(false);

            submission.AddMaterial(
                material.Id, material.Title, material.Type, material.VersionLabel, input.Note);
        }

        _submissions.Add(submission);

        // The pipeline reacts, as a recorded transition rather than a flag.
        target.NoteSubmission(submission.Id, command.SentAt, _clock.UtcNow, actor);

        TaskItemId? followUpId = await MarketActivitySupport
            .AddFollowUpAsync(
                _tasks,
                _links,
                _memberships,
                command.OrganizationId,
                opportunity.Id,
                target.Id,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        _audit.Record(
            AuditAction.SubmissionRecorded,
            entityType: nameof(Submission),
            entityId: submission.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.SubmissionsWrite,
            semanticDelta: new
            {
                OpportunityId = opportunity.Id.ToString(),
                TargetId = target.Id.ToString(),
                Channel = command.Channel.ToString(),
                Materials = submission.Materials.Count,
                FollowUpTaskId = followUpId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordSubmissionResult(submission.Id, followUpId);
    }
}

/// <summary>
/// Records a pitch as the commercial reading of one interaction.
/// </summary>
/// <remarks>
/// <para>
/// One real-world event, one interaction identity. The command creates the M2
/// interaction itself rather than asking the user to create one first and link it,
/// because two steps is how a meeting ends up recorded twice with the same
/// participants and the same timestamp (ADR-0020).
/// </para>
/// <para>
/// Interaction, pitch, material snapshots, target progression and follow-up task
/// commit together.
/// </para>
/// </remarks>
public sealed class RecordPitchHandler
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IOpportunityTargetRepository _targets;
    private readonly IOpportunityPitchRepository _pitches;
    private readonly IInteractionRepository _interactions;
    private readonly IMaterialRepository _materials;
    private readonly ITaskRepository _tasks;
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RecordPitchHandler(
        IOpportunityRepository opportunities,
        IOpportunityTargetRepository targets,
        IOpportunityPitchRepository pitches,
        IInteractionRepository interactions,
        IMaterialRepository materials,
        ITaskRepository tasks,
        IPersonRepository people,
        ICompanyRepository companies,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _opportunities = opportunities;
        _targets = targets;
        _pitches = pitches;
        _interactions = interactions;
        _materials = materials;
        _tasks = tasks;
        _people = people;
        _companies = companies;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<RecordPitchResult> HandleAsync(
        RecordPitchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        // Recording a pitch records an interaction, so it needs the grant that
        // governs interactions too. Holding one without the other should not be a
        // way in.
        await _guard
            .AuthorizeAsync(Permission.InteractionsRecord, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityTarget target = await MarketActivitySupport
            .RequireTargetAsync(_targets, command.OrganizationId, command.TargetId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await MarketActivitySupport
            .RequireOpportunityAsync(
                _opportunities, command.OrganizationId, target.OpportunityId, cancellationToken)
            .ConfigureAwait(false);

        opportunity.RequireMarketActive();

        target.RequireVersion(command.ExpectedVersion);

        Interaction interaction = await BuildInteractionAsync(command, actor, cancellationToken)
            .ConfigureAwait(false);

        _interactions.Add(interaction);

        OpportunityPitch pitch = OpportunityPitch.Record(
            command.OrganizationId,
            opportunity.Id,
            target.Id,
            interaction.Id,
            command.Kind,
            command.Outcome,
            command.OccurredAt,
            actor,
            _clock.UtcNow,
            command.Subject,
            command.Notes);

        foreach (SubmissionMaterialInput input in command.Materials ?? [])
        {
            Material material = await MarketActivitySupport
                .RequireMaterialAsync(
                    _materials, command.OrganizationId, input.MaterialId, cancellationToken)
                .ConfigureAwait(false);

            pitch.AddMaterial(material.Id, material.Title, material.Type, material.VersionLabel);
        }

        _pitches.Add(pitch);

        target.NotePitch(pitch.Id, command.OccurredAt, _clock.UtcNow, actor);

        // The outcome the agent recorded moves the target, so the pipeline agrees
        // with what the pitch says happened rather than needing a second command.
        ApplyOutcome(target, command, actor, _clock.UtcNow, pitch.Id);

        TaskItemId? followUpId = await MarketActivitySupport
            .AddFollowUpAsync(
                _tasks,
                _pitches,
                _memberships,
                command.OrganizationId,
                opportunity.Id,
                target.Id,
                command.FollowUp,
                actor,
                _clock.UtcNow,
                cancellationToken,
                interaction.Id)
            .ConfigureAwait(false);

        _audit.Record(
            AuditAction.PitchRecorded,
            entityType: nameof(OpportunityPitch),
            entityId: pitch.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new
            {
                OpportunityId = opportunity.Id.ToString(),
                TargetId = target.Id.ToString(),
                InteractionId = interaction.Id.ToString(),
                Outcome = command.Outcome.ToString(),
                FollowUpTaskId = followUpId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordPitchResult(pitch.Id, interaction.Id, followUpId);
    }

    /// <summary>Moves the target to match what the pitch produced.</summary>
    /// <remarks>
    /// Only two outcomes say something about the pipeline. The rest leave the
    /// target where it is, because "they took it away" is not progress and
    /// pretending otherwise would inflate every pipeline report.
    /// </remarks>
    private static void ApplyOutcome(
        OpportunityTarget target,
        RecordPitchCommand command,
        UserId actor,
        DateTimeOffset now,
        OpportunityPitchId pitchId)
    {
        OpportunityTargetStage? stage = command.Outcome switch
        {
            PitchOutcome.Interested => OpportunityTargetStage.Interested,
            PitchOutcome.Passed => OpportunityTargetStage.Passed,
            _ => null,
        };

        if (stage is not { } target_stage)
        {
            return;
        }

        if (!OpportunityTarget.AllowedTransitions[target.Stage].Contains(target_stage))
        {
            return;
        }

        target.MoveTo(
            target_stage,
            command.OccurredAt,
            now,
            actor,
            target.Version,
            pitchId: pitchId,
            note: command.Subject);
    }

    private async Task<Interaction> BuildInteractionAsync(
        RecordPitchCommand command,
        UserId actor,
        CancellationToken cancellationToken)
    {
        List<(RelationshipEndpoint Party, string? Role)> participants = [];

        foreach (InteractionParticipantInput participant in command.Participants ?? [])
        {
            await RequirePartyAsync(command.OrganizationId, participant, cancellationToken)
                .ConfigureAwait(false);

            participants.Add((participant.Party, participant.Role));
        }

        // Built through the M2 factory, so a pitch's interaction is validated the
        // same way any other interaction is: at least one participant, nobody
        // listed twice.
        return Interaction.Record(
            command.OrganizationId,
            command.InteractionType,
            command.OccurredAt,
            command.Summary,
            participants,
            actor,
            _clock.UtcNow,
            command.DetailedNotes);
    }

    private async Task RequirePartyAsync(
        OrganizationId organizationId,
        InteractionParticipantInput participant,
        CancellationToken cancellationToken)
    {
        if (participant.Party.AsPerson is { } person)
        {
            bool exists = await _people
                .ExistsActiveAsync(organizationId, person, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Person), person.ToString());
            }

            return;
        }

        if (participant.Party.AsCompany is { } company)
        {
            bool exists = await _companies
                .ExistsActiveAsync(organizationId, company, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Company), company.ToString());
            }
        }
    }
}

/// <summary>Shared lookups and the follow-up task the market commands both create.</summary>
internal static class MarketActivitySupport
{
    internal static async Task<Opportunity> RequireOpportunityAsync(
        IOpportunityRepository opportunities,
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken)
    {
        Opportunity? opportunity = await opportunities
            .FindAsync(organizationId, opportunityId, cancellationToken)
            .ConfigureAwait(false);

        return opportunity
            ?? throw new EntityNotFoundException(nameof(Opportunity), opportunityId.ToString());
    }

    internal static async Task<OpportunityTarget> RequireTargetAsync(
        IOpportunityTargetRepository targets,
        OrganizationId organizationId,
        OpportunityTargetId targetId,
        CancellationToken cancellationToken)
    {
        OpportunityTarget? target = await targets
            .FindAsync(organizationId, targetId, cancellationToken)
            .ConfigureAwait(false);

        return target
            ?? throw new EntityNotFoundException(nameof(OpportunityTarget), targetId.ToString());
    }

    internal static async Task<Material> RequireMaterialAsync(
        IMaterialRepository materials,
        OrganizationId organizationId,
        MaterialId materialId,
        CancellationToken cancellationToken)
    {
        Material? material = await materials
            .FindAsync(organizationId, materialId, cancellationToken)
            .ConfigureAwait(false);

        return material ?? throw new EntityNotFoundException(nameof(Material), materialId.ToString());
    }

    /// <summary>
    /// Creates the follow-up task and records which pursuit it belongs to.
    /// </summary>
    /// <remarks>
    /// The task carries no opportunity column. Pursuit context lives in
    /// <see cref="OpportunityTaskLink"/>, because widening the task row by two
    /// nullable identifiers per milestone is a shape that gets worse every time
    /// (ADR-0020).
    /// </remarks>
    internal static async Task<TaskItemId?> AddFollowUpAsync(
        ITaskRepository tasks,
        IOpportunityPitchRepository links,
        IMembershipRepository memberships,
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunityTargetId targetId,
        OpportunityFollowUpInput? followUp,
        UserId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        InteractionId? sourceInteractionId = null)
    {
        if (followUp is null)
        {
            return null;
        }

        if (followUp.AssignedTo is { } assignee)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(memberships, organizationId, assignee, cancellationToken)
                .ConfigureAwait(false);
        }

        TaskItem task = TaskItem.Create(
            organizationId,
            followUp.Title,
            actor,
            now,
            dueAt: followUp.DueAt,
            sourceInteractionId: sourceInteractionId,
            assignedTo: followUp.AssignedTo ?? actor,
            notes: followUp.Notes);

        tasks.Add(task);

        links.AddTaskLink(OpportunityTaskLink.Create(
            organizationId, task.Id, opportunityId, targetId, now));

        return task.Id;
    }
}
