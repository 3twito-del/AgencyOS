using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Projects;

// -------------------------------------------------------------------- commands

public sealed record CreateProjectCommand(
    OrganizationId OrganizationId,
    string Title,
    ProjectType Type,
    string? WorkingTitle,
    DevelopmentStage Stage,
    string? Logline,
    string? Synopsis,
    CompanyId? PrimaryCompanyId,
    int? Year,
    UserId? LeadUserId,
    string? Notes);

/// <param name="ExpectedVersion">Version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateProjectCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    string Title,
    ProjectType Type,
    string? WorkingTitle,
    string? Logline,
    string? Synopsis,
    CompanyId? PrimaryCompanyId,
    int? Year,
    UserId? LeadUserId,
    string? Notes,
    int ExpectedVersion);

public sealed record ChangeProjectStatusCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    ProjectStatus Status,
    string? Reason,
    int ExpectedVersion);

public sealed record ChangeProjectStageCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    DevelopmentStage Stage,
    string? Reason,
    int ExpectedVersion);

public sealed record CreateProjectRoleCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    ProjectRoleType Type,
    string? Label,
    bool IsExclusive,
    string? Notes,
    int ExpectedVersion);

/// <param name="Action">Update, Hold, Close or Reopen.</param>
public sealed record ChangeProjectRoleCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    ProjectRoleId RoleId,
    ProjectRoleAction Action,
    string? Label,
    bool IsExclusive,
    string? Notes,
    int ExpectedVersion);

/// <summary>What a role change command does.</summary>
public enum ProjectRoleAction
{
    Update = 1,
    Hold = 2,
    Close = 3,
    Reopen = 4,
}

/// <param name="PersonId">The person attaching, when the party is a person.</param>
/// <param name="CompanyId">The company attaching, when the party is a company.</param>
public sealed record AttachToProjectRoleCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    ProjectRoleId RoleId,
    PersonId? PersonId,
    CompanyId? CompanyId,
    AttachmentStatus Status,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Source,
    string? Notes,
    int ExpectedVersion);

/// <param name="ExpectedVersion">The <em>attachment's</em> version, not the project's.</param>
public sealed record ChangeAttachmentStatusCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    AttachmentId AttachmentId,
    AttachmentStatus Status,
    DateOnly OccurredOn,
    string? Reason,
    int ExpectedVersion);

public sealed record AddProjectCompanyCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    CompanyId CompanyId,
    ProjectCompanyCapacity Capacity,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Notes,
    int ExpectedVersion);

public sealed record EndProjectCompanyCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    Guid ParticipationId,
    DateOnly EndsOn,
    int ExpectedVersion);

public sealed record CreateSourcePropertyCommand(
    OrganizationId OrganizationId,
    string Title,
    SourcePropertyType Type,
    string? AttributedCreator,
    PersonId? CreatorPersonId,
    string? SourceReference,
    string? Provenance,
    int? Year,
    string? Notes);

public sealed record LinkSourcePropertyCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    SourcePropertyId SourcePropertyId,
    string? Notes,
    int ExpectedVersion);

public sealed record UnlinkSourcePropertyCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    SourcePropertyId SourcePropertyId,
    int ExpectedVersion);

public sealed record LinkMaterialToProjectCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    MaterialId MaterialId,
    string? Notes,
    int ExpectedVersion);

public sealed record UnlinkMaterialFromProjectCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    MaterialId MaterialId,
    int ExpectedVersion);

/// <param name="ProjectId">The project to link to, or null to unlink.</param>
/// <param name="ExpectedVersion">The credit's version.</param>
public sealed record LinkCreditToProjectCommand(
    OrganizationId OrganizationId,
    CreditId CreditId,
    ProjectId? ProjectId,
    int ExpectedVersion);

// -------------------------------------------------------------------- handlers

/// <summary>Creates a project.</summary>
public sealed class CreateProjectHandler
{
    private readonly IProjectRepository _projects;
    private readonly ICompanyRepository _companies;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProjectHandler(
        IProjectRepository projects,
        ICompanyRepository companies,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _companies = companies;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProjectId> HandleAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await RequireCompanyAsync(_companies, command.OrganizationId, command.PrimaryCompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (command.LeadUserId is { } lead)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(_memberships, command.OrganizationId, lead, cancellationToken)
                .ConfigureAwait(false);
        }

        Project project = Project.Create(
            command.OrganizationId,
            command.Title,
            command.Type,
            actor,
            _clock.UtcNow,
            command.WorkingTitle,
            command.Stage,
            command.Logline,
            command.Synopsis,
            command.PrimaryCompanyId,
            command.Year,
            command.LeadUserId,
            command.Notes);

        _projects.Add(project);

        _audit.Record(
            AuditAction.ProjectCreated,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new
            {
                project.Title,
                Type = project.Type.ToString(),
                Stage = project.Stage.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return project.Id;
    }

    /// <summary>Confirms a company exists in this tenant before anything references it.</summary>
    internal static async Task RequireCompanyAsync(
        ICompanyRepository companies,
        OrganizationId organizationId,
        CompanyId? companyId,
        CancellationToken cancellationToken)
    {
        if (companyId is not { } id)
        {
            return;
        }

        bool exists = await companies
            .ExistsActiveAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Company), id.ToString());
        }
    }
}

