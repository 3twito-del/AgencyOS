using AgencyOS.Application.Authorization;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Representations;

/// <summary>
/// Removes internal judgment from read models for callers who may not see it.
/// </summary>
/// <remarks>
/// <para>
/// A talent profile's positioning and a prospect's strategy are materially more
/// sensitive than the records that carry them (ADR-0017). A caller without
/// <c>talent.notes.read</c> receives those fields <em>absent</em> rather than a
/// refusal, and absent is deliberately indistinguishable from empty: signalling
/// that something was withheld would leak the fact that a note exists.
/// </para>
/// <para>
/// This type exists because redaction is a property of the data, not of the
/// endpoint that happens to return it. M4 shipped it inside the query service
/// first, and a saved Prospects view - which runs the projection directly rather
/// than through that service - returned strategy notes to a caller the prospects
/// endpoint would have redacted them for. One implementation, called from every
/// path that returns these models, is the only arrangement where that class of
/// mistake is a compile-time question rather than a review one.
/// </para>
/// <para>
/// M5 adds a package's strategy under its own permission. It is a separate grant
/// from talent notes rather than the same one, because the populations differ: a
/// coordinator who legitimately reads client positioning has no particular reason
/// to read what the agency thinks its play is on a package (ADR-0019).
/// </para>
/// <para>
/// M6 adds opportunity strategy under a third grant. It is the most sensitive text
/// the system holds - it names who the agency expects to pass and what it will
/// settle for - and it is kept out of the search vector as well, because a
/// redaction that can be defeated by searching for a phrase is not a redaction
/// (ADR-0020).
/// </para>
/// </remarks>
public sealed class SensitiveNotes
{
    private readonly TenantGuard _guard;

    public SensitiveNotes(TenantGuard guard) => _guard = guard;

    /// <summary>Whether the caller may read internal notes in this tenant.</summary>
    public Task<bool> MayReadAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.TalentNotesRead, organizationId, cancellationToken);

    /// <summary>Redacts a talent profile unless the caller may read its positioning.</summary>
    public async Task<TalentDetailModel> ApplyAsync(
        OrganizationId organizationId,
        TalentDetailModel talent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(talent);

        return await MayReadAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? talent
            : Redact(talent);
    }

    /// <summary>Redacts a prospect unless the caller may read its strategy.</summary>
    public async Task<ProspectModel> ApplyAsync(
        OrganizationId organizationId,
        ProspectModel prospect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prospect);

        return await MayReadAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? prospect
            : Redact(prospect);
    }

    /// <summary>Redacts a list of prospects with a single permission check.</summary>
    public async Task<IReadOnlyList<ProspectModel>> ApplyAsync(
        OrganizationId organizationId,
        IReadOnlyList<ProspectModel> prospects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prospects);

        return await MayReadAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? prospects
            : [.. prospects.Select(Redact)];
    }

    /// <summary>Whether the caller may read package strategy in this tenant.</summary>
    public Task<bool> MayReadPackageStrategyAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.PackageStrategyRead, organizationId, cancellationToken);

    /// <summary>Redacts a package unless the caller may read its strategy.</summary>
    public async Task<PackageDetailModel> ApplyAsync(
        OrganizationId organizationId,
        PackageDetailModel package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        return await MayReadPackageStrategyAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? package
            : Redact(package);
    }

    /// <summary>Whether the caller may read opportunity strategy in this tenant.</summary>
    public Task<bool> MayReadOpportunityStrategyAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.HasPermissionAsync(Permission.OpportunityStrategyRead, organizationId, cancellationToken);

    /// <summary>Redacts an opportunity unless the caller may read its strategy.</summary>
    public async Task<OpportunityDetailModel> ApplyAsync(
        OrganizationId organizationId,
        OpportunityDetailModel opportunity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        return await MayReadOpportunityStrategyAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ? opportunity
            : Redact(opportunity);
    }

    /// <summary>The redaction itself, in one place so both models agree on what it means.</summary>
    public static TalentDetailModel Redact(TalentDetailModel talent)
    {
        ArgumentNullException.ThrowIfNull(talent);

        return talent with { PositioningNotes = null };
    }

    /// <inheritdoc cref="Redact(TalentDetailModel)"/>
    public static ProspectModel Redact(ProspectModel prospect)
    {
        ArgumentNullException.ThrowIfNull(prospect);

        return prospect with { StrategyNotes = null };
    }

    /// <inheritdoc cref="Redact(TalentDetailModel)"/>
    public static PackageDetailModel Redact(PackageDetailModel package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return package with { StrategyNotes = null };
    }

    /// <inheritdoc cref="Redact(TalentDetailModel)"/>
    public static OpportunityDetailModel Redact(OpportunityDetailModel opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        return opportunity with { StrategyNotes = null };
    }
}
