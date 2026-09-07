using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Application.Projects;

/// <summary>
/// Authorizes every read of the project model, then delegates to the projections
/// and redacts what the caller may not see.
/// </summary>
/// <remarks>
/// <para>
/// Reads need tenant scoping for the same reason writes do: the endpoint policy
/// asks only whether the caller holds a permission somewhere, so without a scoped
/// check a user with <c>projects.read</c> in one tenant could read another's slate.
/// </para>
/// <para>
/// Package strategy is redacted through <see cref="SensitiveNotes"/> rather than
/// here, so that saved views and search - which reach these models by other routes
/// - apply the identical rule. M4 learned that the hard way (ADR-0017).
/// </para>
/// </remarks>
public sealed class ProjectQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;

    private readonly IProjectQueries _queries;
    private readonly TenantGuard _guard;
    private readonly SensitiveNotes _notes;
    private readonly IClock _clock;

    public ProjectQueryService(
        IProjectQueries queries,
        TenantGuard guard,
        SensitiveNotes notes,
        IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _notes = notes;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ProjectSummaryModel>> ListProjectsAsync(
        OrganizationId organizationId,
        ProjectFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListProjectsAsync(organizationId, filter ?? new ProjectFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProjectDetailModel?> GetProjectAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetProjectAsync(organizationId, projectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AttachmentModel>> ListAttachmentsAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        bool currentOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListAttachmentsAsync(organizationId, projectId, currentOnly, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The curated project history.
    /// </summary>
    /// <remarks>
    /// Composed from domain events and effective-dated rows, not from the audit
    /// trail. The audit trail answers "who did what, under what grant" for a
    /// security reviewer; this answers "what happened to this project" for the
    /// people working it, and the two are deliberately different surfaces
    /// (ADR-0012).
    /// </remarks>
    public async Task<IReadOnlyList<ProjectHistoryEntryModel>> GetProjectHistoryAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetProjectHistoryAsync(organizationId, projectId, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SourcePropertyModel>> ListSourcePropertiesAsync(
        OrganizationId organizationId,
        SourcePropertyFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListSourcePropertiesAsync(
                organizationId, filter ?? new SourcePropertyFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SourcePropertyModel?> GetSourcePropertyAsync(
        OrganizationId organizationId,
        SourcePropertyId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetSourcePropertyAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackageSummaryModel>> ListPackagesAsync(
        OrganizationId organizationId,
        PackageFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.PackagesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListPackagesAsync(organizationId, filter ?? new PackageFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PackageDetailModel?> GetPackageAsync(
        OrganizationId organizationId,
        PackageId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.PackagesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        PackageDetailModel? package = await _queries
            .GetPackageAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return package is null
            ? null
            : await _notes.ApplyAsync(organizationId, package, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Operational signals for the project side of the command centre.</summary>
    public async Task<ProjectCommandCenterModel> GetProjectCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ProjectsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard.AuthorizeAsync(Permission.PackagesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetProjectCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);
}