/// <summary>Updates a project's descriptive fields.</summary>
public sealed class UpdateProjectHandler
{
    private readonly IProjectRepository _projects;
    private readonly ICompanyRepository _companies;
    private readonly IMembershipRepository _memberships;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProjectHandler(
        IProjectRepository projects,
        ICompanyRepository companies,
        IMembershipRepository memberships,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _companies = companies;
        _memberships = memberships;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(UpdateProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProjectHandler
            .RequireCompanyAsync(_companies, command.OrganizationId, command.PrimaryCompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (command.LeadUserId is { } lead)
        {
            await CreateProspectHandler
                .RequireTenantMemberAsync(_memberships, command.OrganizationId, lead, cancellationToken)
                .ConfigureAwait(false);
        }

        project.Update(
            command.Title,
            command.Type,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.WorkingTitle,
            command.Logline,
            command.Synopsis,
            command.PrimaryCompanyId,
            command.Year,
            command.LeadUserId,
            command.Notes);

        _audit.Record(
            AuditAction.ProjectUpdated,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { project.Title, Type = project.Type.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Moves a project's operational status or its development stage.</summary>
/// <remarks>
/// One handler for both because they share everything except which method they
/// call: the same authorization, the same version guard, the same audit shape.
/// They stay separate <em>commands</em>, because status and stage are separate
/// facts (ADR-0018).
/// </remarks>
public sealed class ChangeProjectLifecycleHandler
{
    private readonly IProjectRepository _projects;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeProjectLifecycleHandler(
        IProjectRepository projects,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        ChangeProjectStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        ProjectStatus from = project.Status;

        project.ChangeStatus(
            command.Status, _clock.UtcNow, actor, command.ExpectedVersion, command.Reason);

        _audit.Record(
            AuditAction.ProjectStatusChanged,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { From = from.ToString(), To = project.Status.ToString() },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ChangeProjectStageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        DevelopmentStage from = project.Stage;

        project.ChangeStage(
            command.Stage, _clock.UtcNow, actor, command.ExpectedVersion, command.Reason);

        _audit.Record(
            AuditAction.ProjectStageChanged,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { From = from.ToString(), To = project.Stage.ToString() },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Creates and changes the positions on a project.</summary>
public sealed class ProjectRoleHandler
{
    private readonly IProjectRepository _projects;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectRoleHandler(
        IProjectRepository projects,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProjectRoleId> HandleAsync(
        CreateProjectRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        ProjectRole role = project.AddRole(
            command.Type,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.Label,
            command.IsExclusive,
            command.Notes);

        _audit.Record(
            AuditAction.ProjectRoleCreated,
            entityType: nameof(ProjectRole),
            entityId: role.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new
            {
                ProjectId = project.Id.ToString(),
                Type = role.Type.ToString(),
                role.Label,
                role.IsExclusive,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return role.Id;
    }

    public async Task HandleAsync(
        ChangeProjectRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.RequireVersion(command.ExpectedVersion);

        ProjectRole role = project.RequireRole(command.RoleId);

        DateTimeOffset now = _clock.UtcNow;

        switch (command.Action)
        {
            case ProjectRoleAction.Update:
                project.UpdateRole(role.Id, command.Label, command.IsExclusive, command.Notes, now);
                break;

            case ProjectRoleAction.Hold:
                role.Hold(now);
                break;

            case ProjectRoleAction.Close:
                role.Close(now);
                break;

            case ProjectRoleAction.Reopen:
                role.Reopen(now);

                // Reopening restores whatever occupancy the attachments say, rather
                // than assuming the role is empty.
                project.RefreshRoleOccupancy(role.Id, now);
                break;

            default:
                throw new Domain.Common.DomainException($"Unknown role action '{command.Action}'.");
        }

        _audit.Record(
            command.Action == ProjectRoleAction.Close
                ? AuditAction.ProjectRoleClosed
                : AuditAction.ProjectRoleUpdated,
            entityType: nameof(ProjectRole),
            entityId: role.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new
            {
                ProjectId = project.Id.ToString(),
                Action = command.Action.ToString(),
                Status = role.Status.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records and changes who is attached to a project's roles.</summary>
public sealed class AttachmentHandler
{
    private readonly IProjectRepository _projects;
    private readonly IPersonRepository _people;
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AttachmentHandler(
        IProjectRepository projects,
        IPersonRepository people,
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _people = people;
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<AttachmentId> HandleAsync(
        AttachToProjectRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.PersonId is not null && command.CompanyId is not null)
        {
            throw new Domain.Common.DomainException(
                "An attachment names a person or a company, not both.");
        }

        AttachmentParty party;

        if (command.PersonId is { } personId)
        {
            bool exists = await _people
                .ExistsActiveAsync(command.OrganizationId, personId, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Person), personId.ToString());
            }

            party = AttachmentParty.Person(personId);
        }
        else if (command.CompanyId is { } companyId)
        {
            await CreateProjectHandler
                .RequireCompanyAsync(_companies, command.OrganizationId, companyId, cancellationToken)
                .ConfigureAwait(false);

            party = AttachmentParty.Company(companyId);
        }
        else
        {
            throw new Domain.Common.DomainException(
                "An attachment must name either a person or a company.");
        }

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        Attachment attachment = project.Attach(
            command.RoleId,
            party,
            command.Status,
            command.StartsOn,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.EndsOn,
            command.Source,
            command.Notes);

        _audit.Record(
            AuditAction.AttachmentCreated,
            entityType: nameof(Attachment),
            entityId: attachment.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new
            {
                ProjectId = project.Id.ToString(),
                RoleId = command.RoleId.ToString(),
                Party = party.ToString(),
                Status = attachment.Status.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return attachment.Id;
    }

    public async Task HandleAsync(
        ChangeAttachmentStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        Attachment attachment = project.RequireAttachment(command.AttachmentId);

        AttachmentStatus from = attachment.Status;

        // Guarded on the attachment's own version, because that is the record the
        // caller was looking at when they decided to change it.
        attachment.ChangeStatus(
            command.Status,
            command.OccurredOn,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Reason);

        // The role's occupancy is a consequence, not a separate decision: a role
        // whose last holder just withdrew is open again, and leaving it marked
        // filled would make every "missing director" view wrong.
        project.RefreshRoleOccupancy(attachment.ProjectRoleId, _clock.UtcNow);

        _audit.Record(
            AuditAction.AttachmentStatusChanged,
            entityType: nameof(Attachment),
            entityId: attachment.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { From = from.ToString(), To = attachment.Status.ToString() },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records which companies are involved in a project, and in what capacity.</summary>
public sealed class ProjectCompanyHandler
{
    private readonly IProjectRepository _projects;
    private readonly ICompanyRepository _companies;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectCompanyHandler(
        IProjectRepository projects,
        ICompanyRepository companies,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _companies = companies;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> HandleAsync(
        AddProjectCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProjectHandler
            .RequireCompanyAsync(_companies, command.OrganizationId, command.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        ProjectCompanyParticipation participation = project.AddParticipation(
            command.CompanyId,
            command.Capacity,
            command.StartsOn,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.EndsOn,
            command.Notes);

        _audit.Record(
            AuditAction.ProjectCompanyParticipationAdded,
            entityType: nameof(ProjectCompanyParticipation),
            entityId: participation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new
            {
                ProjectId = project.Id.ToString(),
                CompanyId = command.CompanyId.ToString(),
                Capacity = command.Capacity.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return participation.Id;
    }

    public async Task HandleAsync(
        EndProjectCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.EndParticipation(
            command.ParticipationId, command.EndsOn, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.ProjectCompanyParticipationEnded,
            entityType: nameof(ProjectCompanyParticipation),
            entityId: command.ParticipationId.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { ProjectId = project.Id.ToString(), command.EndsOn });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Creates source properties and links them to projects.</summary>
public sealed class SourcePropertyHandler
{
    private readonly ISourcePropertyRepository _sources;
    private readonly IProjectRepository _projects;
    private readonly IPersonRepository _people;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SourcePropertyHandler(
        ISourcePropertyRepository sources,
        IProjectRepository projects,
        IPersonRepository people,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _sources = sources;
        _projects = projects;
        _people = people;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<SourcePropertyId> HandleAsync(
        CreateSourcePropertyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.CreatorPersonId is { } creator)
        {
            bool exists = await _people
                .ExistsActiveAsync(command.OrganizationId, creator, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Person), creator.ToString());
            }
        }

        SourceProperty property = SourceProperty.Create(
            command.OrganizationId,
            command.Title,
            command.Type,
            actor,
            _clock.UtcNow,
            command.AttributedCreator,
            command.CreatorPersonId,
            command.SourceReference,
            command.Provenance,
            command.Year,
            command.Notes);

        _sources.Add(property);

        _audit.Record(
            AuditAction.SourcePropertyCreated,
            entityType: nameof(SourceProperty),
            entityId: property.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { property.Title, Type = property.Type.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return property.Id;
    }

    public async Task HandleAsync(
        LinkSourcePropertyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        SourceProperty? property = await _sources
            .FindAsync(command.OrganizationId, command.SourcePropertyId, cancellationToken)
            .ConfigureAwait(false);

        if (property is null)
        {
            throw new EntityNotFoundException(
                nameof(SourceProperty), command.SourcePropertyId.ToString());
        }

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.LinkSourceProperty(
            command.SourcePropertyId, _clock.UtcNow, actor, command.ExpectedVersion, command.Notes);

        _audit.Record(
            AuditAction.SourcePropertyLinked,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { SourcePropertyId = command.SourcePropertyId.ToString(), property.Title });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        UnlinkSourcePropertyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.UnlinkSourceProperty(command.SourcePropertyId, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.SourcePropertyUnlinked,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { SourcePropertyId = command.SourcePropertyId.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Links credits and materials to projects, and unlinks them.</summary>
/// <remarks>
/// <para>
/// M4 left <c>Credit.ProjectId</c> nullable and unconstrained as a seam. Closing it
/// means adding the foreign key and a command, not inferring links: matching a
/// credit to a project because the titles look alike would manufacture canonical
/// relationships out of a guess, and nothing downstream could tell which
/// relationships were asserted and which were assumed (ADR-0019).
/// </para>
/// <para>
/// Unlinking sets the column back to null. The credit's own title is untouched,
/// because what somebody's credit said is a fact in its own right.
/// </para>
/// </remarks>
public sealed class ProjectLinkHandler
{
    private readonly IProjectRepository _projects;
    private readonly ICreditRepository _credits;
    private readonly IMaterialRepository _materials;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectLinkHandler(
        IProjectRepository projects,
        ICreditRepository credits,
        IMaterialRepository materials,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _credits = credits;
        _materials = materials;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        LinkCreditToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Both permissions: this changes a talent record and asserts something
        // about a project, so holding one grant is not enough.
        await _guard
            .AuthorizeAsync(Permission.TalentWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Credit? credit = await _credits
            .FindAsync(command.OrganizationId, command.CreditId, cancellationToken)
            .ConfigureAwait(false);

        if (credit is null)
        {
            throw new EntityNotFoundException(nameof(Credit), command.CreditId.ToString());
        }

        if (command.ProjectId is { } projectId)
        {
            bool exists = await _projects
                .ExistsAsync(command.OrganizationId, projectId, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                throw new EntityNotFoundException(nameof(Project), projectId.ToString());
            }
        }

        credit.LinkToProject(command.ProjectId, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            command.ProjectId is null
                ? AuditAction.CreditUnlinkedFromProject
                : AuditAction.CreditLinkedToProject,
            entityType: nameof(Credit),
            entityId: credit.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { ProjectId = command.ProjectId?.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        LinkMaterialToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Material? material = await _materials
            .FindAsync(command.OrganizationId, command.MaterialId, cancellationToken)
            .ConfigureAwait(false);

        if (material is null)
        {
            throw new EntityNotFoundException(nameof(Material), command.MaterialId.ToString());
        }

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.LinkMaterial(
            command.MaterialId, _clock.UtcNow, actor, command.ExpectedVersion, command.Notes);

        _audit.Record(
            AuditAction.MaterialLinkedToProject,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { MaterialId = command.MaterialId.ToString(), material.Title });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        UnlinkMaterialFromProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.ProjectsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Project project = await ProjectCommandSupport
            .RequireProjectAsync(_projects, command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        project.UnlinkMaterial(command.MaterialId, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.MaterialUnlinkedFromProject,
            entityType: nameof(Project),
            entityId: project.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.ProjectsWrite,
            semanticDelta: new { MaterialId = command.MaterialId.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Shared lookups for the project command handlers.</summary>
internal static class ProjectCommandSupport
{
    internal static async Task<Project> RequireProjectAsync(
        IProjectRepository projects,
        OrganizationId organizationId,
        ProjectId projectId,
        CancellationToken cancellationToken)
    {
        Project? project = await projects
            .FindAsync(organizationId, projectId, cancellationToken)
            .ConfigureAwait(false);

        return project ?? throw new EntityNotFoundException(nameof(Project), projectId.ToString());
    }
}
