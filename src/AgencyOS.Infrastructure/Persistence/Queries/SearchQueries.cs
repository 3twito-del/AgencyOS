using System.Data.Common;
using System.Globalization;
using AgencyOS.Application.Search;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Ranked search across people, companies and tasks.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL rather than LINQ. The ranking combines a full-text match, a
/// prefix match and a trigram similarity into one comparable score, and none of
/// those have an expression-tree equivalent. <c>CLAUDE.md</c> allows manual SQL
/// where it is justified; a ranking function that has to be read and argued about
/// is a clearer artifact as SQL than as a provider-specific function chain.
/// </para>
/// <para>
/// The score is deliberately banded so results from different tables sort
/// sensibly against each other: an exact name match scores 1.0, a prefix 0.9, a
/// full-text hit lands in 0.5-0.8 and a fuzzy hit in 0.2-0.6. The bands are the
/// same formula for all three types, so a company never outranks a person merely
/// for being a company.
/// </para>
/// <para>
/// The query is fully parameterized. User text reaches the database only as a
/// parameter, and the prefix <c>tsquery</c> is built by
/// <c>agencyos_prefix_tsquery</c>, a database function that quotes every token so
/// that a query containing <c>&amp;</c> or <c>!</c> is text rather than tsquery
/// syntax.
/// </para>
/// </remarks>
internal sealed class SearchQueries : ISearchQueries
{
    /// <summary>
    /// One ranked branch of the union.
    /// </summary>
    /// <remarks>
    /// The three branches differ only in table, columns and type code, so the
    /// scoring expression is written once and formatted per branch. Writing it
    /// three times is how the three quietly stop agreeing.
    /// </remarks>
    private const string BranchTemplate = """
        SELECT
            {typeCode} AS entity_type,
            t.{idColumn} AS id,
            t.{titleColumn} AS title,
            {subtitleExpression} AS subtitle,
            t.{statusColumn} AS status,
            (GREATEST(
                CASE
                    WHEN lower(t.{titleColumn}) = lower(q.raw) THEN 1.0
                    WHEN starts_with(lower(t.{titleColumn}), lower(q.raw)) THEN 0.9
                    ELSE 0.0
                END,
                CASE
                    WHEN q.tsq IS NOT NULL AND t.search_vector @@ q.tsq
                        THEN 0.5 + LEAST(ts_rank(t.search_vector, q.tsq), 1.0) * 0.3
                    ELSE 0.0
                END,
                CASE
                    WHEN t.{titleColumn} % q.raw
                        THEN 0.2 + similarity(t.{titleColumn}, q.raw) * 0.4
                    ELSE 0.0
                END
            ))::double precision AS score,
            CASE
                WHEN lower(t.{titleColumn}) = lower(q.raw) THEN 1
                WHEN starts_with(lower(t.{titleColumn}), lower(q.raw)) THEN 2
                WHEN q.tsq IS NOT NULL AND t.search_vector @@ q.tsq THEN 3
                ELSE 4
            END AS matched_on
        FROM {table} t
        CROSS JOIN q
        WHERE t.organization_id = @organization_id
          AND ({archivedPredicate})
          AND (
              starts_with(lower(t.{titleColumn}), lower(q.raw))
              OR (q.tsq IS NOT NULL AND t.search_vector @@ q.tsq)
              OR t.{titleColumn} % q.raw
          )
        """;

    private readonly AgencyOsDbContext _context;

    public SearchQueries(AgencyOsDbContext context) => _context = context;

    public async Task<SearchResultModel> SearchAsync(
        OrganizationId organizationId,
        string query,
        IReadOnlySet<SearchEntityType> types,
        bool includeArchived,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);

        List<string> branches = [];

