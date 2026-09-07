using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Domain.Opportunities;

/// <summary>What kind of record an opportunity subject points at.</summary>
public enum OpportunitySubjectKind
{
    TalentProfile = 1,
    Project = 2,
    Package = 3,
    ProjectRole = 4,
}

/// <summary>What a subject is doing in the pursuit.</summary>
/// <remarks>
/// The same project can be the thing being sold in one opportunity and the context
/// around a client placement in another. Without this the two records would look
/// identical and mean different things.
/// </remarks>
public enum OpportunitySubjectRole
{
    /// <summary>The thing being pursued.</summary>
    Primary = 1,

    /// <summary>What the pursuit is attached to or set within.</summary>
    Context = 2,

    /// <summary>Supporting material or a secondary element.</summary>
    Supporting = 3,
}

/// <summary>
/// A typed reference to one thing an opportunity is about.
/// </summary>
/// <remarks>
/// <para>
/// A choice of exactly one typed identifier, never a kind plus a raw
/// <see cref="Guid"/>. M5's package elements took the raw-identifier route
/// deliberately and paid for it with a validator that has to be remembered; here
/// the domain can express the choice and the database can enforce it with real
/// foreign keys, so it does (ADR-0020).
/// </para>
/// </remarks>
public readonly record struct OpportunitySubjectRef
{
    private OpportunitySubjectRef(
        OpportunitySubjectKind kind,
        TalentProfileId? talentProfileId,
        ProjectId? projectId,
        PackageId? packageId,
        ProjectRoleId? projectRoleId)
    {
        Kind = kind;
        TalentProfileId = talentProfileId;
        ProjectId = projectId;
        PackageId = packageId;
        ProjectRoleId = projectRoleId;
    }

    public OpportunitySubjectKind Kind { get; }

    public TalentProfileId? TalentProfileId { get; }

    public ProjectId? ProjectId { get; }

    public PackageId? PackageId { get; }

    public ProjectRoleId? ProjectRoleId { get; }

    /// <summary>Gets a value indicating whether this reference names anything at all.</summary>
    public bool IsSpecified =>
        TalentProfileId is not null
        || ProjectId is not null
        || PackageId is not null
        || ProjectRoleId is not null;

    public static OpportunitySubjectRef Talent(TalentProfileId id) =>
        new(OpportunitySubjectKind.TalentProfile, id, null, null, null);

    public static OpportunitySubjectRef Project(ProjectId id) =>
        new(OpportunitySubjectKind.Project, null, id, null, null);

    public static OpportunitySubjectRef Package(PackageId id) =>
        new(OpportunitySubjectKind.Package, null, null, id, null);

    public static OpportunitySubjectRef ProjectRole(ProjectRoleId id) =>
        new(OpportunitySubjectKind.ProjectRole, null, null, null, id);

    /// <summary>The identifier, whichever one is set.</summary>
    public Guid TargetId =>
        TalentProfileId?.Value
        ?? ProjectId?.Value
        ?? PackageId?.Value
        ?? ProjectRoleId?.Value
        ?? Guid.Empty;

    public override string ToString() => $"{Kind}:{TargetId}";
}

/// <summary>
/// One thing an opportunity is about.
/// </summary>
/// <remarks>
/// Several are allowed, because pursuits genuinely span records: taking a package
/// to market is about the package and the project it is built around, and placing a
/// client is about the client and often a specific role. Which combinations are
/// legal is decided by the opportunity's kind, and checked when it is taken out
/// (ADR-0020).
/// </remarks>
public sealed class OpportunitySubject
{
    private OpportunitySubject()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    public OpportunitySubjectKind Kind { get; private set; }

    public OpportunitySubjectRole Role { get; private set; }

    public TalentProfileId? TalentProfileId { get; private set; }

    public ProjectId? ProjectId { get; private set; }

    public PackageId? PackageId { get; private set; }

    public ProjectRoleId? ProjectRoleId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>The identifier this subject points at, whichever kind it is.</summary>
    public Guid TargetId =>
        TalentProfileId?.Value
        ?? ProjectId?.Value
        ?? PackageId?.Value
        ?? ProjectRoleId?.Value
        ?? Guid.Empty;

    /// <summary>The reference, reassembled from whichever column is set.</summary>
    public OpportunitySubjectRef Reference => Kind switch
    {
        OpportunitySubjectKind.TalentProfile => OpportunitySubjectRef.Talent(TalentProfileId!.Value),
        OpportunitySubjectKind.Project => OpportunitySubjectRef.Project(ProjectId!.Value),
        OpportunitySubjectKind.Package => OpportunitySubjectRef.Package(PackageId!.Value),
        _ => OpportunitySubjectRef.ProjectRole(ProjectRoleId!.Value),
    };

    internal static OpportunitySubject Create(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunitySubjectRef subject,
        OpportunitySubjectRole role,
        DateTimeOffset now,
        string? note)
    {
        if (!subject.IsSpecified)
        {
            throw new DomainException("An opportunity subject must name something.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"Unknown opportunity subject role '{role}'.");
        }

        return new OpportunitySubject
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            Kind = subject.Kind,
            Role = role,
            TalentProfileId = subject.TalentProfileId,
            ProjectId = subject.ProjectId,
            PackageId = subject.PackageId,
            ProjectRoleId = subject.ProjectRoleId,
            Note = Ensure.OptionalMax(note, nameof(note), 1000),
            AddedAt = now,
        };
    }

    internal bool Matches(OpportunitySubjectRef subject) =>
        Kind == subject.Kind && TargetId == subject.TargetId;
}
