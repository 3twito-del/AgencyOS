using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Opportunities;

/// <summary>
/// Authorizes every read of the pursuit model, then delegates to the projections
/// and redacts what the caller may not see.
/// </summary>
/// <remarks>
/// <para>
/// Reads need tenant scoping for the same reason writes do: the endpoint policy
/// asks only whether the caller holds a permission somewhere, so without a scoped
/// check a user with <c>opportunities.read</c> in one tenant could read another's
/// pipeline.
/// </para>
/// <para>
/// Strategy is redacted through <see cref="SensitiveNotes"/> rather than here, so
/// that saved views, search and the command centre apply the identical rule. M4
/// learned that the hard way and M5 confirmed the lesson (ADR-0017).
/// </para>
/// </remarks>
public sealed class OpportunityQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;

    private readonly IOpportunityQueries _queries;
    private readonly TenantGuard _guard;
    private readonly SensitiveNotes _notes;
    private readonly IClock _clock;

    public OpportunityQueryService(
        IOpportunityQueries queries,
        TenantGuard guard,
        SensitiveNotes notes,
        IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _notes = notes;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OpportunitySummaryModel>> ListOpportunitiesAsync(
        OrganizationId organizationId,
        OpportunityFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListOpportunitiesAsync(
                organizationId,
                filter ?? new OpportunityFilter(),
                _clock.UtcNow,
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityDetailModel?> GetOpportunityAsync(
        OrganizationId organizationId,
        OpportunityId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunityDetailModel? opportunity = await _queries
            .GetOpportunityAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return opportunity is null
            ? null
            : await _notes.ApplyAsync(organizationId, opportunity, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityTargetModel>> ListTargetsAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListTargetsAsync(organizationId, opportunityId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityTargetModel?> GetTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetTargetAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What has gone out.
    /// </summary>
    /// <remarks>
    /// Gated by <c>submissions.read</c> rather than by opportunity access. Who may
    /// see what the agency has sent, and to whom, is a question worth being able to
    /// answer separately from who may see the pursuit (ADR-0020).
    /// </remarks>
    public async Task<IReadOnlyList<SubmissionModel>> ListSubmissionsAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId = null,
        OpportunityTargetId? targetId = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.SubmissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListSubmissionsAsync(
                organizationId, opportunityId, targetId, _clock.UtcNow, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubmissionModel?> GetSubmissionAsync(
        OrganizationId organizationId,
        SubmissionId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.SubmissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetSubmissionAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PitchModel>> ListPitchesAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId = null,
        OpportunityTargetId? targetId = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListPitchesAsync(organizationId, opportunityId, targetId, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The curated pursuit timeline.
    /// </summary>
    /// <remarks>
    /// Composed from domain events, submissions, pitches and linked tasks - never
    /// from the audit trail. A pitch appears once, merged from its interaction and
    /// its commercial metadata, rather than twice under two headings.
    /// </remarks>
    public async Task<IReadOnlyList<OpportunityHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        OpportunityId id,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetHistoryAsync(organizationId, id, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetPipelineAsync(organizationId, ownerUserId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Factual operational signals for the pursuit side of the command centre.</summary>
    /// <remarks>
    /// Everything here is a business fact with a date behind it: an action that is
    /// past due, a reply that has not come. Nothing is scored, ranked or predicted.
    /// </remarks>
    public async Task<OpportunityCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.OpportunitiesRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard.AuthorizeAsync(Permission.SubmissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);
}
