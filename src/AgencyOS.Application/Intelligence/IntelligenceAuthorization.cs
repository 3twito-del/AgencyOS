using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Intelligence;

/// <summary>
/// Who may read and write intelligence, and at what classification.
/// </summary>
/// <remarks>
/// <para>
/// The shape M10 established for documents, applied to a subject that is arguably
/// more sensitive. A document is usually something somebody sent the agency; a
/// thesis is what the agency privately thinks, and a source-sensitive signal is a
/// promise somebody made to a person who spoke to them (ADR-0030).
/// </para>
/// <para>
/// <strong>Lists are narrowed inside the query</strong>, before anything is counted
/// or paged. Filtering afterwards leaks the count, and "3 hidden signals about this
/// person" is itself the disclosure.
/// </para>
/// </remarks>
public sealed class IntelligenceAuthorization
{
    private readonly TenantGuard _guard;

    public IntelligenceAuthorization(TenantGuard guard) => _guard = guard;

    /// <summary>
    /// The one grant that covers the three elevated classifications.
    /// </summary>
    /// <remarks>
    /// Confidential, source-sensitive and restricted share a door on the M10
    /// precedent. Three separate grants would in practice be given to the same
    /// people and would triple the chance of granting the wrong one; what varies
    /// between them is what they protect, not who should see it.
    /// </remarks>
    public static string? ElevatedGrantFor(IntelligenceSensitivity sensitivity) => sensitivity switch
    {
        IntelligenceSensitivity.Confidential => Permission.IntelligenceSensitiveRead,
        IntelligenceSensitivity.SourceSensitive => Permission.IntelligenceSensitiveRead,
        IntelligenceSensitivity.Restricted => Permission.IntelligenceSensitiveRead,
        _ => null,
    };

    /// <summary>Authorizes reading the intelligence surface at all.</summary>
    public Task<UserId> AuthorizeReadAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.IntelligenceRead, organizationId, cancellationToken);

    /// <summary>
    /// Authorizes reading one item, including its classification.
    /// </summary>
    /// <remarks>
    /// Refuses rather than redacts, as documents do. A reader who followed a
    /// reference to a thesis they may not open learns that a grant exists to ask
    /// for, rather than concluding the agency never formed a view.
    /// </remarks>
    public async Task<UserId> AuthorizeReadAsync(
        OrganizationId organizationId,
        IntelligenceSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>Authorizes recording or revising intelligence.</summary>
    public Task<UserId> AuthorizeWriteAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(Permission.IntelligenceWrite, organizationId, cancellationToken);

    /// <summary>
    /// Authorizes recording something at a given classification.
    /// </summary>
    /// <remarks>
    /// A writer may not file intelligence into a classification they could not then
    /// read. Otherwise anybody could hide their own work from themselves and — the
    /// case that matters — could move somebody else's thesis out of their reach
    /// (ADR-0025, ADR-0030).
    /// </remarks>
    public async Task<UserId> AuthorizeClassifyAsync(
        OrganizationId organizationId,
        IntelligenceSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await AuthorizeWriteAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>
    /// Authorizes stating or resolving a forecast.
    /// </summary>
    /// <remarks>
    /// Its own grant because a probability carries the forecaster's name into the
    /// agency's calibration record. Recording what happened and staking a
    /// reputation on what will happen are different acts.
    /// </remarks>
    public async Task<UserId> AuthorizeForecastAsync(
        OrganizationId organizationId,
        IntelligenceSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.IntelligencePredictionsWrite, organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>Authorizes working the talent radar.</summary>
    /// <remarks>
    /// Held apart because the radar concerns people who do not know they are being
    /// discussed, and because converting an entry reaches into M4 and creates a
    /// real prospect.
    /// </remarks>
    public async Task<UserId> AuthorizeRadarAsync(
        OrganizationId organizationId,
        IntelligenceSensitivity sensitivity,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(Permission.IntelligenceRadarWrite, organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (ElevatedGrantFor(sensitivity) is { } grant)
        {
            await _guard.AuthorizeAsync(grant, organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return actor;
    }

    /// <summary>
    /// The classifications a caller may see, for narrowing a list.
    /// </summary>
    /// <remarks>
    /// Used by the query layer <em>inside</em> the SQL, before counting, ranking or
    /// paging. Filtering afterwards would leak the count, and a count of
    /// source-sensitive signals about a named person is itself a disclosure
    /// (ADR-0030).
    /// </remarks>
    public async Task<IReadOnlySet<IntelligenceSensitivity>> ReadableSensitivitiesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        HashSet<IntelligenceSensitivity> readable = [IntelligenceSensitivity.Internal];

        if (await _guard
            .HasPermissionAsync(
                Permission.IntelligenceSensitiveRead, organizationId, cancellationToken)
            .ConfigureAwait(false))
        {
            readable.Add(IntelligenceSensitivity.Confidential);
            readable.Add(IntelligenceSensitivity.SourceSensitive);
            readable.Add(IntelligenceSensitivity.Restricted);
        }

        return readable;
    }

    /// <summary>Whether the caller may read a given classification.</summary>
    public async Task<bool> CanReadAsync(
        OrganizationId organizationId,
        IntelligenceSensitivity sensitivity,
        CancellationToken cancellationToken = default) =>
        (await ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Contains(sensitivity);
}
