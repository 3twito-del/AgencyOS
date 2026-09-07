using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M7 deal model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, as M2 through M6 do: a method callable
/// without one is a cross-tenant read waiting to be written. The database enforces
/// the same containment through composite foreign keys (ADR-0011).
/// </remarks>
public interface IDealRepository
{
    Task<Deal?> FindAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a live negotiation with the same target for the same kind of
    /// transaction.
    /// </summary>
    /// <remarks>
    /// Keyed on the kind as well as the target, because one buyer can genuinely be
    /// negotiating a project sale and a producing deal at the same time. Two live
    /// deals of the <em>same</em> kind means two colleagues working the same thing
    /// without knowing about each other, which is what this prevents. A partial
    /// unique index enforces the same rule (ADR-0021).
    /// </remarks>
    Task<Deal?> FindLiveForTargetAsync(
        OrganizationId organizationId,
        Domain.Opportunities.OpportunityTargetId targetId,
        DealKind kind,
        CancellationToken cancellationToken = default);

    void Add(Deal deal);
}

public interface IOfferRepository
{
    /// <summary>Loads an offer with the terms its commands reason about.</summary>
    Task<Offer?> FindAsync(
        OrganizationId organizationId,
        OfferId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every offer in a negotiation, so the chain can be validated as a whole.
    /// </summary>
    /// <remarks>
    /// Chain rules are about the thread rather than about one offer, and checking
    /// them against a partial view would let a second standing offer through
    /// whenever the two were written concurrently.
    /// </remarks>
    Task<IReadOnlyList<Offer>> ListForDealAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken = default);

    void Add(Offer offer);
}

/// <summary>Links ordinary tasks to the negotiation they belong to.</summary>
/// <remarks>
/// A join rather than another nullable identifier on the task row, on the M6
/// precedent: every milestone that adds a linkable thing would otherwise widen
/// <c>TaskItem</c> again and add a combination nothing validates.
/// </remarks>
public interface IDealTaskLinkRepository
{
    void Add(DealTaskLink link);
}
