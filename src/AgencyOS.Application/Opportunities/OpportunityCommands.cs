using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Application.Opportunities;

// -------------------------------------------------------------------- commands

public sealed record CreateOpportunityCommand(
    OrganizationId OrganizationId,
    string Name,
    OpportunityKind Kind,
    UserId OwnerUserId,
    DateOnly OpenedOn,
    OpportunityPriority Priority,
    string? Description,
    string? StrategyNotes,
    IReadOnlyList<OpportunitySubjectInput> Subjects);

/// <param name="Subject">The typed record this subject points at.</param>
/// <param name="Role">What it is doing in the pursuit.</param>
public sealed record OpportunitySubjectInput(
    OpportunitySubjectRef Subject,
    OpportunitySubjectRole Role,
    string? Note = null);

/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateOpportunityCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    string Name,
    OpportunityPriority Priority,
    UserId OwnerUserId,
    string? Description,
    string? StrategyNotes,
    int ExpectedVersion);

/// <param name="Outcome">Required when closing, refused otherwise.</param>
public sealed record ChangeOpportunityStatusCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    OpportunityStatus Status,
    DateOnly OccurredOn,
    OpportunityOutcome? Outcome,
    string? Reason,
    int ExpectedVersion);

public sealed record AddOpportunitySubjectCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    OpportunitySubjectRef Subject,
    OpportunitySubjectRole Role,
    string? Note,
    int ExpectedVersion);

public sealed record RemoveOpportunitySubjectCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    Guid SubjectId,
    int ExpectedVersion);

/// <param name="CompanyId">The company approached. Exactly one of this and PersonId.</param>
/// <param name="PersonId">The person approached. Exactly one of this and CompanyId.</param>
/// <param name="ContactPersonId">The individual dealt with. Only valid for a company target.</param>
public sealed record AddOpportunityTargetCommand(
    OrganizationId OrganizationId,
    OpportunityId OpportunityId,
    CompanyId? CompanyId,
    PersonId? PersonId,
    PersonId? ContactPersonId,
    UserId? OwnerUserId,
    DateOnly? NextActionOn,
    string? Notes,
    int ExpectedVersion);

/// <param name="ExpectedVersion">The <em>target's</em> version, not the opportunity's.</param>
public sealed record UpdateOpportunityTargetCommand(
    OrganizationId OrganizationId,
    OpportunityTargetId TargetId,
    PersonId? ContactPersonId,
    UserId? OwnerUserId,
    DateOnly? NextActionOn,
    string? Notes,
    int ExpectedVersion);

/// <param name="Stage">The stage to move to. Must be legal from the current one.</param>
/// <param name="OccurredAt">When it happened, which is not always now.</param>
public sealed record MoveOpportunityTargetCommand(
    OrganizationId OrganizationId,
    OpportunityTargetId TargetId,
    OpportunityTargetStage Stage,
    DateTimeOffset OccurredAt,
    string? Note,
    int ExpectedVersion);

/// <param name="Kind">What the target did. A stage change is a different command.</param>
/// <param name="SubmissionId">The submission this responds to, when it responds to one.</param>
public sealed record RecordTargetResponseCommand(
    OrganizationId OrganizationId,
    OpportunityTargetId TargetId,
    OpportunityTargetEventKind Kind,
    DateTimeOffset OccurredAt,
    SubmissionId? SubmissionId,
    string? Note,
    int ExpectedVersion);

