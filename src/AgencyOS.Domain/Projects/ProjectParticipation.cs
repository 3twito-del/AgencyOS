using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Domain.Projects;

/// <summary>
/// The capacity in which a company is involved in a project.
/// </summary>
/// <remarks>
/// Structural rather than creative. These are the positions a company occupies in
/// how a project is made and sold, not jobs anybody is cast or hired into.
/// </remarks>
public enum ProjectCompanyCapacity
{
    Studio = 1,
    Network = 2,
    Streamer = 3,
    ProductionCompany = 4,
    Financier = 5,
    Distributor = 6,
    SalesCompany = 7,
    Other = 99,
}

/// <summary>
/// A company's involvement in a project, in a structural capacity.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an <see cref="Attachment"/>. A studio is not a position
/// somebody fills: there is no role that could be open or unfilled, so expressing
/// it as an attachment would mean inventing a fake <see cref="ProjectRole"/> on
/// every project purely to hang the studio off. Worse, one company can be the
/// producer - a real creative role, through an attachment - <em>and</em> the
/// studio on the same project, and a single table would have to collapse those
/// into one nullable-role shape (ADR-0019).
/// </para>
/// <para>
/// Effective-dated, because financiers and distributors come and go and the fact
/// that one was involved does not stop being true when they leave.
/// </para>
/// <para>
/// This records known participation. It says nothing about an active sales process
/// - buyers, pitches and submissions are M6.
/// </para>
/// </remarks>
public sealed class ProjectCompanyParticipation
{
    private ProjectCompanyParticipation()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public CompanyId CompanyId { get; private set; }

    public ProjectCompanyCapacity Capacity { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets a value indicating whether the involvement still holds.</summary>
    public bool IsOpen => EndsOn is null;

    internal static ProjectCompanyParticipation Create(
        OrganizationId organizationId,
        ProjectId projectId,
        CompanyId companyId,
        ProjectCompanyCapacity capacity,
        DateOnly startsOn,
        DateTimeOffset now,
        DateOnly? endsOn,
        string? notes)
    {
        if (!Enum.IsDefined(capacity))
        {
            throw new DomainException($"Unknown company capacity '{capacity}'.");
        }

        if (endsOn is { } end && end < startsOn)
        {
            throw new DomainException(
                $"Company participation cannot end on {end:yyyy-MM-dd}, before it starts on "
                    + $"{startsOn:yyyy-MM-dd}.");
        }

        return new ProjectCompanyParticipation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            CompanyId = companyId,
            Capacity = capacity,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            CreatedAt = now,
        };
    }

    internal void End(DateOnly endsOn)
    {
        if (endsOn < StartsOn)
        {
            throw new DomainException(
                $"Company participation cannot end on {endsOn:yyyy-MM-dd}, before it started on "
                    + $"{StartsOn:yyyy-MM-dd}.");
        }

        EndsOn = endsOn;
    }
}

/// <summary>
/// A link between a project and a source property it derives from.
/// </summary>
/// <remarks>
/// Many-to-many on purpose: one book can spawn a film and a series, and one
/// project can draw on a book and an article at once. A column on either side
/// would force a choice that the domain does not actually make.
/// </remarks>
public sealed class ProjectSourceProperty
{
    private ProjectSourceProperty()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    public SourcePropertyId SourcePropertyId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public UserId LinkedBy { get; private set; }

    internal static ProjectSourceProperty Create(
        OrganizationId organizationId,
        ProjectId projectId,
        SourcePropertyId sourcePropertyId,
        DateTimeOffset now,
        UserId linkedBy,
        string? notes)
    {
        return new ProjectSourceProperty
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            SourcePropertyId = sourcePropertyId,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            LinkedAt = now,
            LinkedBy = linkedBy,
        };
    }
}

/// <summary>
/// A link between a project and a piece of material.
/// </summary>
/// <remarks>
/// <para>
/// A join rather than a <c>ProjectId</c> column on <c>Material</c>, for two
/// reasons. A material can serve more than one project - a lookbook reused across
/// a slate - and a column would force a choice. More importantly, a column would
/// express a project fact by mutating the talent's own material record, so linking
/// a screenplay to a project would bump the version of something owned by the
/// writer (ADR-0019).
/// </para>
/// <para>
/// Still metadata only. M4's material record does not hold the file and neither
/// does this; document storage is M10.
/// </para>
/// </remarks>
public sealed class ProjectMaterialLink
{
    private ProjectMaterialLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProjectId ProjectId { get; private set; }

    /// <summary>The M4 material this project uses.</summary>
    public MaterialId MaterialId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public UserId LinkedBy { get; private set; }

    internal static ProjectMaterialLink Create(
        OrganizationId organizationId,
        ProjectId projectId,
        MaterialId materialId,
        DateTimeOffset now,
        UserId linkedBy,
        string? notes)
    {
        return new ProjectMaterialLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            MaterialId = materialId,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            LinkedAt = now,
            LinkedBy = linkedBy,
        };
    }
}
