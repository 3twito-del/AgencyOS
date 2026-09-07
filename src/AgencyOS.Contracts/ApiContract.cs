namespace AgencyOS.Contracts;

/// <summary>
/// The versioned API contract identity shared by the AgencyOS server and its clients.
/// </summary>
/// <remarks>
/// Governing document: <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. The release
/// authority answers a client handshake with a supported contract range; a client
/// outside that range is refused before it loads business data. That handshake is
/// implemented in M1 — M0 only establishes the identity it negotiates over.
/// </remarks>
public static class ApiContract
{
    /// <summary>
    /// The API contract version this build speaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>build/Version.props</c> holds the authoritative value and stamps it into
    /// assembly metadata. This constant must agree with it; the agreement is
    /// asserted by <c>AgencyOS.Tests.Unit</c> so the two cannot silently diverge.
    /// </para>
    /// <para>
    /// Version 2 added the M2 people slice. Version 3 adds M3 search, saved views,
    /// synchronization, and the optimistic concurrency token on existing records.
    /// </para>
    /// <para>
    /// Both steps are additive, so the supported range stays open at 1. The
    /// concurrency guarantee does not depend on the contract version: the version
    /// token is a required field on guarded mutations, so a client that omits it
    /// gets a 400 rather than a silent overwrite, whatever contract it claims.
    /// </para>
    /// </remarks>
    public const int Current = 3;

    /// <summary>
    /// The lowest contract version this build still serves.
    /// </summary>
    /// <remarks>
    /// Version 1 remains supported because every version-2 change is additive: a
    /// contract-1 client simply does not call the new endpoints. It drops only when
    /// a change makes an older client genuinely unsafe.
    /// </remarks>
    public const int MinimumSupported = 1;
}
