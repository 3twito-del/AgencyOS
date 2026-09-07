using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Deals;

/// <summary>
/// The one implementation of what a caller may see of a negotiation.
/// </summary>
/// <remarks>
/// <para>
/// Two separate rules, applied here and only here. Strategy is redacted the way
/// M4 through M6 redact notes: absent rather than refused, and absent is
/// deliberately indistinguishable from empty, because signalling that something
/// was withheld leaks that there is something to withhold.
/// </para>
/// <para>
/// Economics is the sharper rule and the one M7 adds. Without
/// <c>deals.economics.read</c> every money and percentage term is removed from
/// every read - detail, offers, history, pipeline, command centre and saved views
/// alike. Structural terms survive: an assistant scheduling around a start date
/// has no reason to see the fee, and an agency forced to choose between showing
/// them everything and showing them nothing would show them everything. The
/// catalog guarantees the split is safe by classifying every money-bearing term
/// as economic, and a test enforces that (ADR-0021).
/// </para>
/// <para>
/// Comparison is the deliberate exception. It is refused rather than emptied,
/// because a diff returned with the economic rows stripped would say nothing
/// changed when the number doubled - an absence a reader can misread as a fact.
/// Absent-not-refused works for a field that might legitimately be empty; it does
/// not work for an answer that would be actively false.
/// </para>
/// <para>
/// This lives in one class for the reason M4 learned the hard way: a saved
/// Prospects view ran its projection directly and returned strategy notes the
/// endpoint would have redacted. One implementation, called from every path, is
/// the only arrangement where that class of mistake is a compile-time question
/// (ADR-0017).
/// </para>
/// </remarks>
public sealed class DealRedaction
{
    private readonly TenantGuard _guard;

    public DealRedaction(TenantGuard guard) => _guard = guard;

    /// <summary>Whether the caller may read what deals in this tenant pay.</summary>
    public Task<bool> MayReadEconomicsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.DealEconomicsRead, organizationId, cancellationToken);

    /// <summary>Whether the caller may read the agency's negotiating strategy.</summary>
    public Task<bool> MayReadStrategyAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.DealStrategyRead, organizationId, cancellationToken);

    /// <summary>Refuses the caller outright when they may not read economics.</summary>
    /// <remarks>
    /// Used by comparison alone. Everything else redacts rather than refuses.
    /// </remarks>
    public Task RequireEconomicsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.DealEconomicsRead, organizationId, cancellationToken);

    /// <summary>Applies both rules to a negotiation's full surface.</summary>
    public async Task<DealDetailModel> ApplyAsync(
        OrganizationId organizationId,
        DealDetailModel deal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deal);

        bool economics = await MayReadEconomicsAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);
        bool strategy = await MayReadStrategyAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return Apply(deal, economics, strategy);
    }

    /// <summary>Applies the economics rule to a list of offers with one check.</summary>
    public async Task<IReadOnlyList<OfferModel>> ApplyAsync(
        OrganizationId organizationId,
        IReadOnlyList<OfferModel> offers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offers);

        return await MayReadEconomicsAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? offers
            : [.. offers.Select(Redact)];
    }

    /// <summary>Applies the economics rule to one offer.</summary>
    public async Task<OfferModel?> ApplyAsync(
        OrganizationId organizationId,
        OfferModel? offer,
        CancellationToken cancellationToken = default)
    {
        if (offer is null)
        {
            return null;
        }

        return await MayReadEconomicsAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? offer
            : Redact(offer);
    }

    /// <summary>Applies both rules with the permissions already decided.</summary>
    public static DealDetailModel Apply(DealDetailModel deal, bool economics, bool strategy)
    {
        ArgumentNullException.ThrowIfNull(deal);

        return deal with
        {
            StrategyNotes = strategy ? deal.StrategyNotes : null,
            Offers = economics ? deal.Offers : [.. deal.Offers.Select(Redact)],
            AcceptedOffer = economics ? deal.AcceptedOffer : RedactOrNull(deal.AcceptedOffer),
            OpenOffer = economics ? deal.OpenOffer : RedactOrNull(deal.OpenOffer),
        };
    }

    /// <summary>
    /// Removes the economic terms from an offer.
    /// </summary>
    /// <remarks>
    /// The remaining list is simply shorter. There is no marker, no count of what
    /// was dropped and no flag: the reader sees an offer with structural terms, and
    /// cannot tell whether the economic ones were withheld or never recorded.
    /// </remarks>
    private static OfferModel Redact(OfferModel offer) =>
        offer with { Terms = [.. offer.Terms.Where(term => !term.IsEconomic)] };

    private static OfferModel? RedactOrNull(OfferModel? offer) =>
        offer is null ? null : Redact(offer);
}
