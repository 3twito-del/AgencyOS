using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Legal;

/// <summary>
/// Authorizes every read of the contract model, then redacts what the caller may
/// not see.
/// </summary>
/// <remarks>
/// The same shape M7 uses, for the same reason: an endpoint policy asks only
/// whether the caller holds a permission somewhere, so a scoped check has to
/// happen here or a user with <c>contracts.read</c> in one tenant could read
/// another tenant's paper. Redaction goes through <see cref="ContractRedaction"/>
/// rather than happening inline, so saved views, search and the command centre
/// apply an identical rule (ADR-0017, ADR-0021, ADR-0022).
/// </remarks>
public sealed class ContractQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;
    private const int HistoryLimit = 200;
    private const int MaximumHorizonDays = 365;
    private const int DefaultHorizonDays = 90;

    private readonly IContractQueries _queries;
    private readonly TenantGuard _guard;
    private readonly ContractRedaction _redaction;
    private readonly IClock _clock;

    public ContractQueryService(
        IContractQueries queries,
        TenantGuard guard,
        ContractRedaction redaction,
        IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _redaction = redaction;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ContractSummaryModel>> ListContractsAsync(
        OrganizationId organizationId,
        ContractFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        // Summaries carry no drafted terms and no privileged prose, so there is
        // nothing on them to redact. Every figure is a count or a date.
        return await _queries
            .ListContractsAsync(
                organizationId, filter ?? new ContractFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContractDetailModel?> GetContractAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractDetailModel? contract = await _queries
            .GetContractAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return contract is null
            ? null
            : await _redaction.ApplyAsync(organizationId, contract, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<ContractVersionModel?> GetVersionAsync(
        OrganizationId organizationId,
        ContractVersionId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractVersionModel? version = await _queries
            .GetVersionAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        return await _redaction.ApplyAsync(organizationId, version, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares a drafting version against the offer the contract was papered from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires <c>contracts.terms.read</c> and refuses without it, on the M7
    /// precedent. A comparison with the terms stripped out would report that the
    /// draft matched what was agreed when it did not, and somebody would sign on
    /// that. An absence can be honest; a false answer cannot.
    /// </para>
    /// <para>
    /// It reports Matched, Changed, MissingFromContract, AddedInContract and
    /// NotComparable, and nothing else. Whether a change is acceptable is a legal
    /// judgement AgencyOS does not make (ADR-0022).
    /// </para>
    /// </remarks>
    public async Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _redaction.RequireTermsAsync(organizationId, cancellationToken).ConfigureAwait(false);

        // The economic figures are the substance of the comparison, so a caller
        // without them is refused here too, rather than handed a diff with the
        // money silently absent from both sides and every line reading Matched.
        await _guard.AuthorizeAsync(Permission.DealEconomicsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ReconcileAsync(organizationId, contractId, versionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContractOptionModel>> ListOptionsAsync(
        OrganizationId organizationId,
        OptionFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.RightsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListOptionsAsync(
                organizationId, filter ?? new OptionFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ObligationFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ObligationsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ObligationModel> obligations = await _queries
            .ListObligationsAsync(
                organizationId, filter ?? new ObligationFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);

        return await _redaction.ApplyAsync(organizationId, obligations, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RightsGrantModel>> ListRightsGrantsAsync(
        OrganizationId organizationId,
        ContractId? contractId = null,
        Guid? projectId = null,
        bool currentOnly = true,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.RightsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListRightsGrantsAsync(organizationId, contractId, projectId, currentOnly, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContractHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        // The timeline names what happened, never what a term was worth, so it
        // carries nothing the economics permission protects.
        return await _queries
            .GetHistoryAsync(organizationId, id, HistoryLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Dates somebody has to act on, out to a horizon.
    /// </summary>
    /// <remarks>
    /// Only rows whose deadline resolved to an actual date appear. A clause whose
    /// anchor event has not happened, or which counts business days this build
    /// cannot count, is absent rather than guessed onto a day. A legal calendar
    /// that quietly invents dates is worse than one that admits a gap (ADR-0022).
    /// </remarks>
    public async Task<IReadOnlyList<LegalDeadlineModel>> GetDeadlinesAsync(
        OrganizationId organizationId,
        int? withinDays = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        return await _queries
            .GetDeadlinesAsync(organizationId, today, ClampHorizon(withinDays), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContractCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.ContractsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        ContractCommandCenterModel centre = await _queries
            .GetCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // Obligations are the only privileged content on it. Summaries, deadlines
        // and counts name rows without repeating what counsel wrote.
        return centre with
        {
            OverdueObligations = await _redaction
                .ApplyAsync(organizationId, centre.OverdueObligations, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    private static int Clamp(int? limit) =>
        limit is not { } value ? DefaultLimit : Math.Clamp(value, 1, MaximumLimit);

    private static int ClampHorizon(int? days) =>
        days is not { } value ? DefaultHorizonDays : Math.Clamp(value, 1, MaximumHorizonDays);
}
