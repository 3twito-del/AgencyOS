namespace AgencyOS.Domain.Releases;

/// <summary>
/// What a client asserts about itself before it is allowed to touch business data.
/// </summary>
/// <remarks>
/// These are the fields the client presents in
/// <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. They are assertions, not proof: the
/// server decides what a client asserting them may do. A client cannot widen its
/// own authority by lying, because every answer is computed from the server's
/// policy row rather than from anything the client sent.
/// </remarks>
/// <param name="Platform">Platform moniker, for example <c>windows-x64</c>.</param>
/// <param name="Ring">The release ring the client believes it belongs to.</param>
/// <param name="Version">The client build version.</param>
/// <param name="ApiContractVersion">The API contract version the client speaks.</param>
/// <param name="BuildId">Build identifier.</param>
/// <param name="GitCommit">Source commit.</param>
/// <param name="LocalSchemaVersion">Local cache schema version.</param>
/// <param name="WindowsBuild">Reported Windows build.</param>
public sealed record ClientIdentity(
    string Platform,
    ReleaseRing Ring,
    ClientVersion Version,
    int ApiContractVersion,
    string? BuildId = null,
    string? GitCommit = null,
    int? LocalSchemaVersion = null,
    string? WindowsBuild = null);
