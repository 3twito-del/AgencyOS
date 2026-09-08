using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Intelligence;

// -------------------------------------------------------------------- commands

public sealed record OpenResearchCaseCommand(
    OrganizationId OrganizationId,
    string Question,
    IntelligenceSensitivity Sensitivity,
    Guid? OwnerUserId = null,
    string? Context = null,
    IReadOnlyList<SubjectInput>? Subjects = null);

public sealed record UpdateResearchCaseCommand(
    OrganizationId OrganizationId,
    ResearchCaseId ResearchCaseId,
    string Question,
    IntelligenceSensitivity Sensitivity,
    int ExpectedVersion,
    string? Context = null);

public sealed record ChangeResearchCaseStatusCommand(
    OrganizationId OrganizationId,
    ResearchCaseId ResearchCaseId,
    ResearchCaseStatus Status,
    int ExpectedVersion,
    string? Conclusion = null);

/// <summary>
/// Attaches an existing intelligence object or task to a case.
/// </summary>
/// <remarks>
/// Always a link, never a copy. A finding of fact belongs in a signal, an
/// interpretation in a thesis and a forecast in a prediction; a case that held its
/// own version of any of them would be a second copy with none of their semantics
/// (ADR-0030).
/// </remarks>
public sealed record LinkResearchItemCommand(
    OrganizationId OrganizationId,
    ResearchCaseId ResearchCaseId,
    ResearchLinkKind Kind,
    Guid LinkedId,
    int ExpectedVersion,
    string? Note = null);

public sealed record UnlinkResearchItemCommand(
    OrganizationId OrganizationId,
    ResearchCaseId ResearchCaseId,
    Guid LinkId,
    int ExpectedVersion);

public sealed record AddResearchSubjectCommand(
    OrganizationId OrganizationId,
    ResearchCaseId ResearchCaseId,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);

// --------------------------------------------------------------------- handler

/// <summary>
/// Opens and works research questions.
/// </summary>
/// <remarks>
/// A case is a workspace. It organizes evidence and conclusions and holds none,
/// which is what will let M12 read structured signals and theses rather than
/// parsing somebody's notes (ADR-0030).
/// </remarks>
public sealed class ResearchCaseHandler
{
    private readonly IResearchCaseRepository _cases;
    private readonly IIntelligenceSourceRepository _sources;
    private readonly ISignalRepository _signals;
    private readonly IThesisRepository _theses;
    private readonly IPredictionRepository _predictions;
    private readonly ITaskRepository _tasks;
    private readonly IIntelligenceSubjectValidator _subjects;
    private readonly IIntelligenceEventRepository _events;
    private readonly IntelligenceAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ResearchCaseHandler(
        IResearchCaseRepository cases,
        IIntelligenceSourceRepository sources,
        ISignalRepository signals,
        IThesisRepository theses,
        IPredictionRepository predictions,
        ITaskRepository tasks,
        IIntelligenceSubjectValidator subjects,
        IIntelligenceEventRepository events,
        IntelligenceAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _cases = cases;
        _sources = sources;
        _signals = signals;
        _theses = theses;
        _predictions = predictions;
        _tasks = tasks;
        _subjects = subjects;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<ResearchCaseId> HandleAsync(
        OpenResearchCaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            await RequireSubjectAsync(command.OrganizationId, subject, cancellationToken)
                .ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.UtcNow;

        ResearchCase researchCase = ResearchCase.Open(
            command.OrganizationId,
            command.Question,
            command.Sensitivity,
            command.OwnerUserId is { } owner ? new UserId(owner) : actor,
            actor,
            now,
            command.Context);

        foreach (SubjectInput subject in command.Subjects ?? [])
        {
            researchCase.AddSubject(subject.Kind, subject.SubjectId, actor, now, subject.Note);
        }

        _cases.Add(researchCase);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.ResearchCase,
            researchCase.Id.Value,
            IntelligenceEventKind.ResearchCaseOpened,
            $"Research opened: {researchCase.Question}",
            actor,
            now));

