using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Loads and stores negotiations.</summary>
/// <remarks>
/// Every query filters on the tenant as well as the identifier. The database
/// enforces the same containment through composite foreign keys, and this makes a
/// cross-tenant read impossible to write by accident rather than merely refused
/// after the fact (ADR-0011).
/// </remarks>
public sealed class DealRepository : IDealRepository
{
    private readonly AgencyOsDbContext _context;

    public DealRepository(AgencyOsDbContext context) => _context = context;

    public Task<Deal?> FindAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default) =>
        _context.Deals
            .Include(deal => deal.Events)
            .FirstOrDefaultAsync(
                deal => deal.OrganizationId == organizationId && deal.Id == id,
                cancellationToken);

    public async Task<Deal?> FindLiveForTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId targetId,
        DealKind kind,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the partial unique index exactly. Checking here gives the user a
        // sentence they can act on; the index is what settles a race between two
        // transactions that both passed this check.
        DealStatus[] live = [.. Deal.LiveStatuses];

        return await _context.Deals
            .FirstOrDefaultAsync(
                deal => deal.OrganizationId == organizationId
                    && deal.OpportunityTargetId == targetId
                    && deal.Kind == kind
                    && live.Contains(deal.Status),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Deal deal) => _context.Deals.Add(deal);
}

/// <summary>Loads and stores offers, with the terms their commands reason about.</summary>
public sealed class OfferRepository : IOfferRepository
{
    private readonly AgencyOsDbContext _context;

    public OfferRepository(AgencyOsDbContext context) => _context = context;

    public Task<Offer?> FindAsync(
        OrganizationId organizationId,
        OfferId id,
        CancellationToken cancellationToken = default) =>
        _context.Offers
            .Include(offer => offer.Terms)
            .Include(offer => offer.Events)
            .FirstOrDefaultAsync(
                offer => offer.OrganizationId == organizationId && offer.Id == id,
                cancellationToken);

    /// <summary>
    /// The whole thread, ordered, so the chain rules see all of it.
    /// </summary>
    /// <remarks>
    /// Terms are deliberately not loaded. Chain validity is about lineage and
    /// status, and pulling every term of every historical offer to answer a
    /// question that does not involve them would be work nothing needs.
    /// </remarks>
    public async Task<IReadOnlyList<Offer>> ListForDealAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken = default)
    {
        List<Offer> offers = await _context.Offers
            .Where(offer => offer.OrganizationId == organizationId && offer.DealId == dealId)
            .OrderBy(offer => offer.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return offers;
    }

    public void Add(Offer offer) => _context.Offers.Add(offer);
}

/// <summary>Records which negotiation a task belongs to.</summary>
public sealed class DealTaskLinkRepository : IDealTaskLinkRepository
{
    private readonly AgencyOsDbContext _context;

    public DealTaskLinkRepository(AgencyOsDbContext context) => _context = context;

    public void Add(DealTaskLink link) => _context.DealTaskLinks.Add(link);
}
