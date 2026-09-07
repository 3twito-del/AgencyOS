using System.Globalization;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static AgencyOS.Infrastructure.Persistence.Queries.PeopleSliceProjection;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the representation model.
/// </summary>
/// <remarks>
/// <para>
/// Every query is tenant-filtered and untracked. Authorization and the redaction
/// of internal notes are applied above, in
/// <see cref="RepresentationQueryService"/>; this type is not registered for
/// direct use by the API.
/// </para>
/// <para>
/// Client-ness is computed from representation status rather than read from a
/// flag, which is the whole reason there is no flag to read.
/// </para>
/// </remarks>
internal sealed class RepresentationQueries : IRepresentationQueries
{
    /// <summary>Statuses from which a representation can still change.</summary>
    private static readonly RepresentationStatus[] NonTerminal =
    [
        RepresentationStatus.Pending,
        RepresentationStatus.Active,
        RepresentationStatus.Suspended,
    ];

    private static readonly ProspectStage[] OpenStages =
    [
        ProspectStage.Identified,
        ProspectStage.Contacted,
        ProspectStage.Courting,
    ];

    private readonly AgencyOsDbContext _context;

    public RepresentationQueries(AgencyOsDbContext context) => _context = context;

    public async Task<IReadOnlyList<TalentSummaryModel>> ListTalentAsync(
        OrganizationId organizationId,
        TalentFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<TalentProfile> query = _context.TalentProfiles
            .AsNoTracking()
            .Include(x => x.Disciplines)
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Discipline is { } discipline)
        {
            query = query.Where(x => x.Disciplines.Any(d => d.Discipline == discipline && d.EndsOn == null));
        }

