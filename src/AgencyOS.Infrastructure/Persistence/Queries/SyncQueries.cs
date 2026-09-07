using AgencyOS.Application.Directory;
using AgencyOS.Application.Representations;
using AgencyOS.Application.Sync;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Sync;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static AgencyOS.Infrastructure.Persistence.Queries.PeopleSliceProjection;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Reads a page of a tenant's change feed and the current state of what it names.
/// </summary>
/// <remarks>
/// <para>
/// The page is read first and the records afterwards, without an explicit
/// isolation level, and that is sound rather than a shortcut. Hydration always
/// observes a state at or after the page, never before it. Returning a state
/// newer than the cursor is harmless: the change that produced it has a sequence
/// above the cursor, so the client is told to re-read the record on its next
/// page. The reverse - returning state older than the cursor implies - is what
/// would lose data, and cannot happen.
/// </para>
/// <para>
/// An entity named several times in one page is fetched once. A page is a list of
/// positions, not a list of fetches.
/// </para>
/// </remarks>
internal sealed class SyncQueries : ISyncQueries
{
    private readonly AgencyOsDbContext _context;
    private readonly IRepresentationQueries _representation;

    public SyncQueries(AgencyOsDbContext context, IRepresentationQueries representation)
    {
        _context = context;
        _representation = representation;
    }

    public async Task<SyncPageModel> ReadChangesAsync(
        OrganizationId organizationId,
        long afterSequence,
        int take,
        CancellationToken cancellationToken = default)
    {
        // One row beyond the page, so "is there more" comes from the same read.
        List<ChangeLogEntry> entries = await _context.ChangeLog
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Sequence > afterSequence)
            .OrderBy(x => x.Sequence)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = entries.Count > take;

        if (hasMore)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        if (entries.Count == 0)
        {
            return new SyncPageModel(afterSequence, HasMore: false, [], [], [], [], []);
        }

        List<ChangeEntryModel> changes =
        [
            .. entries.Select(x => new ChangeEntryModel(
                x.Sequence,
                x.EntityType,
                Guid.Parse(x.EntityId),
                x.Kind,
                x.OccurredAt)),
        ];

        HashSet<Guid> personIds = Identify(changes, nameof(Person));
        HashSet<Guid> companyIds = Identify(changes, nameof(Company));
        HashSet<Guid> taskIds = Identify(changes, nameof(TaskItem));

        IReadOnlyList<PersonSummaryModel> people =
            await LoadPeopleAsync(organizationId, personIds, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CompanySummaryModel> companies =
            await LoadCompaniesAsync(organizationId, companyIds, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TaskModel> tasks =
            await LoadTasksAsync(organizationId, taskIds, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TalentSummaryModel> talent =
            await LoadTalentAsync(organizationId, Identify(changes, nameof(TalentProfile)), cancellationToken)
                .ConfigureAwait(false);

        return new SyncPageModel(changes[^1].Sequence, hasMore, changes, people, companies, tasks, talent);
    }

    public async Task<long> GetHeadAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        // A tenant with no writes yet has no counter row, and its head is zero.
        return await _context.ChangeSequences
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.LastValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static HashSet<Guid> Identify(IEnumerable<ChangeEntryModel> changes, string entityType) =>
    [
        .. changes
            .Where(x => string.Equals(x.EntityType, entityType, StringComparison.Ordinal)
                && x.Kind == ChangeKind.Upsert)
            .Select(x => x.EntityId),
    ];

    private async Task<IReadOnlyList<PersonSummaryModel>> LoadPeopleAsync(
        OrganizationId organizationId,
        HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // Matched on the strongly typed identifier rather than its inner Guid: a
        // value-converted property cannot have .Value read inside a translated
        // query, and unwrapping it here keeps the filter in the database.
        List<PersonId> keys = [.. ids.Select(id => new PersonId(id))];

        List<Person> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> companyIds =
        [
            .. people.Where(x => x.PrimaryCompanyId.HasValue).Select(x => x.PrimaryCompanyId!.Value.Value),
        ];

        List<CompanyId> companyKeys = [.. companyIds.Select(id => new CompanyId(id))];

        Dictionary<Guid, string> companyNames = companyKeys.Count == 0
            ? []
            : await _context.Companies
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && companyKeys.Contains(x.Id))
                .Select(x => new { Id = x.Id.Value, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
                .ConfigureAwait(false);

        return [.. people.Select(person => ToSummary(person, companyNames))];
    }

    private async Task<IReadOnlyList<CompanySummaryModel>> LoadCompaniesAsync(
        OrganizationId organizationId,
        HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        List<CompanyId> keys = [.. ids.Select(id => new CompanyId(id))];

        List<Company> companies = await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. companies.Select(ToSummary)];
    }

    private async Task<IReadOnlyList<TaskModel>> LoadTasksAsync(
        OrganizationId organizationId,
        HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        List<TaskItemId> keys = [.. ids.Select(id => new TaskItemId(id))];

        List<TaskItem> tasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && keys.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return [.. tasks.Select(task => ToModel(task, names))];
    }

    /// <summary>
    /// Loads the talent summaries a page names.
    /// </summary>
    /// <remarks>
    /// A representation change emits a talent entry even when the person has no
    /// profile, because the recorder cannot know. Those simply resolve to nothing
    /// and the client has no row to write, which is the correct outcome: there was
    /// never a cached talent row to invalidate.
    /// </remarks>
    private async Task<IReadOnlyList<TalentSummaryModel>> LoadTalentAsync(
        OrganizationId organizationId,
        HashSet<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return [];
        }

        List<TalentSummaryModel> talent = [];

        foreach (Guid personId in personIds)
        {
            TalentDetailModel? detail = await _representation
                .GetTalentAsync(organizationId, new PersonId(personId), cancellationToken)
                .ConfigureAwait(false);

            if (detail is not null)
            {
                talent.Add(detail.Summary);
            }
        }

        return talent;
    }

    /// <summary>
    /// Resolves the display names a task's subject reference needs.
    /// </summary>
    /// <remarks>
    /// Loads the tenant's names rather than joining across the exclusive arc, for
    /// the same reason the directory projections do (ADR-0011): at one agency's
    /// volume it is faster to read and far easier to verify than four conditional
    /// joins.
    /// </remarks>
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
}