        _audit.Record(
            AuditAction.ResearchCaseOpened,
            entityType: nameof(ResearchCase),
            entityId: researchCase.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new
            {
                Sensitivity = researchCase.Sensitivity.ToString(),
                SubjectCount = researchCase.Subjects.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return researchCase.Id;
    }

    public async Task HandleAsync(
        UpdateResearchCaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ResearchCase researchCase = await RequireAsync(
            command.OrganizationId, command.ResearchCaseId, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(
                command.OrganizationId, researchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        researchCase.Update(
            command.Question, command.Sensitivity, command.ExpectedVersion, command.Context);

        _audit.Record(
            AuditAction.ResearchCaseUpdated,
            entityType: nameof(ResearchCase),
            entityId: researchCase.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pauses, resumes, completes or cancels a case.</summary>
    public async Task HandleAsync(
        ChangeResearchCaseStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ResearchCase researchCase = await RequireAsync(
            command.OrganizationId, command.ResearchCaseId, cancellationToken)
            .ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(
                command.OrganizationId, researchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        switch (command.Status)
        {
            case ResearchCaseStatus.Paused:
                researchCase.Pause(now, command.ExpectedVersion);
                break;

            case ResearchCaseStatus.Open:
                researchCase.Resume(now, command.ExpectedVersion);
                break;

            case ResearchCaseStatus.Completed:
                researchCase.Complete(
                    command.Conclusion ?? throw new DomainException(
                        "Completing a case records what was concluded. What was learned "
                            + "belongs in the linked signals and theses; this is the summary "
                            + "a reader sees first."),
                    now,
                    command.ExpectedVersion);
                break;

            case ResearchCaseStatus.Cancelled:
                researchCase.Cancel(
                    command.Conclusion ?? throw new DomainException(
                        "Cancelling a case records why the question stopped mattering."),
                    now,
                    command.ExpectedVersion);
                break;

            default:
                throw new DomainException($"'{command.Status}' is not a research case status.");
        }

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.ResearchCase,
            researchCase.Id.Value,
            command.Status == ResearchCaseStatus.Completed
                ? IntelligenceEventKind.ResearchCaseCompleted
                : IntelligenceEventKind.ResearchCaseStatusChanged,
            $"Research case is now {command.Status.ToString().ToLowerInvariant()}.",
            actor,
            now,
            command.Conclusion));

        _audit.Record(
            AuditAction.ResearchCaseStatusChanged,
            entityType: nameof(ResearchCase),
            entityId: researchCase.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Status = command.Status.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        LinkResearchItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ResearchCase researchCase = await RequireAsync(
            command.OrganizationId, command.ResearchCaseId, cancellationToken)
            .ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(
                command.OrganizationId, researchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireLinkedAsync(
            command.OrganizationId, command.Kind, command.LinkedId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.UtcNow;

        ResearchCaseLink link = researchCase.AddLink(
            command.Kind, command.LinkedId, actor, now, command.Note);

        _events.Add(IntelligenceEvent.Record(
            command.OrganizationId,
            IntelligenceOwnerKind.ResearchCase,
            researchCase.Id.Value,
            IntelligenceEventKind.ResearchCaseLinked,
            $"{command.Kind} attached to the case.",
            actor,
            now));

        _audit.Record(
            AuditAction.ResearchCaseLinked,
            entityType: nameof(ResearchCase),
            entityId: researchCase.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.IntelligenceWrite,
            semanticDelta: new { Kind = command.Kind.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return link.Id;
    }

    public async Task HandleAsync(
        UnlinkResearchItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ResearchCase researchCase = await RequireAsync(
            command.OrganizationId, command.ResearchCaseId, cancellationToken)
            .ConfigureAwait(false);

        await _authorization
            .AuthorizeClassifyAsync(
                command.OrganizationId, researchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        // Detaching removes the link and nothing else. The signal, thesis or
        // prediction goes on existing: it was never the case's to delete.
        researchCase.RemoveLink(command.LinkId);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> HandleAsync(
        AddResearchSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ResearchCase researchCase = await RequireAsync(
            command.OrganizationId, command.ResearchCaseId, cancellationToken)
            .ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeClassifyAsync(
                command.OrganizationId, researchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        await RequireSubjectAsync(
            command.OrganizationId,
            new SubjectInput(command.Kind, command.SubjectId, command.Note),
            cancellationToken).ConfigureAwait(false);

        ResearchCaseSubject subject = researchCase.AddSubject(
            command.Kind, command.SubjectId, actor, _clock.UtcNow, command.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return subject.Id;
    }

    /// <summary>Proves the thing being attached exists in this tenant.</summary>
    private async Task RequireLinkedAsync(
        OrganizationId organizationId,
        ResearchLinkKind kind,
        Guid linkedId,
        CancellationToken cancellationToken)
    {
        bool exists = kind switch
        {
            ResearchLinkKind.Source => await _sources
                .AllExistAsync(organizationId, [new IntelligenceSourceId(linkedId)], cancellationToken)
                .ConfigureAwait(false),

            ResearchLinkKind.Signal => await _signals
                .ExistsAsync(organizationId, new SignalId(linkedId), cancellationToken)
                .ConfigureAwait(false),

            ResearchLinkKind.Thesis => await _theses
                .FindAsync(organizationId, new ThesisId(linkedId), cancellationToken)
                .ConfigureAwait(false) is not null,

            ResearchLinkKind.Prediction => await _predictions
                .FindAsync(organizationId, new PredictionId(linkedId), cancellationToken)
                .ConfigureAwait(false) is not null,

            ResearchLinkKind.Task => await _tasks
                .FindAsync(organizationId, new TaskItemId(linkedId), cancellationToken)
                .ConfigureAwait(false) is not null,

            _ => throw new DomainException($"'{kind}' is not something a case links to."),
        };

        if (!exists)
        {
            throw new EntityNotFoundException(kind.ToString(), linkedId.ToString());
        }
    }

    private async Task RequireSubjectAsync(
        OrganizationId organizationId,
        SubjectInput subject,
        CancellationToken cancellationToken)
    {
        if (!await _subjects
            .ExistsAsync(organizationId, subject.Kind, subject.SubjectId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new DomainException(
                $"There is no {subject.Kind} with that identifier in this organization.");
        }
    }

    private async Task<ResearchCase> RequireAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        CancellationToken cancellationToken) =>
        await _cases.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(ResearchCase), id.ToString());
}