        List<TalentProfile> profiles = await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profiles.Count == 0)
        {
            return [];
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<TalentSummaryModel> summaries = [];

        foreach (TalentProfile profile in profiles)
        {
            Representation? representation = context.CurrentFor(profile.PersonId);

            bool isClient = representation?.Status == RepresentationStatus.Active;
            bool wasClient = representation is null && context.EverRepresented(profile.PersonId);

            if (filter.ClientsOnly && !isClient)
            {
                continue;
            }

            if (filter.FormerClientsOnly && !(wasClient || representation is { Status: RepresentationStatus.Terminated or RepresentationStatus.Expired }))
            {
                continue;
            }

            if (filter.ScopeArea is { } area
                && representation?.Scopes.Any(s => s.Area == area && s.EndsOn == null) != true)
            {
                continue;
            }

            RepresentationTeamMember? lead = representation?.Team
                .FirstOrDefault(t => t.EndsOn == null && t.Role == RepresentationTeamRole.Lead);

            if (filter.LeadUserId is { } leadUserId && lead?.UserId.Value != leadUserId)
            {
                continue;
            }

            string displayName = context.PersonName(profile.PersonId);

            if (filter.Search is { Length: > 0 } search
                && !displayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            summaries.Add(new TalentSummaryModel(
                profile.Id.Value,
                profile.PersonId.Value,
                displayName,
                profile.CareerStage.ToString(),
                [.. profile.CurrentDisciplines.Select(d => d.ToString())],
                DescribeStatus(profile.PersonId, representation, context),
                isClient,
                lead?.UserId.Value,
                lead is null ? null : context.UserName(lead.UserId),
                [.. representation?.Scopes.Where(s => s.EndsOn == null).Select(s => s.Area.ToString()) ?? []],
                profile.UpdatedAt,
                profile.Version));
        }

        return [.. summaries.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).Take(limit)];
    }

    public async Task<TalentDetailModel?> GetTalentAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        TalentProfile? profile = await _context.TalentProfiles
            .AsNoTracking()
            .Include(x => x.Disciplines)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.PersonId == personId,
                cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return null;
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        Representation? representation = context.CurrentFor(personId);

        RepresentationTeamMember? lead = representation?.Team
            .FirstOrDefault(t => t.EndsOn == null && t.Role == RepresentationTeamRole.Lead);

        TalentSummaryModel summary = new(
            profile.Id.Value,
            profile.PersonId.Value,
            context.PersonName(personId),
            profile.CareerStage.ToString(),
            [.. profile.CurrentDisciplines.Select(d => d.ToString())],
            DescribeStatus(personId, representation, context),
            representation?.Status == RepresentationStatus.Active,
            lead?.UserId.Value,
            lead is null ? null : context.UserName(lead.UserId),
            [.. representation?.Scopes.Where(s => s.EndsOn == null).Select(s => s.Area.ToString()) ?? []],
            profile.UpdatedAt,
            profile.Version);

        return new TalentDetailModel(
            summary,
            profile.Summary,
            profile.PositioningNotes,
            profile.BaseMarket,
            profile.Languages,
            profile.CreatedAt);
    }

    public async Task<IReadOnlyList<ProspectModel>> ListProspectsAsync(
        OrganizationId organizationId,
        ProspectFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Prospect> query = _context.Prospects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.OpenOnly)
        {
            query = query.Where(x => OpenStages.Contains(x.Stage));
        }

        if (filter.Stage is { } stage)
        {
            query = query.Where(x => x.Stage == stage);
        }

        if (filter.OwnerUserId is { } owner)
        {
            UserId typed = new(owner);
            query = query.Where(x => x.OwnerUserId == typed);
        }

        if (filter.DueOnOrBefore is { } due)
        {
            query = query.Where(x => x.NextFollowUpOn != null && x.NextFollowUpOn <= due);
        }

        List<Prospect> prospects = await query
            .OrderBy(x => x.NextFollowUpOn == null)
            .ThenBy(x => x.NextFollowUpOn)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (prospects.Count == 0)
        {
            return [];
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return [.. prospects.Select(x => ToModel(x, context))];
    }

    public async Task<ProspectModel?> GetProspectAsync(
        OrganizationId organizationId,
        ProspectId prospectId,
        CancellationToken cancellationToken = default)
    {
        Prospect? prospect = await _context.Prospects
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == prospectId,
                cancellationToken)
            .ConfigureAwait(false);

        if (prospect is null)
        {
            return null;
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return ToModel(prospect, context);
    }

    public async Task<RepresentationModel?> GetRepresentationAsync(
        OrganizationId organizationId,
        RepresentationId representationId,
        CancellationToken cancellationToken = default)
    {
        Representation? representation = await Loaded()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == representationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (representation is null)
        {
            return null;
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return ToModel(representation, context);
    }

    public async Task<RepresentationModel?> GetCurrentRepresentationAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        Representation? representation = await Loaded()
            .Where(x => x.OrganizationId == organizationId && x.PersonId == personId)
            .OrderByDescending(x => x.StartsOn)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (representation is null)
        {
            return null;
        }

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return ToModel(representation, context);
    }

    /// <summary>
    /// Builds the curated representation history for a person.
    /// </summary>
    /// <remarks>
    /// Composed from domain records - status transitions, scope periods, team
    /// assignments and prospect stages - never from the audit trail. Audit exists
    /// for forensics and carries actor, client and permission; this answers "what
    /// happened with this relationship" and carries none of that (ADR-0012).
    /// </remarks>
    public async Task<IReadOnlyList<RepresentationHistoryEntryModel>> GetRepresentationHistoryAsync(
        OrganizationId organizationId,
        PersonId personId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        List<Representation> representations = await Loaded()
            .Where(x => x.OrganizationId == organizationId && x.PersonId == personId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Prospect> prospects = await _context.Prospects
            .AsNoTracking()
            .Include(x => x.Events)
            .Where(x => x.OrganizationId == organizationId && x.PersonId == personId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RepresentationContext context =
            await LoadContextAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<RepresentationHistoryEntryModel> entries = [];

        foreach (Representation representation in representations)
        {
            foreach (RepresentationEvent occurrence in representation.Events)
            {
                entries.Add(new RepresentationHistoryEntryModel(
                    occurrence.OccurredOn,
                    "representation.status",
                    occurrence.FromStatus is null
                        ? $"Representation created as {occurrence.ToStatus}"
                        : $"Representation moved from {occurrence.FromStatus} to {occurrence.ToStatus}",
                    occurrence.Reason));
            }

            foreach (RepresentationScope scope in representation.Scopes)
            {
                entries.Add(new RepresentationHistoryEntryModel(
                    scope.StartsOn,
                    "representation.scope",
                    $"Began representing {scope.Area}",
                    null));

                if (scope.EndsOn is { } scopeEnd)
                {
                    entries.Add(new RepresentationHistoryEntryModel(
                        scopeEnd,
                        "representation.scope",
                        $"Stopped representing {scope.Area}",
                        null));
                }
            }

            foreach (RepresentationTeamMember member in representation.Team)
            {
                string name = context.UserName(member.UserId);

                entries.Add(new RepresentationHistoryEntryModel(
                    member.StartsOn,
                    "representation.team",
                    $"{name} joined the team as {member.Role}",
                    null));

                if (member.EndsOn is { } memberEnd)
                {
                    entries.Add(new RepresentationHistoryEntryModel(
                        memberEnd,
                        "representation.team",
                        $"{name} left the team",
                        null));
                }
            }
        }

        foreach (Prospect prospect in prospects)
        {
            foreach (ProspectEvent occurrence in prospect.Events)
            {
                entries.Add(new RepresentationHistoryEntryModel(
                    occurrence.OccurredOn,
                    "prospect.stage",
                    occurrence.FromStage is null
                        ? "Identified as a prospect"
                        : $"Prospect moved from {occurrence.FromStage} to {occurrence.ToStage}",
                    occurrence.Reason));
            }
        }

        return
        [
            .. entries
                .OrderByDescending(x => x.OccurredOn)
                .ThenBy(x => x.Kind, StringComparer.Ordinal)
                .Take(limit),
        ];
    }

    public async Task<IReadOnlyList<CreditModel>> ListCreditsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        List<Credit> credits = await _context.Credits
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PersonId == personId)
            .OrderByDescending(x => x.Year == null)
            .ThenByDescending(x => x.Year)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (credits.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> companies = await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. credits.Select(x => new CreditModel(
                x.Id.Value,
                x.PersonId.Value,
                x.Title,
                x.Role,
                x.Type.ToString(),
                x.Status.ToString(),
                x.Year,
                x.CompanyId?.Value,
                x.CompanyId is { } company && companies.TryGetValue(company.Value, out string? name) ? name : null,
                x.Source,
                x.Notes,
                x.ProjectId?.Value,
                x.Version)),
        ];
    }

    public async Task<IReadOnlyList<MaterialModel>> ListMaterialsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        List<Material> materials = await _context.Materials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PersonId == personId)
            .OrderBy(x => x.Status)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. materials.Select(ToModel)];
    }

    public async Task<ClientOverviewModel?> GetClientOverviewAsync(
        OrganizationId organizationId,
        PersonId personId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        TalentDetailModel? talent = await GetTalentAsync(organizationId, personId, cancellationToken)
            .ConfigureAwait(false);

        if (talent is null)
        {
            return null;
        }

        RepresentationModel? representation =
            await GetCurrentRepresentationAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<TaskItem> tasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.State == TaskState.Open
                && x.RelatedPersonId == personId)
            .OrderBy(x => x.DueAt == null)
            .ThenBy(x => x.DueAt)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Interaction> interactions = await _context.Interactions
            .AsNoTracking()
            .Include(x => x.Participants)
            .Where(x => x.OrganizationId == organizationId
                && x.Participants.Any(p => p.PersonId == personId))
            .OrderByDescending(x => x.OccurredAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<CreditModel> credits =
            await ListCreditsAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MaterialModel> materials =
            await ListMaterialsAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RepresentationHistoryEntryModel> history =
            await GetRepresentationHistoryAsync(organizationId, personId, 10, cancellationToken)
                .ConfigureAwait(false);

        return new ClientOverviewModel(
            talent,
            representation,
            [.. tasks.Select(t => PeopleSliceProjection.ToModel(t, names))],
            [.. interactions.Select(i => PeopleSliceProjection.ToModel(i, names))],
            credits,
            materials,
            history);
    }

    // ------------------------------------------------------------- plumbing

    private IQueryable<Representation> Loaded() => _context.Representations
        .AsNoTracking()
        .Include(x => x.Events)
        .Include(x => x.Scopes)
        .Include(x => x.Team);

    private static MaterialModel ToModel(Material material) => new(
        material.Id.Value,
        material.PersonId.Value,
        material.Title,
        material.Type.ToString(),
        material.Status.ToString(),
        material.VersionLabel,
        material.ExternalUri,
        material.ReceivedOn,
        material.Source,
        material.Notes,
        material.Version);

    private static ProspectModel ToModel(Prospect prospect, RepresentationContext context) => new(
        prospect.Id.Value,
        prospect.PersonId.Value,
        context.PersonName(prospect.PersonId),
        prospect.Stage.ToString(),
        prospect.OwnerUserId.Value,
        context.UserName(prospect.OwnerUserId),
        prospect.Source,
        prospect.StrategyNotes,
        prospect.IdentifiedOn,
        prospect.NextFollowUpOn,
        prospect.ConvertedToRepresentationId?.Value,
        prospect.UpdatedAt,
        prospect.Version);

    private static RepresentationModel ToModel(Representation representation, RepresentationContext context) => new(
        representation.Id.Value,
        representation.PersonId.Value,
        context.PersonName(representation.PersonId),
        representation.Status.ToString(),
        representation.StartsOn,
        representation.EndsOn,
        representation.IsExclusive,
        representation.Territory,
        representation.Notes,
        [
            .. representation.Scopes
                .OrderBy(x => x.StartsOn)
                .Select(x => new RepresentationScopeModel(x.Area.ToString(), x.StartsOn, x.EndsOn)),
        ],
        [
            .. representation.Team
                .OrderBy(x => x.Role)
                .ThenBy(x => x.StartsOn)
                .Select(x => new RepresentationTeamMemberModel(
                    x.UserId.Value,
                    context.UserName(x.UserId),
                    x.Role.ToString(),
                    x.StartsOn,
                    x.EndsOn)),
        ],
        representation.UpdatedAt,
        representation.Version);

    /// <summary>
    /// Describes where somebody stands, including the case of never having been
    /// represented at all.
    /// </summary>
    private static string? DescribeStatus(
        PersonId personId,
        Representation? representation,
        RepresentationContext context)
    {
        if (representation is not null)
        {
            return representation.Status.ToString();
        }

        return context.EverRepresented(personId) ? nameof(RepresentationStatus.Terminated) : null;
    }

    private async Task<RepresentationContext> LoadContextAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        List<Representation> representations = await Loaded()
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Matched on the strongly typed identifier rather than its inner Guid: a
        // value-converted property cannot have .Value read inside a translated
        // query, and unwrapping it here keeps the filter in the database.
        List<UserId> userIds =
        [
            .. representations.SelectMany(x => x.Team).Select(x => x.UserId).Distinct(),
        ];

        Dictionary<Guid, string> users = userIds.Count == 0
            ? []
            : await _context.Users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .Select(x => new { Id = x.Id.Value, x.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
                .ConfigureAwait(false);

        // Prospect owners are users too, and may not be on any team.
        Dictionary<Guid, string> owners = await _context.Prospects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                prospect => prospect.OwnerUserId,
                user => user.Id,
                (prospect, user) => new { Id = user.Id.Value, user.DisplayName })
            .Distinct()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        foreach (KeyValuePair<Guid, string> owner in owners)
        {
            users[owner.Key] = owner.Value;
        }

        return new RepresentationContext(people, users, representations);
    }

    private async Task<PartyNameLookup> LoadPartyNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
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

        return new PartyNameLookup(people, companies);
    }

    /// <summary>
    /// Names and representations for one tenant, resolved once per request.
    /// </summary>
    /// <remarks>
    /// The same trade the directory projections make (ADR-0011): at one agency's
    /// volume, loading the tenant's names is faster and far easier to verify than
    /// joining through the exclusive arc on every row.
    /// </remarks>
    private sealed record RepresentationContext(
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Users,
        List<Representation> Representations)
    {
        public string PersonName(PersonId id) =>
            People.TryGetValue(id.Value, out string? name) ? name : "(unknown)";

        public string UserName(UserId id) =>
            Users.TryGetValue(id.Value, out string? name) ? name : "(unknown)";

        /// <summary>The representation that still governs, if one does.</summary>
        public Representation? CurrentFor(PersonId personId) => Representations
            .Where(x => x.PersonId == personId && NonTerminal.Contains(x.Status))
            .OrderByDescending(x => x.StartsOn)
            .FirstOrDefault();

        /// <summary>Whether the agency has ever represented them, terminated or not.</summary>
        public bool EverRepresented(PersonId personId) =>
            Representations.Any(x => x.PersonId == personId);
    }
}