        if (types.Contains(SearchEntityType.Person))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Person,
                table: "people",
                idColumn: "id",
                titleColumn: "display_name",
                subtitleExpression: "COALESCE(t.title, t.email)",
                statusColumn: "status",
                archivedPredicate: includeArchived ? "TRUE" : $"t.status = {(int)PersonStatus.Active}"));
        }

        if (types.Contains(SearchEntityType.Company))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Company,
                table: "companies",
                idColumn: "id",
                titleColumn: "name",
                subtitleExpression: "COALESCE(t.legal_name, t.website)",
                statusColumn: "status",
                archivedPredicate: includeArchived ? "TRUE" : $"t.status = {(int)CompanyStatus.Active}"));
        }

        if (types.Contains(SearchEntityType.Task))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Task,
                table: "tasks",
                idColumn: "id",
                titleColumn: "title",
                subtitleExpression: "t.notes",
                statusColumn: "state",

                // A completed task is the task equivalent of archived: still a
                // record, not part of what needs attention.
                archivedPredicate: includeArchived ? "TRUE" : $"t.state = {(int)TaskState.Open}"));
        }

        if (types.Contains(SearchEntityType.Credit))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Credit,
                table: "credits",
                idColumn: "id",
                titleColumn: "title",
                subtitleExpression: "COALESCE(t.role, t.notes)",
                statusColumn: "status",

                // Credits are historical records rather than a working set, so
                // there is nothing to exclude: every credit is always eligible.
                archivedPredicate: "TRUE"));
        }

        if (types.Contains(SearchEntityType.Material))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Material,
                table: "materials",
                idColumn: "id",
                titleColumn: "title",
                subtitleExpression: "COALESCE(t.version_label, t.notes)",
                statusColumn: "status",

                // A retired material is the material equivalent of archived: kept,
                // because knowing what was sent last year matters, but not part of
                // what is ready to send now.
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status <> {(int)MaterialStatus.Retired}"));
        }

        if (types.Contains(SearchEntityType.Project))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Project,
                table: "projects",
                idColumn: "id",
                titleColumn: "title",
                subtitleExpression: "COALESCE(t.working_title, t.logline)",
                statusColumn: "status",

                // An archived project is filed away rather than deleted, so it is
                // the project equivalent of an archived person: still findable when
                // asked for, absent from the working set.
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status <> {(int)ProjectStatus.Archived}"));
        }

        if (types.Contains(SearchEntityType.SourceProperty))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.SourceProperty,
                table: "source_properties",
                idColumn: "id",
                titleColumn: "title",
                subtitleExpression: "COALESCE(t.attributed_creator, t.source_reference)",

                // A source property has no lifecycle: it either is a book or it is
                // not. The type stands in for a status so the shared shape holds.
                statusColumn: "type",
                archivedPredicate: "TRUE"));
        }

        if (types.Contains(SearchEntityType.Package))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Package,
                table: "packages",
                idColumn: "id",
                titleColumn: "name",

                // Deliberately not strategy_notes. A caller without
                // packages.strategy.read must not be able to confirm what a note
                // says by searching a phrase and watching the package surface
                // (ADR-0019).
                subtitleExpression: "t.thesis",
                statusColumn: "status",
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status NOT IN ({(int)PackageStatus.Closed}, {(int)PackageStatus.Abandoned})"));
        }

        if (types.Contains(SearchEntityType.Opportunity))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Opportunity,
                table: "opportunities",
                idColumn: "id",
                titleColumn: "name",

                // Deliberately not strategy_notes, here or in the search vector. A
                // caller without opportunities.strategy.read must not be able to
                // confirm what a note says by searching for a phrase (ADR-0020).
                subtitleExpression: "t.description",
                statusColumn: "status",

                // A cancelled or closed pursuit is history rather than part of the
                // working set, so it is excluded unless asked for.
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status NOT IN ({(int)OpportunityStatus.Closed}, {(int)OpportunityStatus.Cancelled})"));
        }

        if (types.Contains(SearchEntityType.Deal))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Deal,
                table: "deals",
                idColumn: "id",
                titleColumn: "name",

                // The factual summary and nothing else. strategy_notes is absent
                // here and from the search vector, and no term value is indexed
                // anywhere: a caller without deals.economics.read must not be able
                // to confirm a compensation figure by searching for it and
                // watching a deal surface (ADR-0021).
                subtitleExpression: "t.summary",
                statusColumn: "status",

                // A cancelled negotiation or one closed without agreement is
                // history rather than part of the working set.
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status NOT IN ({(int)DealStatus.NoDeal}, {(int)DealStatus.Cancelled})"));
        }

        if (types.Contains(SearchEntityType.Contract))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Contract,
                table: "contracts",
                idColumn: "id",
                titleColumn: "title",

                // The factual summary and nothing else. legal_analysis and
                // strategy_notes are absent here and from the search vector, and no
                // term value is indexed anywhere. A caller without
                // contracts.privileged.read must not be able to confirm that
                // counsel wrote something by searching for a phrase and watching a
                // contract surface, and one without deals.economics.read must not
                // be able to confirm a figure the same way. A redaction that leaves
                // a search hit behind is not a redaction (ADR-0021, ADR-0022).
                subtitleExpression: "t.summary",
                statusColumn: "status",

                // An abandoned or superseded instrument is history rather than part
                // of the working set. A terminated one is deliberately still
                // included: what an ended agreement said is exactly what somebody
                // searches for afterwards.
                archivedPredicate: includeArchived
                    ? "TRUE"
                    : $"t.status NOT IN ({(int)ContractStatus.Abandoned}, "
                        + $"{(int)ContractStatus.Superseded})"));
        }

        if (types.Contains(SearchEntityType.Signal))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Signal,
                table: "signals",
                idColumn: "id",
                titleColumn: "title",

                // The claim, which is the point of finding the row at all. Notes
                // and excerpts are absent here and from the search vector: an
                // excerpt quotes a stored document, and a hit inside one would
                // report that document's contents to whoever ran the search
                // (ADR-0025).
                subtitleExpression: "t.claim",
                statusColumn: "verification",

                // Internal only, always. Global search results appear in a palette
                // beside people and projects, and a source-sensitive claim
                // surfacing there - or merely raising the result count - is the
                // disclosure the classification exists to prevent. Elevated claims
                // are found on the intelligence surface, which narrows in SQL by
                // what the caller may read (§28, ADR-0030).
                //
                // A retracted claim is history rather than part of the working set,
                // and is reachable with archived results included.
                archivedPredicate: includeArchived
                    ? $"t.sensitivity = {(int)IntelligenceSensitivity.Internal}"
                    : $"t.sensitivity = {(int)IntelligenceSensitivity.Internal} "
                        + $"AND t.verification <> {(int)SignalVerification.Retracted}"));
        }

        if (types.Contains(SearchEntityType.Invoice))
        {
            branches.Add(Branch(
                typeCode: (int)SearchEntityType.Invoice,
                table: "invoices",
                idColumn: "id",
                titleColumn: "reference",

                // The reference and nothing else. No amount, no balance and no
                // notes are indexed, so a hit reveals that an invoice with that
                // number exists and nothing whatsoever about what it is worth. The
                // whole branch is gated by finance.read (ADR-0023).
                subtitleExpression: "NULL",
                statusColumn: "status",

                // A voided invoice is history rather than part of the working set,
                // and a draft has no number to search for.
                archivedPredicate: includeArchived
                    ? "t.reference IS NOT NULL"
                    : $"t.reference IS NOT NULL AND t.status <> {(int)InvoiceStatus.Void}"));
        }

        if (branches.Count == 0)
        {
            return new SearchResultModel([], HasMore: false);
        }

        // One row beyond the page, so "is there more" is answered by the same
        // query rather than by a second count that can disagree with it.
        int limit = take + 1;

        string sql = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            WITH q AS (
                SELECT @query::text AS raw, agencyos_prefix_tsquery(@query) AS tsq
            )
            SELECT entity_type, id, title, subtitle, status, score, matched_on
            FROM (
            {string.Join("\n    UNION ALL\n", branches)}
            ) hits
            ORDER BY score DESC, title ASC, id ASC
            OFFSET {skip} LIMIT {limit};
            """);

        List<SearchHitModel> hits = [];

        DbConnection connection = _context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new NpgsqlParameter("organization_id", organizationId.Value));
            command.Parameters.Add(new NpgsqlParameter("query", query));

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                SearchEntityType type = (SearchEntityType)reader.GetInt32(0);

                hits.Add(new SearchHitModel(
                    type,
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    DescribeStatus(type, reader.GetInt32(4)),
                    reader.GetDouble(5),
                    (SearchMatchKind)reader.GetInt32(6)));
            }
        }

        bool hasMore = hits.Count > take;

        if (hasMore)
        {
            hits.RemoveAt(hits.Count - 1);
        }

        return new SearchResultModel(hits, hasMore);
    }

    private static string Branch(
        int typeCode,
        string table,
        string idColumn,
        string titleColumn,
        string subtitleExpression,
        string statusColumn,
        string archivedPredicate)
    {
        return BranchTemplate
            .Replace("{typeCode}", typeCode.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{table}", table, StringComparison.Ordinal)
            .Replace("{idColumn}", idColumn, StringComparison.Ordinal)
            .Replace("{titleColumn}", titleColumn, StringComparison.Ordinal)
            .Replace("{subtitleExpression}", subtitleExpression, StringComparison.Ordinal)
            .Replace("{statusColumn}", statusColumn, StringComparison.Ordinal)
            .Replace("{archivedPredicate}", archivedPredicate, StringComparison.Ordinal);
    }

    /// <summary>Turns a stored status code back into the name the contract uses.</summary>
    private static string DescribeStatus(SearchEntityType type, int status) => type switch
    {
        SearchEntityType.Person => ((PersonStatus)status).ToString(),
        SearchEntityType.Company => ((CompanyStatus)status).ToString(),
        SearchEntityType.Task => ((TaskState)status).ToString(),
        SearchEntityType.Credit => ((CreditStatus)status).ToString(),
        SearchEntityType.Material => ((MaterialStatus)status).ToString(),
        SearchEntityType.Project => ((ProjectStatus)status).ToString(),
        SearchEntityType.SourceProperty => ((SourcePropertyType)status).ToString(),
        SearchEntityType.Package => ((PackageStatus)status).ToString(),
        SearchEntityType.Opportunity => ((OpportunityStatus)status).ToString(),
        SearchEntityType.Deal => ((DealStatus)status).ToString(),
        SearchEntityType.Contract => ((ContractStatus)status).ToString(),
        SearchEntityType.Invoice => ((InvoiceStatus)status).ToString(),
        SearchEntityType.Signal => ((SignalVerification)status).ToString(),
        _ => status.ToString(CultureInfo.InvariantCulture),
    };
}
