using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Representations;

/// <summary>
/// An area of work a representation covers.
/// </summary>
/// <remarks>
/// <para>
/// A controlled vocabulary that can grow, rather than free text or a full
/// ontology. Free text would make "TV", "Television" and "telly" three different
/// scopes and quietly break every filter; an ontology of sub-genres would be
/// speculation, since nothing in AgencyOS yet distinguishes them.
/// </para>
/// <para>
/// Representation is deliberately not hard-coded as "all entertainment": an agency
/// may represent someone for television and not for their books, and that
/// distinction is the whole point of recording scope at all.
/// </para>
/// </remarks>
public enum RepresentationScopeArea
{
    Film = 1,
    Television = 2,
    Literary = 3,
    Directing = 4,
    Acting = 5,
    Producing = 6,
    Digital = 7,
    Brand = 8,
    Speaking = 9,
    Music = 10,
    Books = 11,
    Theatre = 12,
    Other = 99,
}

/// <summary>
/// One area a representation covers, over a period.
/// </summary>
/// <remarks>
/// Effective-dated rather than a simple set, because scope changes are meaningful
/// history: "we picked up their literary representation in 2027" is a fact worth
/// keeping, and a set would lose it the moment the row was rewritten. Ending a
/// scope closes the row; nothing is deleted.
/// </remarks>
public sealed class RepresentationScope
{
    private RepresentationScope()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public RepresentationId RepresentationId { get; private set; }

    public RepresentationScopeArea Area { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>When the agency stopped representing this area, or null while it still does.</summary>
    public DateOnly? EndsOn { get; private set; }

    public bool IsOpen => EndsOn is null;

    internal static RepresentationScope Open(
        OrganizationId organizationId,
        RepresentationId representationId,
        RepresentationScopeArea area,
        DateOnly startsOn)
    {
        return new RepresentationScope
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            RepresentationId = representationId,
            Area = area,
            StartsOn = startsOn,
        };
    }

    internal void End(DateOnly endsOn)
    {
        if (endsOn < StartsOn)
        {
            throw new DomainException(
                $"A representation scope cannot end on {endsOn:O} when it started on {StartsOn:O}.");
        }

        EndsOn = endsOn;
    }
}

/// <summary>
/// What somebody internal does on a representation team.
/// </summary>
/// <remarks>
/// Exactly one open <see cref="Lead"/> per representation, enforced in the domain
/// and again by a partial unique index. "Who owns this relationship" must have one
/// answer.
/// </remarks>
public enum RepresentationTeamRole
{
    /// <summary>The agent who owns the relationship. At most one at a time.</summary>
    Lead = 1,

    /// <summary>An agent working the relationship alongside the lead.</summary>
    Agent = 2,

    /// <summary>Coordinates the work without owning the relationship.</summary>
    Coordinator = 3,

    Assistant = 4,
}

/// <summary>
/// Somebody internal assigned to a representation, over a period.
/// </summary>
/// <remarks>
/// <para>
/// References a <see cref="UserId"/> - an AgencyOS identity - and never a
/// <c>Person</c>. A person is somebody the agency has a record about; a user is
/// somebody who works here. Conflating them is how an external contact ends up
/// with an internal role.
/// </para>
/// <para>
/// The database enforces that the row belongs to the tenant and points at a real
/// user; that the user is a <em>member</em> of that tenant is checked in the
/// application. It is deliberately not a foreign key: membership is temporal, and
/// a key would forbid keeping the team record after somebody's membership was
/// revoked - which is exactly the history this table exists to keep.
/// </para>
/// </remarks>
public sealed class RepresentationTeamMember
{
    private RepresentationTeamMember()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public RepresentationId RepresentationId { get; private set; }

    /// <summary>The internal AgencyOS user, never an external person.</summary>
    public UserId UserId { get; private set; }

    public RepresentationTeamRole Role { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    public bool IsOpen => EndsOn is null;

    internal static RepresentationTeamMember Open(
        OrganizationId organizationId,
        RepresentationId representationId,
        UserId userId,
        RepresentationTeamRole role,
        DateOnly startsOn)
    {
        return new RepresentationTeamMember
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            RepresentationId = representationId,
            UserId = userId,
            Role = role,
            StartsOn = startsOn,
        };
    }

    internal void End(DateOnly endsOn)
    {
        if (endsOn < StartsOn)
        {
            throw new DomainException(
                $"A team assignment cannot end on {endsOn:O} when it started on {StartsOn:O}.");
        }

        EndsOn = endsOn;
    }
}
