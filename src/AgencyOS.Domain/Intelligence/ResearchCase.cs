using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for a <see cref="ResearchCase"/>.</summary>
public readonly record struct ResearchCaseId(Guid Value)
{
    public static ResearchCaseId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Where a research case stands.</summary>
public enum ResearchCaseStatus
{
    Open = 1,

    /// <summary>Set down for now, expected to resume.</summary>
    Paused = 2,

    /// <summary>The question was answered. What was learned lives in the linked objects.</summary>
    Completed = 3,

    /// <summary>Abandoned. The question stopped mattering.</summary>
    Cancelled = 4,
}

/// <summary>What a research case has attached to it.</summary>
public enum ResearchLinkKind
{
    Source = 1,
    Signal = 2,
    Thesis = 3,
    Prediction = 4,
    Task = 5,
}

/// <summary>
/// An open question somebody is investigating.
/// </summary>
/// <remarks>
/// <para>
/// A workspace, not a knowledge store. This is the distinction that matters most
/// for what M12 will later be able to do: a case organizes evidence and conclusions
/// without holding any, so an agent asking "what do we know about X" reads
/// structured signals and theses rather than parsing somebody's notes
/// (ADR-0030).
/// </para>
/// <para>
/// There is deliberately no <c>ResearchFinding</c>. A finding of fact is a
/// <see cref="Signal"/>; an interpretation is a <see cref="Thesis"/>; a forecast is
/// a <see cref="Prediction"/>. A fourth type holding the same content would be a
/// second copy with none of their semantics — no provenance requirement, no
/// stance, no revision history.
/// </para>
/// <para>
/// It is also not a project manager. Tasks remain M2 tasks and are linked, not
/// reimplemented.
/// </para>
/// </remarks>
public sealed class ResearchCase
{
    private readonly List<ResearchCaseSubject> _subjects = [];
    private readonly List<ResearchCaseLink> _links = [];

    private ResearchCase()
    {
    }

    public ResearchCaseId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The question, phrased as one.</summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>Why it is being asked, and what an answer would change.</summary>
    public string? Context { get; private set; }

    public ResearchCaseStatus Status { get; private set; }

    public UserId OwnerUserId { get; private set; }

    public IntelligenceSensitivity Sensitivity { get; private set; }

    /// <summary>What was concluded, recorded when the case is completed.</summary>
    /// <remarks>
    /// A summary for a reader, not a place to put facts. The facts are the linked
    /// signals and theses, which is where anything reusable belongs.
    /// </remarks>
    public string? Conclusion { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<ResearchCaseSubject> Subjects => _subjects;

    public IReadOnlyList<ResearchCaseLink> Links => _links;

    public bool IsOpen => Status is ResearchCaseStatus.Open or ResearchCaseStatus.Paused;

    public static ResearchCase Open(
        OrganizationId organizationId,
        string question,
        IntelligenceSensitivity sensitivity,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset now,
        string? context = null)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        return new ResearchCase
        {
            Id = ResearchCaseId.New(),
            OrganizationId = organizationId,
            Question = Ensure.NotBlankMax(question, nameof(question), 500),
            Context = Ensure.OptionalMax(context, nameof(context), 8000),
            Status = ResearchCaseStatus.Open,
            OwnerUserId = ownerUserId,
            Sensitivity = sensitivity,
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        string question,
        IntelligenceSensitivity sensitivity,
        int expectedVersion,
        string? context = null)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Question = Ensure.NotBlankMax(question, nameof(question), 500);
        Context = Ensure.OptionalMax(context, nameof(context), 8000);
        Sensitivity = Enum.IsDefined(sensitivity)
            ? sensitivity
            : throw new DomainException($"'{sensitivity}' is not a sensitivity.");

        Version++;
    }

    public void Pause(DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != ResearchCaseStatus.Open)
        {
            throw new DomainException($"A {Status.ToString().ToLowerInvariant()} case is not paused.");
        }

        Status = ResearchCaseStatus.Paused;
        Version++;
    }

    public void Resume(DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != ResearchCaseStatus.Paused)
        {
            throw new DomainException("Only a paused case is resumed.");
        }

        Status = ResearchCaseStatus.Open;
        Version++;
    }

    public void Complete(string conclusion, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Status = ResearchCaseStatus.Completed;
        Conclusion = Ensure.NotBlankMax(conclusion, nameof(conclusion), 8000);
        ClosedAt = now;
        Version++;
    }

    public void Cancel(string reason, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        Status = ResearchCaseStatus.Cancelled;
        Conclusion = Ensure.NotBlankMax(reason, nameof(reason), 8000);
        ClosedAt = now;
        Version++;
    }

    public ResearchCaseSubject AddSubject(
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_subjects.Any(x => x.Kind == kind && x.SubjectId == subjectId))
        {
            throw new DomainException("That subject is already on this case.");
        }

        ResearchCaseSubject subject = ResearchCaseSubject.Create(
            OrganizationId, Id, kind, subjectId, addedBy, now, note);

        _subjects.Add(subject);

        return subject;
    }

    public void RemoveSubject(Guid subjectRowId)
    {
        ResearchCaseSubject subject = _subjects.SingleOrDefault(x => x.Id == subjectRowId)
            ?? throw new DomainException("That subject is not on this case.");

        _subjects.Remove(subject);
    }

    /// <summary>Attaches an existing intelligence object or task to the case.</summary>
    public ResearchCaseLink AddLink(
        ResearchLinkKind kind,
        Guid linkedId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not something a case links to.");
        }

        if (linkedId == Guid.Empty)
        {
            throw new DomainException("A link must name what it points at.");
        }

        if (_links.Any(x => x.Kind == kind && x.LinkedId == linkedId))
        {
            throw new DomainException("That is already linked to this case.");
        }

        ResearchCaseLink link = ResearchCaseLink.Create(
            OrganizationId, Id, kind, linkedId, addedBy, now, note);

        _links.Add(link);

        return link;
    }

    public void RemoveLink(Guid linkId)
    {
        ResearchCaseLink link = _links.SingleOrDefault(x => x.Id == linkId)
            ?? throw new DomainException("That is not linked to this case.");

        _links.Remove(link);
    }

    private void RequireOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException(
                $"That case is {Status.ToString().ToLowerInvariant()}. Open a new one rather "
                    + "than reworking a closed question.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(ResearchCase), Id.ToString(), expectedVersion, Version);
        }
    }
}

