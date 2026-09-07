using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Loads and stores contracts.</summary>
/// <remarks>
/// Every query filters on the tenant as well as the identifier, exactly as M2
/// through M7 do. The database enforces the same containment through composite
/// foreign keys, and this makes a cross-tenant read impossible to write by
/// accident rather than merely refused after the fact (ADR-0011).
/// </remarks>
public sealed class ContractRepository : IContractRepository
{
    private readonly AgencyOsDbContext _context;

    public ContractRepository(AgencyOsDbContext context) => _context = context;

    /// <summary>
    /// Loads a contract with the parties and signatures its commands reason about.
    /// </summary>
    /// <remarks>
    /// Parties and signatures are always needed: outstanding signatories are
    /// derived from both, and every command that names a party checks it is on
    /// this paper. Versions and terms are separate aggregates and are not pulled
    /// in, because recording a signature has no business reading a draft.
    /// </remarks>
    public Task<Contract?> FindAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default) =>
        _context.Contracts
            .Include(contract => contract.Parties)
            .Include(contract => contract.Signatures)
            .Include(contract => contract.Events)
            .FirstOrDefaultAsync(
                contract => contract.OrganizationId == organizationId && contract.Id == id,
                cancellationToken);

    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default) =>
        _context.Contracts.AnyAsync(
            contract => contract.OrganizationId == organizationId && contract.Id == id,
            cancellationToken);

    public void Add(Contract contract) => _context.Contracts.Add(contract);

    public void AddRelationship(ContractRelationship relationship) =>
        _context.ContractRelationships.Add(relationship);
}

/// <summary>Loads and stores drafting versions and the terms transcribed from them.</summary>
public sealed class ContractVersionRepository : IContractVersionRepository
{
    private readonly AgencyOsDbContext _context;

    public ContractVersionRepository(AgencyOsDbContext context) => _context = context;

    public Task<ContractVersion?> FindAsync(
        OrganizationId organizationId,
        ContractVersionId id,
        CancellationToken cancellationToken = default) =>
        _context.ContractVersions
            .Include(version => version.Terms)
            .FirstOrDefaultAsync(
                version => version.OrganizationId == organizationId && version.Id == id,
                cancellationToken);

    /// <summary>
    /// Every version of a contract, in drafting order.
    /// </summary>
    /// <remarks>
    /// Terms are deliberately not loaded. The sequence questions - what number is
    /// next, which version the new one supersedes - are about position and status,
    /// and pulling every term of every historical draft to answer them would be
    /// work nothing needs (the M7 offer-thread precedent).
    /// </remarks>
    public async Task<IReadOnlyList<ContractVersion>> ListForContractAsync(
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken = default)
    {
        List<ContractVersion> versions = await _context.ContractVersions
            .Where(version =>
                version.OrganizationId == organizationId && version.ContractId == contractId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions;
    }

    public void Add(ContractVersion version) => _context.ContractVersions.Add(version);
}

/// <summary>Loads and stores what contracts record as granted.</summary>
public sealed class RightsGrantRepository : IRightsGrantRepository
{
    private readonly AgencyOsDbContext _context;

    public RightsGrantRepository(AgencyOsDbContext context) => _context = context;

    public Task<RightsGrant?> FindAsync(
        OrganizationId organizationId,
        RightsGrantId id,
        CancellationToken cancellationToken = default) =>
        _context.RightsGrants.FirstOrDefaultAsync(
            grant => grant.OrganizationId == organizationId && grant.Id == id,
            cancellationToken);

    public void Add(RightsGrant grant) => _context.RightsGrants.Add(grant);
}

/// <summary>Loads and stores the elections a contract creates.</summary>
public sealed class ContractOptionRepository : IContractOptionRepository
{
    private readonly AgencyOsDbContext _context;

    public ContractOptionRepository(AgencyOsDbContext context) => _context = context;

    public Task<ContractOption?> FindAsync(
        OrganizationId organizationId,
        ContractOptionId id,
        CancellationToken cancellationToken = default) =>
        _context.ContractOptions
            .Include(option => option.Events)
            .FirstOrDefaultAsync(
                option => option.OrganizationId == organizationId && option.Id == id,
                cancellationToken);

    public void Add(ContractOption option) => _context.ContractOptions.Add(option);
}

/// <summary>Loads and stores what the parties have to do.</summary>
public sealed class ObligationRepository : IObligationRepository
{
    private readonly AgencyOsDbContext _context;

    public ObligationRepository(AgencyOsDbContext context) => _context = context;

    public Task<Obligation?> FindAsync(
        OrganizationId organizationId,
        ObligationId id,
        CancellationToken cancellationToken = default) =>
        _context.Obligations
            .Include(obligation => obligation.Events)
            .FirstOrDefaultAsync(
                obligation => obligation.OrganizationId == organizationId && obligation.Id == id,
                cancellationToken);

    public void Add(Obligation obligation) => _context.Obligations.Add(obligation);
}

/// <summary>Loads notice requirements and stores the notices recorded against them.</summary>
public sealed class NoticeRepository : INoticeRepository
{
    private readonly AgencyOsDbContext _context;

    public NoticeRepository(AgencyOsDbContext context) => _context = context;

    public Task<NoticeRequirement?> FindRequirementAsync(
        OrganizationId organizationId,
        NoticeRequirementId id,
        CancellationToken cancellationToken = default) =>
        _context.NoticeRequirements.FirstOrDefaultAsync(
            requirement => requirement.OrganizationId == organizationId && requirement.Id == id,
            cancellationToken);

    public void AddRequirement(NoticeRequirement requirement) =>
        _context.NoticeRequirements.Add(requirement);

    public void AddRecord(NoticeRecord record) => _context.NoticeRecords.Add(record);
}

/// <summary>Records which piece of contract work a task manages.</summary>
public sealed class ContractTaskLinkRepository : IContractTaskLinkRepository
{
    private readonly AgencyOsDbContext _context;

    public ContractTaskLinkRepository(AgencyOsDbContext context) => _context = context;

    public void Add(ContractTaskLink link) => _context.ContractTaskLinks.Add(link);
}
