using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Application.Directory;

/// <summary>
/// Authorizes every read of the people slice, then delegates to the projections.
/// </summary>
/// <remarks>
/// <para>
/// Reads need the same tenant scoping as writes, and for the same reason: the
/// endpoint-level permission policy asks only whether the caller holds a
/// permission <em>somewhere</em>. Without a scoped check, a user with
/// <c>people.read</c> in one tenant could read another tenant's directory.
/// </para>
/// <para>
/// Putting that check here rather than in each endpoint means an endpoint cannot
/// forget it. The API is given this service and never the raw
/// <see cref="IPeopleSliceQueries"/>.
/// </para>
/// </remarks>
public sealed class PeopleSliceQueryService
{
    /// <summary>Largest page any list query will return.</summary>
    private const int MaximumLimit = 200;

    private const int DefaultLimit = 50;

    private readonly IPeopleSliceQueries _queries;
    private readonly TenantGuard _guard;
    private readonly IClock _clock;

    public PeopleSliceQueryService(IPeopleSliceQueries queries, TenantGuard guard, IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PersonSummaryModel>> ListPeopleAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.PeopleRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListPeopleAsync(organizationId, Normalize(search), includeArchived, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PersonDetailModel?> GetPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.PeopleRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries.GetPersonAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CompanySummaryModel>> ListCompaniesAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.CompaniesRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListCompaniesAsync(organizationId, Normalize(search), includeArchived, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CompanyDetailModel?> GetCompanyAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.CompaniesRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries.GetCompanyAsync(organizationId, companyId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a person's timeline.
    /// </summary>
    /// <remarks>
    /// Requires read access to people, interactions and relationships, because the
    /// projection composes all three. Requiring only the first would let a caller
    /// see interaction content they are not permitted to read directly.
    /// </remarks>
    public async Task<IReadOnlyList<TimelineEntryModel>?> GetPersonTimelineAsync(
        OrganizationId organizationId,
        PersonId personId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeTimelineAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .GetPersonTimelineAsync(organizationId, personId, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimelineEntryModel>?> GetCompanyTimelineAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeTimelineAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .GetCompanyTimelineAsync(organizationId, companyId, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TasksRead, organizationId, cancellationToken).ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.InteractionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TaskModel>> ListTasksAsync(
        OrganizationId organizationId,
        bool openOnly = true,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TasksRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListTasksAsync(organizationId, openOnly, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AuthorizeTimelineAsync(OrganizationId organizationId, CancellationToken cancellationToken)
    {
        await _guard.AuthorizeAsync(Permission.InteractionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.RelationshipsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.TasksRead, organizationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);

    private static string? Normalize(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