/// <summary>
/// Something attached to a research case.
/// </summary>
/// <remarks>
/// One discriminator and exactly one typed identifier, checked by the database, on
/// the same pattern as every other link in the system since M10. Five arcs rather
/// than fourteen, because a case only ever attaches to things AgencyOS's
/// intelligence layer or task list already owns (ADR-0025, ADR-0030).
/// </remarks>
public sealed class ResearchCaseLink
{
    private ResearchCaseLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ResearchCaseId ResearchCaseId { get; private set; }

    public ResearchLinkKind Kind { get; private set; }

    /// <summary>
    /// What it points at.
    /// </summary>
    /// <remarks>
    /// Persisted into the typed column matching <see cref="Kind"/>, which carries
    /// the composite tenant foreign key.
    /// </remarks>
    public Guid LinkedId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    internal static ResearchCaseLink Create(
        OrganizationId organizationId,
        ResearchCaseId researchCaseId,
        ResearchLinkKind kind,
        Guid linkedId,
        UserId addedBy,
        DateTimeOffset now,
        string? note) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ResearchCaseId = researchCaseId,
            Kind = kind,
            LinkedId = linkedId,
            Note = Ensure.OptionalMax(note, nameof(note), 500),
            AddedAt = now,
            AddedBy = addedBy,
        };
}
