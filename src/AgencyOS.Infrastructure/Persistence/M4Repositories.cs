using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <remarks>
/// Every query filters on the tenant as well as the identifier. The predicate is
/// not redundant with the composite foreign keys: those stop bad data being
/// written, this stops good data being read by the wrong tenant.
/// </remarks>
internal sealed class TalentProfileRepository : ITalentProfileRepository
{
    private readonly AgencyOsDbContext _context;

    public TalentProfileRepository(AgencyOsDbContext context) => _context = context;

    public Task<TalentProfile?> FindAsync(
        OrganizationId organizationId,
        TalentProfileId id,
        CancellationToken cancellationToken = default)
    {
        return _context.TalentProfiles
            .Include(x => x.Disciplines)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public Task<TalentProfile?> FindByPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        return _context.TalentProfiles
            .Include(x => x.Disciplines)
            .FirstOrDefaultAsync(
                x => x.PersonId == personId && x.OrganizationId == organizationId,
                cancellationToken);
    }

    public void Add(TalentProfile profile) => _context.TalentProfiles.Add(profile);
}

internal sealed class ProspectRepository : IProspectRepository
{
    /// <summary>Stages a pursuit can still move from, as integers for the query.</summary>
    private static readonly ProspectStage[] Open =
    [
        ProspectStage.Identified,
        ProspectStage.Contacted,
        ProspectStage.Courting,
    ];

    private readonly AgencyOsDbContext _context;

    public ProspectRepository(AgencyOsDbContext context) => _context = context;

    public Task<Prospect?> FindAsync(
        OrganizationId organizationId,
        ProspectId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Prospects
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public Task<Prospect?> FindOpenForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        return _context.Prospects
            .Include(x => x.Events)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.PersonId == personId
                    && Open.Contains(x.Stage),
                cancellationToken);
    }

    public void Add(Prospect prospect) => _context.Prospects.Add(prospect);
}

internal sealed class RepresentationRepository : IRepresentationRepository
{
    /// <summary>Statuses from which a representation can still change.</summary>
    private static readonly RepresentationStatus[] NonTerminal =
    [
        RepresentationStatus.Pending,
        RepresentationStatus.Active,
        RepresentationStatus.Suspended,
    ];

    private readonly AgencyOsDbContext _context;

    public RepresentationRepository(AgencyOsDbContext context) => _context = context;

    public Task<Representation?> FindAsync(
        OrganizationId organizationId,
        RepresentationId id,
        CancellationToken cancellationToken = default)
    {
        return Loaded().FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public Task<Representation?> FindNonTerminalForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        return Loaded().FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.PersonId == personId
                && NonTerminal.Contains(x.Status),
            cancellationToken);
    }

    public void Add(Representation representation) => _context.Representations.Add(representation);

    /// <summary>
    /// Loads the aggregate whole.
    /// </summary>
    /// <remarks>
    /// Scopes and team are part of the aggregate's invariants - one open lead, no
    /// duplicate open scope - so a command that loaded the root alone could not
    /// enforce them.
    /// </remarks>
    private IQueryable<Representation> Loaded() => _context.Representations
        .Include(x => x.Events)
        .Include(x => x.Scopes)
        .Include(x => x.Team);
}

internal sealed class CreditRepository : ICreditRepository
{
    private readonly AgencyOsDbContext _context;

    public CreditRepository(AgencyOsDbContext context) => _context = context;

    public Task<Credit?> FindAsync(
        OrganizationId organizationId,
        CreditId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Credits.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public void Add(Credit credit) => _context.Credits.Add(credit);
}

internal sealed class MaterialRepository : IMaterialRepository
{
    private readonly AgencyOsDbContext _context;

    public MaterialRepository(AgencyOsDbContext context) => _context = context;

    public Task<Material?> FindAsync(
        OrganizationId organizationId,
        MaterialId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Materials.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public void Add(Material material) => _context.Materials.Add(material);
}
