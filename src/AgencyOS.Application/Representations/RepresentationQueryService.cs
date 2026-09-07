using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Representations;

/// <summary>
/// Authorizes every read of the representation model, then delegates to the
/// projections and redacts what the caller may not see.
/// </summary>
/// <remarks>
/// <para>
/// Reads need the same tenant scoping as writes: the endpoint policy asks only
/// whether the caller holds a permission <em>somewhere</em>, so without a scoped
/// check a user with <c>talent.read</c> in one tenant could read another's client
/// list.
/// </para>
/// <para>
/// Internal judgment is redacted here too, through <see cref="SensitiveNotes"/>.
/// Positioning and strategy notes are removed for callers without
/// <c>talent.notes.read</c>, and the result is indistinguishable from the field
/// being empty - a caller learns nothing about whether a note exists (ADR-0017).
/// The redaction lives in that shared type rather than here because saved views
/// reach the same models by a different route.
/// </para>
/// </remarks>
public sealed class RepresentationQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;

    private readonly IRepresentationQueries _queries;
    private readonly TenantGuard _guard;
    private readonly SensitiveNotes _notes;
    private readonly IClock _clock;

    public RepresentationQueryService(
        IRepresentationQueries queries,
        TenantGuard guard,
        SensitiveNotes notes,
        IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _notes = notes;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TalentSummaryModel>> ListTalentAsync(
        OrganizationId organizationId,
        TalentFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TalentRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListTalentAsync(organizationId, filter ?? new TalentFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TalentDetailModel?> GetTalentAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TalentRead, organizationId, cancellationToken).ConfigureAwait(false);

        TalentDetailModel? talent = await _queries
            .GetTalentAsync(organizationId, personId, cancellationToken)
            .ConfigureAwait(false);

        return talent is null
            ? null
            : await _notes.ApplyAsync(organizationId, talent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProspectModel>> ListProspectsAsync(
        OrganizationId organizationId,
        ProspectFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProspectsRead, organizationId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProspectModel> prospects = await _queries
            .ListProspectsAsync(organizationId, filter ?? new ProspectFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);

        return await _notes.ApplyAsync(organizationId, prospects, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProspectModel?> GetProspectAsync(
        OrganizationId organizationId,
        ProspectId prospectId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProspectsRead, organizationId, cancellationToken).ConfigureAwait(false);

        ProspectModel? prospect = await _queries
            .GetProspectAsync(organizationId, prospectId, cancellationToken)
            .ConfigureAwait(false);

        return prospect is null
            ? null
            : await _notes.ApplyAsync(organizationId, prospect, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepresentationModel?> GetRepresentationAsync(
        OrganizationId organizationId,
        RepresentationId representationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.RepresentationRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetRepresentationAsync(organizationId, representationId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RepresentationModel?> GetCurrentRepresentationAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.RepresentationRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetCurrentRepresentationAsync(organizationId, personId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepresentationHistoryEntryModel>> GetRepresentationHistoryAsync(
        OrganizationId organizationId,
        PersonId personId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.RepresentationRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetRepresentationHistoryAsync(organizationId, personId, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CreditModel>> ListCreditsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TalentRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries.ListCreditsAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaterialModel>> ListMaterialsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TalentRead, organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries.ListMaterialsAsync(organizationId, personId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the client overview.
    /// </summary>
    /// <remarks>
    /// Composes talent, representation, tasks, interactions, credits and materials,
    /// so it requires read access to each of them. Requiring only talent access
    /// would let a caller see interaction content they could not read directly -
    /// the same reasoning the M2 timeline uses.
    /// </remarks>
    public async Task<ClientOverviewModel?> GetClientOverviewAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.TalentRead, organizationId, cancellationToken).ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.RepresentationRead, organizationId, cancellationToken)
            .ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.InteractionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.TasksRead, organizationId, cancellationToken).ConfigureAwait(false);

        ClientOverviewModel? overview = await _queries
            .GetClientOverviewAsync(organizationId, personId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (overview is null)
        {
            return null;
        }

        return overview with
        {
            Talent = await _notes.ApplyAsync(organizationId, overview.Talent, cancellationToken).ConfigureAwait(false),
        };
    }

    private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);
}