/// <summary>Creates and maintains pursuits.</summary>
public sealed class OpportunityHandler
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IOpportunitySubjectTargets _subjectTargets;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OpportunityHandler(
        IOpportunityRepository opportunities,
        IOpportunitySubjectTargets subjectTargets,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _opportunities = opportunities;
        _subjectTargets = subjectTargets;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<OpportunityId> HandleAsync(
        CreateOpportunityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = Opportunity.Create(
            command.OrganizationId,
            command.Name,
            command.Kind,
            command.OwnerUserId,
            command.OpenedOn,
            actor,
            _clock.UtcNow,
            command.Priority,
            command.Description,
            command.StrategyNotes);

        foreach (OpportunitySubjectInput input in command.Subjects ?? [])
        {
            await _subjectTargets
                .RequireAsync(command.OrganizationId, input.Subject, cancellationToken)
                .ConfigureAwait(false);

            opportunity.AddSubject(
                input.Subject, input.Role, _clock.UtcNow, opportunity.Version, input.Note);
        }

        _opportunities.Add(opportunity);

        _audit.Record(
            AuditAction.OpportunityCreated,
            entityType: nameof(Opportunity),
            entityId: opportunity.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new
            {
                opportunity.Name,
                Kind = opportunity.Kind.ToString(),
                Owner = command.OwnerUserId.ToString(),
                Subjects = opportunity.Subjects.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return opportunity.Id;
    }

    public async Task HandleAsync(
        UpdateOpportunityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(
                _memberships, command.OrganizationId, command.OwnerUserId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        opportunity.Update(
            command.Name,
            command.Priority,
            command.OwnerUserId,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.Description,
            command.StrategyNotes);

        _audit.Record(
            AuditAction.OpportunityUpdated,
            entityType: nameof(Opportunity),
            entityId: opportunity.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new { opportunity.Name, Owner = command.OwnerUserId.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a pursuit through its lifecycle.
    /// </summary>
    /// <remarks>
    /// Activating checks the subjects make sense first. A draft is where somebody
    /// assembles the pieces, so an incomplete one is allowed to exist; taking it out
    /// with nothing identified as the thing being pursued is not.
    /// </remarks>
    public async Task HandleAsync(
        ChangeOpportunityStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        if (command.Status == OpportunityStatus.Active)
        {
            opportunity.RequireCoherentSubjects();
        }

        OpportunityStatus from = opportunity.Status;

        opportunity.ChangeStatus(
            command.Status,
            command.OccurredOn,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Outcome,
            command.Reason);

        _audit.Record(
            AuditAction.OpportunityStatusChanged,
            entityType: nameof(Opportunity),
            entityId: opportunity.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new
            {
                From = from.ToString(),
                To = opportunity.Status.ToString(),
                Outcome = opportunity.Outcome?.ToString(),
            },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddOpportunitySubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await _subjectTargets
            .RequireAsync(command.OrganizationId, command.Subject, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        OpportunitySubject subject = opportunity.AddSubject(
            command.Subject, command.Role, _clock.UtcNow, command.ExpectedVersion, command.Note);

        _audit.Record(
            AuditAction.OpportunitySubjectAdded,
            entityType: nameof(OpportunitySubject),
            entityId: subject.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new
            {
                OpportunityId = opportunity.Id.ToString(),
                Kind = subject.Kind.ToString(),
                subject.TargetId,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return subject.Id;
    }

    public async Task HandleAsync(
        RemoveOpportunitySubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        opportunity.RemoveSubject(command.SubjectId, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.OpportunitySubjectRemoved,
            entityType: nameof(OpportunitySubject),
            entityId: command.SubjectId.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new { OpportunityId = opportunity.Id.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Opportunity> RequireAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken)
    {
        Opportunity? opportunity = await _opportunities
            .FindAsync(organizationId, opportunityId, cancellationToken)
            .ConfigureAwait(false);

        return opportunity
            ?? throw new EntityNotFoundException(nameof(Opportunity), opportunityId.ToString());
    }
}

/// <summary>Adds and moves the parties a pursuit is aimed at.</summary>
public sealed class OpportunityTargetHandler
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IOpportunityTargetRepository _targets;
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OpportunityTargetHandler(
        IOpportunityRepository opportunities,
        IOpportunityTargetRepository targets,
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
        _people = people;
        _companies = companies;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<OpportunityTargetId> HandleAsync(
        AddOpportunityTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Opportunity opportunity = await RequireOpportunityAsync(
            command.OrganizationId, command.OpportunityId, cancellationToken).ConfigureAwait(false);

        opportunity.RequireVersion(command.ExpectedVersion);
        opportunity.RequireOpen();

        await RequireEndpointsAsync(command, cancellationToken).ConfigureAwait(false);

        if (command.OwnerUserId is { } owner)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(_memberships, command.OrganizationId, owner, cancellationToken)
                .ConfigureAwait(false);
        }

        Guid endpointId = command.CompanyId?.Value ?? command.PersonId!.Value.Value;

        OpportunityTarget? open = await _targets
            .FindOpenForEndpointAsync(
                command.OrganizationId, command.OpportunityId, endpointId, cancellationToken)
            .ConfigureAwait(false);

        if (open is not null)
        {
            throw new AlreadyExistsException(
                "This party is already an open target on this opportunity. "
                    + "Work that target rather than adding a second.");
        }

        OpportunityTarget target = OpportunityTarget.Create(
            command.OrganizationId,
            command.OpportunityId,
            command.CompanyId,
            command.PersonId,
            command.ContactPersonId,
            _clock.UtcNow,
            actor,
            command.OwnerUserId,
            command.NextActionOn,
            command.Notes);

        _targets.Add(target);

        _audit.Record(
            AuditAction.OpportunityTargetAdded,
            entityType: nameof(OpportunityTarget),
            entityId: target.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new
            {
                OpportunityId = command.OpportunityId.ToString(),
                CompanyId = command.CompanyId?.ToString(),
                PersonId = command.PersonId?.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return target.Id;
    }

    public async Task HandleAsync(
        UpdateOpportunityTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityTarget target = await RequireTargetAsync(
            command.OrganizationId, command.TargetId, cancellationToken).ConfigureAwait(false);

        if (command.ContactPersonId is { } contact)
        {
            await RequirePersonAsync(command.OrganizationId, contact, cancellationToken)
                .ConfigureAwait(false);
        }

        if (command.OwnerUserId is { } owner)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(_memberships, command.OrganizationId, owner, cancellationToken)
                .ConfigureAwait(false);
        }

        target.Update(
            command.ContactPersonId,
            command.OwnerUserId,
            command.NextActionOn,
            command.Notes,
            _clock.UtcNow,
            command.ExpectedVersion);

        _audit.Record(
            AuditAction.OpportunityTargetUpdated,
            entityType: nameof(OpportunityTarget),
            entityId: target.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new { command.NextActionOn });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a target through the pipeline.
    /// </summary>
    /// <remarks>
    /// Refused when the pursuit is not being worked. Recording that a studio passed
    /// on an opportunity that was cancelled last month describes something that did
    /// not happen the way the record would say.
    /// </remarks>
    public async Task HandleAsync(
        MoveOpportunityTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityTarget target = await RequireTargetAsync(
            command.OrganizationId, command.TargetId, cancellationToken).ConfigureAwait(false);

        Opportunity opportunity = await RequireOpportunityAsync(
            command.OrganizationId, target.OpportunityId, cancellationToken).ConfigureAwait(false);

        opportunity.RequireMarketActive();

        OpportunityTargetStage from = target.Stage;

        target.MoveTo(
            command.Stage,
            command.OccurredAt,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            note: command.Note);

        _audit.Record(
            AuditAction.OpportunityTargetMoved,
            entityType: nameof(OpportunityTarget),
            entityId: target.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new { From = from.ToString(), To = target.Stage.ToString() },
            reason: command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records something a target did that did not move them along.</summary>
    public async Task HandleAsync(
        RecordTargetResponseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.OpportunitiesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityTarget target = await RequireTargetAsync(
            command.OrganizationId, command.TargetId, cancellationToken).ConfigureAwait(false);

        Opportunity opportunity = await RequireOpportunityAsync(
            command.OrganizationId, target.OpportunityId, cancellationToken).ConfigureAwait(false);

        opportunity.RequireMarketActive();

        target.RecordEvent(
            command.Kind,
            command.OccurredAt,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            submissionId: command.SubmissionId,
            note: command.Note);

        _audit.Record(
            AuditAction.OpportunityTargetEventRecorded,
            entityType: nameof(OpportunityTarget),
            entityId: target.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.OpportunitiesWrite,
            semanticDelta: new { Kind = command.Kind.ToString() },
            reason: command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireEndpointsAsync(
        AddOpportunityTargetCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId is { } company)
        {
            bool exists = await _companies
                .ExistsActiveAsync(command.OrganizationId, company, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Company), company.ToString());
            }
        }

        if (command.PersonId is { } person)
        {
            await RequirePersonAsync(command.OrganizationId, person, cancellationToken)
                .ConfigureAwait(false);
        }

        if (command.ContactPersonId is { } contact)
        {
            await RequirePersonAsync(command.OrganizationId, contact, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RequirePersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        bool exists = await _people
            .ExistsActiveAsync(organizationId, personId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Person), personId.ToString());
        }
    }

    private async Task<Opportunity> RequireOpportunityAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken)
    {
        Opportunity? opportunity = await _opportunities
            .FindAsync(organizationId, opportunityId, cancellationToken)
            .ConfigureAwait(false);

        return opportunity
            ?? throw new EntityNotFoundException(nameof(Opportunity), opportunityId.ToString());
    }

    private async Task<OpportunityTarget> RequireTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId targetId,
        CancellationToken cancellationToken)
    {
        OpportunityTarget? target = await _targets
            .FindAsync(organizationId, targetId, cancellationToken)
            .ConfigureAwait(false);

        return target
            ?? throw new EntityNotFoundException(nameof(OpportunityTarget), targetId.ToString());
    }
}
