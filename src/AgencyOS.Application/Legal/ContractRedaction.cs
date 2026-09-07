using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Legal;

/// <summary>
/// What a caller may see of a contract.
/// </summary>
/// <remarks>
/// <para>
/// Three rules, applied here and only here.
/// </para>
/// <para>
/// <strong>Terms</strong> need <c>contracts.terms.read</c> to be seen at all, and
/// the figures inside them need <c>deals.economics.read</c> - the same grant M7
/// uses, because it is the same economics. Without the first the term list is
/// empty; without the second the money and percentage terms are gone and the
/// structural ones remain, so somebody scheduling around a delivery date is not
/// shut out of the whole document to protect a fee.
/// </para>
/// <para>
/// <strong>Privileged content</strong> needs <c>contracts.privileged.read</c>.
/// That covers the contract's legal analysis and strategy, and any individual term
/// or obligation a person has classified as legal strategy or attorney-client
/// privileged. The classification is always assigned, never inferred: AgencyOS
/// deciding for itself that something is privileged would be wrong in both
/// directions, and the expensive direction is disclosing what should have been
/// withheld (ADR-0022).
/// </para>
/// <para>
/// Everything is removed rather than marked. A redacted list is simply shorter and
/// carries no count of what was dropped, because "3 terms withheld" tells a reader
/// that three sensitive terms exist - which is most of what they wanted to know.
/// </para>
/// </remarks>
public sealed class ContractRedaction
{
    private readonly TenantGuard _guard;

    public ContractRedaction(TenantGuard guard) => _guard = guard;

    /// <summary>Whether the caller may read drafted terms at all.</summary>
    public Task<bool> MayReadTermsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.ContractTermsRead, organizationId, cancellationToken);

    /// <summary>Whether the caller may read the figures inside them.</summary>
    public Task<bool> MayReadEconomicsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.DealEconomicsRead, organizationId, cancellationToken);

    /// <summary>Whether the caller may read privileged content.</summary>
    public Task<bool> MayReadPrivilegedAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.ContractPrivilegedRead, organizationId, cancellationToken);

    /// <summary>Refuses the caller outright when they may not read terms.</summary>
    /// <remarks>
    /// Used by reconciliation alone, on the M7 precedent: a comparison with the
    /// terms stripped out would report that a draft matched what was negotiated
    /// when it did not, and a false answer is worse than an absence.
    /// </remarks>
    public Task RequireTermsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.ContractTermsRead, organizationId, cancellationToken);

    /// <summary>Reads the three permissions once, for a batch.</summary>
    public async Task<ContractVisibility> ResolveAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        new(
            await MayReadTermsAsync(organizationId, cancellationToken).ConfigureAwait(false),
            await MayReadEconomicsAsync(organizationId, cancellationToken).ConfigureAwait(false),
            await MayReadPrivilegedAsync(organizationId, cancellationToken).ConfigureAwait(false));

    /// <summary>Applies all three rules to a contract's full surface.</summary>
    public async Task<ContractDetailModel> ApplyAsync(
        OrganizationId organizationId,
        ContractDetailModel contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        ContractVisibility visibility =
            await ResolveAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return Apply(contract, visibility);
    }

    /// <summary>Applies the term rules to one version.</summary>
    public async Task<ContractVersionModel?> ApplyAsync(
        OrganizationId organizationId,
        ContractVersionModel? version,
        CancellationToken cancellationToken = default)
    {
        if (version is null)
        {
            return null;
        }

        ContractVisibility visibility =
            await ResolveAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return Apply(version, visibility);
    }

    /// <summary>Applies the privilege rule to a list of obligations with one check.</summary>
    public async Task<IReadOnlyList<ObligationModel>> ApplyAsync(
        OrganizationId organizationId,
        IReadOnlyList<ObligationModel> obligations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obligations);

        return await MayReadPrivilegedAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? obligations
            : [.. obligations.Where(x => !IsPrivileged(x))];
    }

    /// <summary>Applies all three rules with the permissions already decided.</summary>
    public static ContractDetailModel Apply(ContractDetailModel contract, ContractVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(visibility);

        bool privileged = visibility.MayReadPrivileged
            || contract.Privilege is PrivilegeClass.Ordinary or PrivilegeClass.Confidential;

        return contract with
        {
            // Absent, not marked. A "withheld" placeholder would tell the reader
            // that counsel wrote something, which is most of what they wanted.
            LegalAnalysis = privileged ? contract.LegalAnalysis : null,
            StrategyNotes = privileged ? contract.StrategyNotes : null,
            Versions = [.. contract.Versions.Select(version => Apply(version, visibility))],
            Obligations = visibility.MayReadPrivileged
                ? contract.Obligations
                : [.. contract.Obligations.Where(x => !IsPrivileged(x))],
        };
    }

    /// <summary>Applies the term rules to one version with the permissions decided.</summary>
    public static ContractVersionModel Apply(ContractVersionModel version, ContractVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(visibility);

        if (!visibility.MayReadTerms)
        {
            return version with { Terms = [] };
        }

        IEnumerable<ContractTermModel> terms = version.Terms;

        if (!visibility.MayReadEconomics)
        {
            terms = terms.Where(term => !term.IsEconomic);
        }

        if (!visibility.MayReadPrivileged)
        {
            terms = terms.Where(term => !IsPrivileged(term));
        }

        return version with { Terms = [.. terms] };
    }

    /// <summary>
    /// Whether a row was classified above ordinary.
    /// </summary>
    /// <remarks>
    /// Read from the recorded classification, never guessed from the text. A term
    /// containing the word "privileged" is not privileged; a term somebody marked
    /// as privileged is.
    /// </remarks>
    private static bool IsPrivileged(ContractTermModel term) => term.IsPrivileged;

    private static bool IsPrivileged(ObligationModel obligation) => obligation.IsPrivileged;
}

/// <summary>
/// What one caller may see of contracts in one tenant.
/// </summary>
/// <remarks>
/// Resolved once per request and passed down, so a list of fifty contracts asks
/// the permission store three times rather than a hundred and fifty.
/// </remarks>
/// <param name="MayReadTerms">Whether drafted terms are visible at all.</param>
/// <param name="MayReadEconomics">Whether the figures inside them are.</param>
/// <param name="MayReadPrivileged">Whether legal analysis and privileged rows are.</param>
public sealed record ContractVisibility(
    bool MayReadTerms,
    bool MayReadEconomics,
    bool MayReadPrivileged);
