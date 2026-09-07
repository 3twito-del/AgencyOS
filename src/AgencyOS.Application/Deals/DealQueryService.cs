using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Deals;

/// <summary>
/// Authorizes every read of the deal model, then redacts what the caller may not
/// see.
/// </summary>
/// <remarks>
/// Reads need tenant scoping for the same reason writes do: an endpoint policy
/// asks only whether the caller holds a permission somewhere, so without a scoped
/// check a user with <c>deals.read</c> in one tenant could read another's
/// negotiations. Redaction goes through <see cref="DealRedaction"/> rather than
/// happening here, so saved views, search and the command centre apply the
/// identical rule (ADR-0017, ADR-0021).
/// </remarks>
public sealed class DealQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;
    private const int HistoryLimit = 200;

    private readonly IDealQueries _queries;
    private readonly TenantGuard _guard;
    private readonly DealRedaction _redaction;
    private readonly IClock _clock;

    public DealQueryService(
        IDealQueries queries,
        TenantGuard guard,
        DealRedaction redaction,
        IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _redaction = redaction;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DealSummaryModel>> ListDealsAsync(
        OrganizationId organizationId,
        DealFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.DealsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        // Summaries carry no term values, so there is nothing economic to redact
        // here. Every figure on them is a count or a date.
        return await _queries
            .ListDealsAsync(organizationId, filter ?? new DealFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DealDetailModel?> GetDealAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.DealsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        DealDetailModel? deal = await _queries
            .GetDealAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return deal is null
            ? null
            : await _redaction.ApplyAsync(organizationId, deal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OfferModel>> ListOffersAsync(
        OrganizationId organizationId,
        DealId? dealId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OffersRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OfferModel> offers = await _queries
            .ListOffersAsync(organizationId, dealId, cancellationToken)
            .ConfigureAwait(false);

        return await _redaction.ApplyAsync(organizationId, offers, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OfferModel?> GetOfferAsync(
        OrganizationId organizationId,
        OfferId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OffersRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        OfferModel? offer = await _queries
            .GetOfferAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return await _redaction.ApplyAsync(organizationId, offer, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The offer awaiting an answer, or the agreement once terms are agreed.
    /// </summary>
    /// <remarks>
    /// Unambiguous by construction: a negotiation holds at most one standing offer
    /// and at most one accepted offer, both enforced by partial unique indexes.
    /// </remarks>
    public async Task<OfferModel?> GetCurrentOfferAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OffersRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        OfferModel? offer = await _queries
            .GetCurrentOfferAsync(organizationId, dealId, cancellationToken)
            .ConfigureAwait(false);

        return await _redaction.ApplyAsync(organizationId, offer, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares two offers in the same negotiation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires <c>deals.economics.read</c> and refuses without it. This is the one
    /// place M7 refuses rather than redacts: a diff with the economic rows removed
    /// would report that nothing changed when the guarantee moved by a hundred
    /// thousand, and a reader would act on that. An absence can be honest; a false
    /// answer cannot (ADR-0021).
    /// </para>
    /// <para>
    /// The comparison itself is computed by the F# rules kernel, so the same
    /// function answers here, in the Windows client and in any future surface.
    /// </para>
    /// </remarks>
    public async Task<OfferComparisonModel> CompareOffersAsync(
        OrganizationId organizationId,
        OfferId previousOfferId,
        OfferId currentOfferId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OffersRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _redaction.RequireEconomicsAsync(organizationId, cancellationToken).ConfigureAwait(false);

        OfferModel previous = await RequireOfferAsync(organizationId, previousOfferId, cancellationToken)
            .ConfigureAwait(false);

        OfferModel current = await RequireOfferAsync(organizationId, currentOfferId, cancellationToken)
            .ConfigureAwait(false);

        if (previous.DealId != current.DealId)
        {
            throw new DomainException(
                "Two offers can only be compared when they belong to the same negotiation.");
        }

        if (previous.Id == current.Id)
        {
            throw new DomainException("An offer cannot be compared with itself.");
        }

        TermDifference[] differences = DealRules.CompareTerms(
            [.. previous.Terms.Select(ToRulesInput)],
            [.. current.Terms.Select(ToRulesInput)]);

        return new OfferComparisonModel(
            previous.DealId,
            previous.Id,
            current.Id,
            [.. differences.Select(difference => Describe(difference, previous, current))]);
    }

    public async Task<IReadOnlyList<DealHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.DealsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        // The timeline names what happened, never what it was worth, so it needs
        // no economic redaction to be safe for a caller without the permission.
        return await _queries
            .GetHistoryAsync(organizationId, id, HistoryLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DealPipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        UserId? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.DealsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetPipelineAsync(organizationId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DealCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.DealsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OfferModel> RequireOfferAsync(
        OrganizationId organizationId,
        OfferId offerId,
        CancellationToken cancellationToken) =>
        await _queries.GetOfferAsync(organizationId, offerId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Offer), offerId.ToString());

    private static TermInput ToRulesInput(OfferTermModel term) =>
        new()
        {
            Code = (int)term.Code,
            Kind = (int)term.ValueKind,
            Amount = term.Amount,
            Currency = term.Currency,
            Number = term.Number,
            Whole = term.Whole,
            Text = term.Text,
            Flag = term.Flag,
            Date = term.Date,
            Unit = term.Unit is { } unit ? (int)unit : null,
            Sequence = term.Sequence,
        };

    private static TermDifferenceModel Describe(
        TermDifference difference,
        OfferModel previous,
        OfferModel current)
    {
        DealTermCode code = (DealTermCode)difference.TermCode;

        return new TermDifferenceModel(
            code,
            DealTermCatalog.Find(code)?.DisplayName ?? code.ToString(),
            difference.Change.ToString(),
            difference.Direction.ToString(),
            previous.Terms.FirstOrDefault(term => term.Code == code),
            current.Terms.FirstOrDefault(term => term.Code == code));
    }

    private static int Clamp(int? limit) =>
        limit is not { } value ? DefaultLimit : Math.Clamp(value, 1, MaximumLimit);
}
