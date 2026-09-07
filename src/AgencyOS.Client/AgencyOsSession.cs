using AgencyOS.Contracts;

namespace AgencyOS.Client;

/// <summary>
/// Who this client is, where it is pointed, and which tenant it is working in.
/// </summary>
/// <remarks>
/// <para>
/// The identity fields are asserted to the server on every request; the server
/// decides what this build may do. Nothing here grants anything.
/// </para>
/// <para>
/// <see cref="Subject"/> feeds the M1 development authentication scheme, which
/// the host only accepts on rings that forbid real data. Adopting OIDC replaces
/// how this value is obtained, not how it is used.
/// </para>
/// </remarks>
public sealed class AgencyOsSession
{
    /// <summary>Header carrying the development identity subject.</summary>
    public const string SubjectHeader = "X-AgencyOS-Dev-Subject";

    public AgencyOsSession(
        Uri baseAddress,
        Guid organizationId,
        string? subject = null,
        string platform = "windows-x64",
        string? channel = null,
        string? clientVersion = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        BaseAddress = baseAddress;
        OrganizationId = organizationId;
        Subject = subject;
        Platform = platform;

        // The build's own metadata is the truth about which ring and version this
        // is. Letting a caller override it would let a client misreport itself to
        // the release authority.
        Channel = channel ?? BuildInfo.Channel;
        ClientVersion = clientVersion ?? BuildInfo.Version;
    }

    public Uri BaseAddress { get; }

    /// <summary>The tenant whose records this session works in.</summary>
    public Guid OrganizationId { get; }

    public string? Subject { get; }

    public string Platform { get; }

    public string Channel { get; }

    public string ClientVersion { get; }
}
