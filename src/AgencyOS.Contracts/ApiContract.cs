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
    /// <c>build/Version.props</c> holds the authoritative value and stamps it into
    /// assembly metadata. This constant must agree with it; the agreement is
    /// asserted by <c>AgencyOS.Tests.Unit</c> so the two cannot silently diverge.
    /// </remarks>
    public const int Current = 1;
}
