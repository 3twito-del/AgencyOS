using System.Globalization;
using AgencyOS.Application.Deals;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the deal model.
/// </summary>
/// <remarks>
/// <para>
/// Every fact about where a negotiation stands is <em>derived</em> here rather
/// than stored: how many offers there are, which one is on the table, which one is
/// the agreement, whether a reply is outstanding. There is no
/// <c>accepted_offer_id</c> column on the deal to disagree with the offer that
/// says it is the agreement, and no <c>has_open_offer</c> flag to go stale
/// (ADR-0021).
/// </para>
/// <para>
/// The counterparty is read through the M6 target rather than copied onto the
/// deal, for the same reason: a copy would be a second answer that stops agreeing
/// the moment somebody corrects the target.
/// </para>
/// <para>
/// Deliberately unauthorized. <see cref="DealQueryService"/> applies the
/// tenant-scoped checks, and <see cref="DealRedaction"/> removes the economics and
/// the strategy a caller may not read.
/// </para>
/// </remarks>
internal sealed class DealQueries : IDealQueries
{
    private readonly AgencyOsDbContext _context;

    public DealQueries(AgencyOsDbContext context) => _context = context;

    public async Task<IReadOnlyList<DealSummaryModel>> ListDealsAsync(
        OrganizationId organizationId,
        DealFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Deal> query = Filtered(organizationId, filter);

        List<Deal> deals = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit * 4)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deals.Count == 0)
        {
            return [];
        }

        DealContext context = await LoadContextAsync(
            organizationId, [.. deals.Select(x => x.Id)], cancellationToken).ConfigureAwait(false);

        IEnumerable<DealSummaryModel> summaries = deals.Select(deal => ToSummary(deal, context));

        // Applied after projection because both depend on the offer rows rather
        // than on anything the deal row carries.
        if (filter.HasOpenOffer)
        {
            summaries = summaries.Where(x => x.HasOpenOffer);
        }

        if (filter.TermsAgreedOnly)
        {
            summaries = summaries.Where(x => x.AcceptedOfferId is not null);
        }

        return [.. summaries.Take(limit)];
    }

    public async Task<DealDetailModel?> GetDealAsync(
        OrganizationId organizationId,
        DealId id,
        CancellationToken cancellationToken = default)
    {
        Deal? deal = await _context.Deals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (deal is null)
        {
            return null;
        }

        DealContext context = await LoadContextAsync(organizationId, [deal.Id], cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OfferModel> offers = await ListOffersAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return new DealDetailModel(
            ToSummary(deal, context),
            deal.Summary,
            deal.StrategyNotes,
            offers,
            offers.FirstOrDefault(x => x.Status == OfferStatus.Accepted),
            offers.FirstOrDefault(x => x.Status == OfferStatus.Open),
            context.TasksFor(deal.Id),
            deal.CreatedAt);
    }

    public async Task<IReadOnlyList<OfferModel>> ListOffersAsync(
        OrganizationId organizationId,
        DealId? dealId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Offer> query = _context.Offers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (dealId is { } deal)
        {
            query = query.Where(x => x.DealId == deal);
        }

        List<Offer> offers = await query
            .OrderBy(x => x.DealId)
            .ThenBy(x => x.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ProjectOffersAsync(organizationId, offers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfferModel?> GetOfferAsync(
        OrganizationId organizationId,
        OfferId id,
        CancellationToken cancellationToken = default)
    {
        Offer? offer = await _context.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (offer is null)
        {
            return null;
        }

        IReadOnlyList<OfferModel> projected = await ProjectOffersAsync(
            organizationId, [offer], cancellationToken).ConfigureAwait(false);

        return projected.FirstOrDefault();
    }

    /// <summary>
    /// The offer awaiting an answer, or the agreement once terms are agreed.
    /// </summary>
    /// <remarks>
    /// Unambiguous by construction rather than by convention: partial unique
    /// indexes guarantee at most one of each per negotiation, so "the current
    /// offer" is a question with one answer.
    /// </remarks>
    public async Task<OfferModel?> GetCurrentOfferAsync(
        OrganizationId organizationId,
        DealId dealId,
        CancellationToken cancellationToken = default)
    {
        Offer? current = await _context.Offers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.DealId == dealId
                && (x.Status == OfferStatus.Open || x.Status == OfferStatus.Accepted))
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        IReadOnlyList<OfferModel> projected = await ProjectOffersAsync(
            organizationId, [current], cancellationToken).ConfigureAwait(false);

        return projected.FirstOrDefault();
    }

    /// <summary>
    /// A negotiation's curated timeline.
    /// </summary>
    /// <remarks>
    /// Composed from deal and offer events. Never from audit rows: the audit log
    /// answers who did what under which permission, and rendering it to an agent
    /// would answer the wrong question in the wrong vocabulary (ADR-0012).
    /// </remarks>
    public async Task<IReadOnlyList<DealHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        DealId id,
        int limit,
        CancellationToken cancellationToken = default)
    {
        List<DealEvent> dealEvents = await _context.DealEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.DealId == id)
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Offer> offers = await _context.Offers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.DealId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        OfferId[] offerIds = [.. offers.Select(x => x.Id)];

        List<OfferEvent> offerEvents = await _context.OfferEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && offerIds.Contains(x.OfferId))
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OfferId, Offer> byId = offers.ToDictionary(x => x.Id);

        List<DealHistoryEntryModel> entries =
        [
            .. dealEvents.Select(x => new DealHistoryEntryModel(
                x.RecordedAt,
                x.Kind.ToString(),
                DescribeDealEvent(x),
                x.Reason,
                null,
                Name(users, x.RecordedBy))),

            .. offerEvents.Select(x => new DealHistoryEntryModel(
                x.RecordedAt,
                "Offer" + x.Transition,
                DescribeOfferEvent(x, byId),
                x.Reason,
                x.OfferId,
                Name(users, x.RecordedBy))),
        ];

        return [.. entries.OrderByDescending(x => x.OccurredAt).Take(limit)];
    }

    public async Task<IReadOnlyList<DealPipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        UserId? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Deal> query = _context.Deals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (ownerUserId is { } owner)
        {
            query = query.Where(x => x.OwnerUserId == owner);
        }

        List<Deal> deals = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        if (deals.Count == 0)
        {
            return [.. PipelineStatuses.Select(status =>
                new DealPipelineColumnModel(status, []))];
        }

        DealContext context = await LoadContextAsync(
            organizationId, [.. deals.Select(x => x.Id)], cancellationToken).ConfigureAwait(false);

        List<DealSummaryModel> summaries = [.. deals.Select(deal => ToSummary(deal, context))];

        return
        [
            .. PipelineStatuses.Select(status => new DealPipelineColumnModel(
                status,
                [.. summaries
                    .Where(x => x.Status == status)
                    .OrderByDescending(x => x.UpdatedAt)])),
        ];
    }

    /// <summary>
    /// What a negotiator needs to look at, stated as fact.
    /// </summary>
    /// <remarks>
    /// Counts, dates and lists. No probability, no expected commission, no
    /// forecast, no quality score - M9 owns financial accounting and a later
    /// milestone owns judgment, and a number invented here would be read as one
    /// the agency stood behind.
    /// </remarks>
    public async Task<DealCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        List<Deal> deals = await _context.Deals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deals.Count == 0)
        {
            return new DealCommandCenterModel([], [], [], [], [], [], [], [], 0, 0);
        }

        DealContext context = await LoadContextAsync(
            organizationId, [.. deals.Select(x => x.Id)], cancellationToken).ConfigureAwait(false);

        List<DealSummaryModel> summaries = [.. deals.Select(deal => ToSummary(deal, context))];

        DateTimeOffset recently = now.AddDays(-14);
        DateTimeOffset expiringWindow = now.AddDays(7);

        return new DealCommandCenterModel(
            [.. summaries
                .Where(x => x.Status == DealStatus.Negotiating)
                .OrderByDescending(x => x.UpdatedAt)],

            // Awaiting an answer is the absence of one: an offer still standing.
            // Nothing anywhere records that a counterparty stayed silent.
            [.. summaries
                .Where(x => x.HasOpenOffer)
                .OrderBy(x => x.LatestOfferAt)],

            [.. summaries
                .Where(x => x.HasOpenOffer
                    && x.OpenOfferExpiresAt is { } expiry
                    && expiry <= expiringWindow)
                .OrderBy(x => x.OpenOfferExpiresAt)],

            [.. summaries
                .Where(x => x.LatestOfferAt is { } at && at >= recently)
                .OrderByDescending(x => x.LatestOfferAt)],

            [.. summaries
                .Where(x => x.Status == DealStatus.TermsAgreed && x.UpdatedAt >= recently)
                .OrderByDescending(x => x.UpdatedAt)],

            [.. summaries
                .Where(x => x.Status is DealStatus.NoDeal or DealStatus.Cancelled
                    && x.UpdatedAt >= recently)
                .OrderByDescending(x => x.UpdatedAt)],

            // A factual state, and explicitly not a claim that contract work has
            // started. M7 has no way to know whether it has.
            [.. summaries
                .Where(x => x.Status == DealStatus.TermsAgreed)
                .OrderByDescending(x => x.UpdatedAt)],

            [.. context.OverdueTasks(now)],

            summaries.Count(x => x.Status == DealStatus.Negotiating),
            summaries.Count(x => x.Status == DealStatus.TermsAgreed));
    }

    // ----------------------------------------------------------------- helpers

    private static readonly DealStatus[] PipelineStatuses =
    [
        DealStatus.Draft,
        DealStatus.Negotiating,
        DealStatus.TermsAgreed,
        DealStatus.NoDeal,
    ];

    private IQueryable<Deal> Filtered(OrganizationId organizationId, DealFilter filter)
    {
        IQueryable<Deal> query = _context.Deals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.OwnerUserId is { } owner)
        {
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.OpportunityId is { } opportunity)
        {
            query = query.Where(x => x.OpportunityId == opportunity);
        }

        if (filter.OpportunityTargetId is { } target)
        {
            query = query.Where(x => x.OpportunityTargetId == target);
        }

        if (filter.OpenedAfter is { } after)
        {
            query = query.Where(x => x.OpenedOn >= after);
        }

        if (filter.OpenedBefore is { } before)
        {
            query = query.Where(x => x.OpenedOn <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();

            query = query.Where(x =>
                EF.Functions.ILike(x.Name, $"%{search}%")
                || (x.Reference != null && EF.Functions.ILike(x.Reference, $"%{search}%"))
                || (x.Summary != null && EF.Functions.ILike(x.Summary, $"%{search}%")));
        }

        // The counterparty lives on the M6 target, so these narrow through it
        // rather than through a column this table does not carry.
        if (filter.CounterpartyCompanyId is { } company)
        {
            query = query.Where(x => _context.OpportunityTargets
                .Any(t => t.Id == x.OpportunityTargetId
                    && t.CompanyId != null
                    && t.CompanyId.Value.Value == company));
        }

        if (filter.CounterpartyPersonId is { } person)
        {
            query = query.Where(x => _context.OpportunityTargets
                .Any(t => t.Id == x.OpportunityTargetId
                    && t.PersonId != null
                    && t.PersonId.Value.Value == person));
        }

        // The subject lives on the M6 opportunity, for the same reason.
        if (filter.TalentProfileId is { } talent)
        {
            query = query.Where(x => _context.OpportunitySubjects
                .Any(s => s.OpportunityId == x.OpportunityId
                    && s.TalentProfileId != null
                    && s.TalentProfileId.Value.Value == talent));
        }

        if (filter.ProjectId is { } project)
        {
            query = query.Where(x => _context.OpportunitySubjects
                .Any(s => s.OpportunityId == x.OpportunityId
                    && s.ProjectId != null
                    && s.ProjectId.Value.Value == project));
        }

        return query;
    }

    private async Task<IReadOnlyList<OfferModel>> ProjectOffersAsync(
        OrganizationId organizationId,
        IReadOnlyList<Offer> offers,
        CancellationToken cancellationToken)
    {
        if (offers.Count == 0)
        {
            return [];
        }

        OfferId[] ids = [.. offers.Select(x => x.Id)];

        List<OfferTerm> terms = await _context.OfferTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.OfferId))
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OfferId, OfferDirection> directions =
            offers.ToDictionary(x => x.Id, x => x.Direction);

        ILookup<OfferId, OfferTerm> byOffer = terms.ToLookup(x => x.OfferId);

        return
        [
            .. offers.Select(offer => new OfferModel(
                offer.Id,
                offer.DealId,
                offer.Direction,
                offer.Status,
                offer.Sequence,
                offer.RespondsToOfferId,
                ClassifyResponse(offer, directions),
                offer.RecordedAt,
                offer.CommunicatedAt,
                offer.RecordedByUserId,
                Name(users, offer.RecordedByUserId),
                offer.Summary,
                offer.Notes,
                offer.ExpiresAt,
                [.. byOffer[offer.Id].OrderBy(term => term.Sequence).Select(ToTermModel)],
                false,
                offer.Version)),
        ];
    }

    /// <summary>
    /// Whether a response is a counter or a revision.
    /// </summary>
    /// <remarks>
    /// Derived from the two sides rather than stored. An answer from the other
    /// side is a counter; one from the same side replaces the agency's own earlier
    /// position, which is a revision.
    /// </remarks>
    private static OfferResponseKind? ClassifyResponse(
        Offer offer,
        Dictionary<OfferId, OfferDirection> directions) =>
        offer.RespondsToOfferId is { } respondsTo
            && directions.TryGetValue(respondsTo, out OfferDirection answered)
            ? Offer.Classify(answered, offer.Direction)
            : null;

    private static OfferTermModel ToTermModel(OfferTerm term)
    {
        DealTermDefinition? definition = DealTermCatalog.Find(term.Code);

        return new OfferTermModel(
            term.Code,
            term.Label ?? definition?.DisplayName ?? term.Code.ToString(),
            term.ValueKind,
            DealTermCatalog.IsEconomic(term.Code),
            term.AmountValue,
            term.CurrencyCodeValue,
            term.NumericValue,
            term.IntegerValue,
            term.TextValue,
            term.BooleanValue,
            term.DateValue,
            term.Unit,
            Render(term),
            term.Sequence,
            term.Notes);
    }

    /// <summary>
    /// Renders a term for display.
    /// </summary>
    /// <remarks>
    /// Never the canonical form, and nothing parses it back. The typed columns are
    /// what M8 reconciles against a contract and what comparison works on;
    /// this is a convenience for a screen, produced in invariant culture so it does
    /// not vary by who is looking at it.
    /// </remarks>
    private static string Render(OfferTerm term) => term.ValueKind switch
    {
        TermValueKind.Money when term.AmountValue is { } amount =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{amount:N2} {term.CurrencyCodeValue}"),

        TermValueKind.Percentage when term.NumericValue is { } percentage =>
            string.Create(CultureInfo.InvariantCulture, $"{percentage:0.####}%"),

        TermValueKind.Decimal when term.NumericValue is { } number =>
            number.ToString("0.####", CultureInfo.InvariantCulture),

        TermValueKind.Integer when term.IntegerValue is { } whole =>
            whole.ToString(CultureInfo.InvariantCulture),

        TermValueKind.Count when term.IntegerValue is { } quantity =>
            term.Unit is { } unit
                ? string.Create(CultureInfo.InvariantCulture, $"{quantity} {unit}")
                : quantity.ToString(CultureInfo.InvariantCulture),

        TermValueKind.Duration when term.IntegerValue is { } length =>
            string.Create(CultureInfo.InvariantCulture, $"{length} {term.Unit}"),

        TermValueKind.Date when term.DateValue is { } date => date.ToString("O"),
        TermValueKind.Boolean when term.BooleanValue is { } flag => flag ? "Yes" : "No",
        TermValueKind.Text => term.TextValue ?? string.Empty,
        _ => string.Empty,
    };

    private static DealSummaryModel ToSummary(Deal deal, DealContext context)
    {
        DealOffers offers = context.OffersFor(deal.Id);

        return new DealSummaryModel(
            deal.Id,
            deal.Name,
            deal.Reference,
            deal.Kind,
            deal.Status,
            deal.OpportunityId,
            context.OpportunityName(deal.OpportunityId),
            deal.OpportunityTargetId,
            context.Counterparty(deal.OpportunityTargetId),
            context.CounterpartyCompany(deal.OpportunityTargetId),
            context.CounterpartyPerson(deal.OpportunityTargetId),
            context.Subject(deal.OpportunityId),
            deal.OwnerUserId,
            Name(context.Users, deal.OwnerUserId),
            deal.OpenedOn,
            deal.ClosedOn,
            offers.Count,
            offers.LatestId,
            offers.LatestDirection,
            offers.LatestAt,
            offers.OpenId is not null,
            offers.OpenExpiresAt,
            offers.AcceptedId,
            context.OpenTaskCount(deal.Id),
            context.NextTaskDueAt(deal.Id),
            deal.UpdatedAt,
            deal.Version);
    }

    private static string DescribeDealEvent(DealEvent entry) => entry.Kind switch
    {
        DealEventKind.Opened => "Negotiation opened",
        DealEventKind.MetadataUpdated => "Deal details updated",
        _ => entry.Transition switch
        {
            DealTransition.OfferRecorded => "Negotiation began",
            DealTransition.OfferAccepted => "Commercial terms agreed",
            DealTransition.NegotiationReopened => "Negotiation reopened",
            DealTransition.ClosedNoDeal => "Closed without agreement",
            DealTransition.Cancelled => "Negotiation cancelled",
            _ => string.Create(CultureInfo.InvariantCulture, $"Moved to {entry.ToStatus}"),
        },
    };

    private static string DescribeOfferEvent(OfferEvent entry, Dictionary<OfferId, Offer> offers)
    {
        string side = offers.TryGetValue(entry.OfferId, out Offer? offer)
            ? offer.Direction == OfferDirection.Inbound ? "Inbound" : "Outbound"
            : "Offer";

        return entry.Transition switch
        {
            OfferTransition.Opened => $"{side} offer recorded",
            OfferTransition.Accepted => $"{side} offer accepted",
            OfferTransition.Rejected => $"{side} offer rejected",
            OfferTransition.Withdrawn => $"{side} offer withdrawn",
            OfferTransition.Expired => $"{side} offer expired",
            OfferTransition.AnsweredByCounter => $"{side} offer superseded by a later offer",
            OfferTransition.UnwoundByReopen => $"{side} agreement unwound by reopening",
            _ => $"{side} offer changed",
        };
    }

    private static string? Name(Dictionary<Guid, string> users, UserId id) =>
        users.TryGetValue(id.Value, out string? name) ? name : null;

    private Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.Memberships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Id = u.Id.Value, u.DisplayName })
            .Distinct()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

    /// <summary>
    /// Everything a batch of deals needs, loaded once.
    /// </summary>
    /// <remarks>
    /// Offers, targets, subjects, tasks and names resolved in five queries rather
    /// than five per deal. The derived facts are then computed in memory, which is
    /// what keeps them impossible to disagree with the rows they came from.
    /// </remarks>
    private async Task<DealContext> LoadContextAsync(
        OrganizationId organizationId,
        IReadOnlyList<DealId> dealIds,
        CancellationToken cancellationToken)
    {
        DealId[] ids = [.. dealIds];

        List<Offer> offers = await _context.Offers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.DealId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Deal> deals = await _context.Deals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        OpportunityTargetId[] targetIds = [.. deals.Select(x => x.OpportunityTargetId).Distinct()];
        OpportunityId[] opportunityIds = [.. deals.Select(x => x.OpportunityId).Distinct()];

        List<OpportunityTarget> targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && targetIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Opportunity> opportunities = await _context.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && opportunityIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<OpportunitySubject> subjects = await _context.OpportunitySubjects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && opportunityIds.Contains(x.OpportunityId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var links = await _context.DealTaskLinks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.DealId))
            .Join(
                _context.Tasks.AsNoTracking().Where(t => t.State == TaskState.Open),
                link => link.TaskItemId,
                task => task.Id,
                (link, task) => new
                {
                    link.DealId,
                    link.OfferId,
                    TaskId = task.Id.Value,
                    task.Title,
                    task.State,
                    task.Priority,
                    task.DueAt,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> projects = await _context.Projects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> talent = await _context.TalentProfiles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.People.AsNoTracking(),
                profile => profile.PersonId,
                person => person.Id,
                (profile, person) => new { Id = profile.Id.Value, person.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> packages = await _context.Packages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        return new DealContext(
            offers.ToLookup(x => x.DealId),
            targets.ToDictionary(x => x.Id),
            opportunities.ToDictionary(x => x.Id),
            subjects.ToLookup(x => x.OpportunityId),
            [.. links.Select(x => (
                x.DealId,
                Task: new DealTaskModel(
                    x.TaskId,
                    x.Title,
                    x.State.ToString(),
                    x.Priority.ToString(),
                    x.DueAt,
                    x.OfferId)))],
            people,
            companies,
            users,
            projects,
            talent,
            packages);
    }

    /// <summary>The derived offer facts for one negotiation.</summary>
    private sealed record DealOffers(
        int Count,
        OfferId? LatestId,
        OfferDirection? LatestDirection,
        DateTimeOffset? LatestAt,
        OfferId? OpenId,
        DateTimeOffset? OpenExpiresAt,
        OfferId? AcceptedId);

    /// <summary>Everything a batch of deals needs, resolved once per request.</summary>
    private sealed record DealContext(
        ILookup<DealId, Offer> Offers,
        Dictionary<OpportunityTargetId, OpportunityTarget> Targets,
        Dictionary<OpportunityId, Opportunity> Opportunities,
        ILookup<OpportunityId, OpportunitySubject> Subjects,
        IReadOnlyList<(DealId DealId, DealTaskModel Task)> Tasks,
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies,
        Dictionary<Guid, string> Users,
        Dictionary<Guid, string> Projects,
        Dictionary<Guid, string> Talent,
        Dictionary<Guid, string> Packages)
    {
        public DealOffers OffersFor(DealId dealId)
        {
            List<Offer> offers = [.. Offers[dealId].OrderBy(x => x.Sequence)];

            // Drafts are not on the table. They have not been made or received, so
            // they do not count towards the thread a reader is looking at.
            List<Offer> recorded = [.. offers.Where(x => x.Status != OfferStatus.Draft)];

            Offer? latest = recorded.LastOrDefault();
            Offer? open = recorded.FirstOrDefault(x => x.Status == OfferStatus.Open);
            Offer? accepted = recorded.FirstOrDefault(x => x.Status == OfferStatus.Accepted);

            return new DealOffers(
                recorded.Count,
                latest?.Id,
                latest?.Direction,
                latest?.CommunicatedAt ?? latest?.RecordedAt,
                open?.Id,
                open?.ExpiresAt,
                accepted?.Id);
        }

        public string OpportunityName(OpportunityId id) =>
            Opportunities.TryGetValue(id, out Opportunity? opportunity) ? opportunity.Name : "(unknown)";

        public string Counterparty(OpportunityTargetId id)
        {
            if (!Targets.TryGetValue(id, out OpportunityTarget? target))
            {
                return "(unknown)";
            }

            if (target.CompanyId is { } company)
            {
                return Companies.TryGetValue(company.Value, out string? name) ? name : "(unknown)";
            }

            return target.PersonId is { } person
                && People.TryGetValue(person.Value, out string? personName)
                ? personName
                : "(unknown)";
        }

        public Guid? CounterpartyCompany(OpportunityTargetId id) =>
            Targets.TryGetValue(id, out OpportunityTarget? target) && target.CompanyId is { } company
                ? company.Value
                : null;

        public Guid? CounterpartyPerson(OpportunityTargetId id) =>
            Targets.TryGetValue(id, out OpportunityTarget? target) && target.PersonId is { } person
                ? person.Value
                : null;

        /// <summary>
        /// What the pursuit is about, through its primary subject.
        /// </summary>
        /// <remarks>
        /// Read through M6 rather than copied onto the deal. M7 proved no need for
        /// a frozen deal-specific snapshot, and copying every subject would have
        /// been the kind of duplication the brief warns against.
        /// </remarks>
        public string? Subject(OpportunityId id)
        {
            if (!Opportunities.TryGetValue(id, out Opportunity? opportunity))
            {
                return null;
            }

            OpportunitySubjectKind? required = Opportunity.RequiredSubjectKind(opportunity.Kind);

            OpportunitySubject? subject = required is { } kind
                ? Subjects[id].FirstOrDefault(x => x.Kind == kind)
                : Subjects[id].FirstOrDefault();

            if (subject is null)
            {
                return null;
            }

            return subject.Kind switch
            {
                OpportunitySubjectKind.TalentProfile when subject.TalentProfileId is { } profile =>
                    Talent.TryGetValue(profile.Value, out string? name) ? name : null,
                OpportunitySubjectKind.Project when subject.ProjectId is { } project =>
                    Projects.TryGetValue(project.Value, out string? title) ? title : null,
                OpportunitySubjectKind.Package when subject.PackageId is { } package =>
                    Packages.TryGetValue(package.Value, out string? name) ? name : null,
                _ => null,
            };
        }

        public IReadOnlyList<DealTaskModel> TasksFor(DealId dealId) =>
            [.. Tasks.Where(x => x.DealId == dealId).Select(x => x.Task).OrderBy(x => x.DueAt)];

        public int OpenTaskCount(DealId dealId) => Tasks.Count(x => x.DealId == dealId);

        public DateTimeOffset? NextTaskDueAt(DealId dealId) =>
            Tasks
                .Where(x => x.DealId == dealId && x.Task.DueAt is not null)
                .Select(x => x.Task.DueAt)
                .DefaultIfEmpty(null)
                .Min();

        public IReadOnlyList<DealTaskModel> OverdueTasks(DateTimeOffset now) =>
            [.. Tasks
                .Where(x => x.Task.DueAt is { } due && due < now)
                .Select(x => x.Task)
                .OrderBy(x => x.DueAt)];
    }
}
