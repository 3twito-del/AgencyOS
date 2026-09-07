namespace AgencyOS.Contracts.Provisioning;

/// <summary>Headers used by first-run initialization.</summary>
public static class BootstrapHeaders
{
    /// <summary>
    /// Carries the out-of-band bootstrap token.
    /// </summary>
    /// <remarks>
    /// Supplied through deployment configuration, never through the API. On an
    /// uninitialized system there is nobody to authenticate as, so possession of
    /// this token is the only thing standing between an anonymous caller and the
    /// creation of the first owner.
    /// </remarks>
    public const string Token = "X-AgencyOS-Bootstrap-Token";
}

/// <summary>First-run initialization request.</summary>
/// <param name="OrganizationName">Display name of the initial organization.</param>
/// <param name="OrganizationLegalName">Registered legal name, when it differs.</param>
/// <param name="OrganizationType">Agency, Studio, Network, ProductionCompany or Other.</param>
/// <param name="OwnerSubject">Identity-provider subject of the first owner.</param>
/// <param name="OwnerDisplayName">Display name of the first owner.</param>
/// <param name="OwnerEmail">Email address of the first owner.</param>
public sealed record BootstrapRequest(
    string OrganizationName,
    string? OrganizationLegalName,
    string OrganizationType,
    string OwnerSubject,
    string OwnerDisplayName,
    string OwnerEmail);

/// <summary>
/// Result of first-run initialization.
/// </summary>
/// <remarks>
/// Returns identifiers only. No session, token or credential is issued: the owner
/// authenticates through the configured identity provider like anyone else.
/// </remarks>
/// <param name="OrganizationId">The organization that was created.</param>
/// <param name="OwnerUserId">The user granted ownership.</param>
/// <param name="MembershipId">The ownership membership.</param>
/// <param name="InitializedAt">When initialization completed, UTC.</param>
public sealed record BootstrapResponse(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid MembershipId,
    DateTimeOffset InitializedAt);

/// <summary>Reports whether the system has been initialized.</summary>
/// <param name="Initialized">Whether first-run initialization has completed.</param>
public sealed record SystemStatusResponse(bool Initialized);
