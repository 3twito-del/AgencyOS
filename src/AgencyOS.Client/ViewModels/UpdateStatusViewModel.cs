using AgencyOS.Contracts;
using AgencyOS.Contracts.Releases;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// How the release authority's answer reads on screen.
/// </summary>
/// <remarks>
/// <para>
/// The five states were settled in M0 and enforced server-side ever since. M13
/// adds no policy and changes no enforcement: this file decides only what a
/// person is told, and every sentence has to be true of what the server will
/// actually do next (ADR-0034, docs/06_FORCED_UPDATE_PROTOCOL.md).
/// </para>
/// <para>
/// The two blocking states are the ones worth getting right. A client that
/// softened <c>Mandatory</c> into a suggestion would be describing a system that
/// is about to refuse the user's next write, and a client that dramatised
/// <c>Available</c> into a warning would train people to ignore the ones that
/// matter.
/// </para>
/// </remarks>
public static class UpdateFormatting
{
    /// <summary>What the state is called.</summary>
    public static string Headline(string? policy) => policy switch
    {
        "None" => "Up to date",
        "Available" => "An update is available",
        "Recommended" => "An update is recommended",
        "Mandatory" => "An update is required",
        "Revoked" => "This build has been withdrawn",
        _ => "Update status unknown",
    };

    /// <summary>
    /// What it means for the person's work.
    /// </summary>
    /// <remarks>
    /// Stated in terms of what will happen rather than in terms of severity. "You
    /// can keep working" and "changes will be refused" are facts somebody can act
    /// on; "important" and "critical" are adjectives they have to interpret.
    /// </remarks>
    public static string Consequence(string? policy) => policy switch
    {
        "None" => "Nothing to do.",
        "Available" => "You can keep working. Update whenever it suits you.",
        "Recommended" =>
            "You can keep working. This update is advised, and updating soon avoids "
                + "being interrupted later.",
        "Mandatory" =>
            "Changes are refused until this build is updated. Reading is unaffected.",
        // Deliberately the same verb as Mandatory. The two states differ in why,
        // not in what happens next, and giving them different vocabulary would
        // make a reader work out whether they mean the same thing.
        "Revoked" =>
            "Changes are refused because this build has been withdrawn. Reading is "
                + "unaffected.",
        _ =>
            "AgencyOS could not reach the release authority, so it does not know "
                + "whether this build is current.",
    };

    /// <summary>
    /// Whether the server will refuse this build's writes.
    /// </summary>
    /// <remarks>
    /// Read from the server's own <c>BlocksProtectedMutations</c> rather than
    /// re-derived from the policy name. The client does not decide this and must
    /// not appear to: if the two ever disagreed, the server is right and the
    /// screen would be lying.
    /// </remarks>
    public static bool BlocksWork(HandshakeResponse? handshake) =>
        handshake?.BlocksProtectedMutations ?? false;

    /// <summary>
    /// How prominent the notice should be.
    /// </summary>
    /// <remarks>
    /// Three levels rather than five, because <c>None</c> and <c>Available</c> are
    /// the same to a person who is working and <c>Mandatory</c> and <c>Revoked</c>
    /// both mean the same thing: their next change will be refused.
    /// </remarks>
    public static string Severity(string? policy) => policy switch
    {
        "Mandatory" or "Revoked" => "Blocking",
        "Recommended" => "Advisory",
        _ => "Informational",
    };

    /// <summary>
    /// What to do about it, when there is something to do.
    /// </summary>
    /// <remarks>
    /// A recovery line rather than a button label. AgencyOS does not install its
    /// own updates in this build, so telling somebody the app will handle it would
    /// be untrue; naming the version they need is what actually helps.
    /// </remarks>
    public static string Guidance(HandshakeResponse? handshake)
    {
        if (handshake is null)
        {
            return "Check your connection and try again. Your work is unaffected.";
        }

        return handshake.Policy switch
        {
            "None" => string.Empty,

            "Revoked" when handshake.RollbackTarget is { Length: > 0 } target =>
                $"Install {target}, which the release authority has designated for "
                    + "this channel.",

            "Revoked" => handshake.LatestVersion is { Length: > 0 } latest
                ? $"Install {latest} to continue working."
                : "Ask your administrator which build to install.",

            "Mandatory" => handshake.MinimumSupportedVersion is { Length: > 0 } minimum
                ? $"Install {minimum} or newer to continue making changes."
                : "Install the current build to continue making changes.",

            _ => handshake.LatestVersion is { Length: > 0 } available
                ? $"The current build is {available}."
                : string.Empty,
        };
    }
}

/// <summary>
/// What the release authority says about this build.
/// </summary>
/// <remarks>
/// <para>
/// A read-only view over the existing handshake. There is deliberately no method
/// here that overrides, dismisses or defers a policy: enforcement is the server's,
/// and a client that could set any of this aside would be the client the forced
/// update protocol exists to prevent (§T).
/// </para>
/// <para>
/// An unreachable authority is its own state rather than an assumed "fine". A
/// build that treated silence as permission would keep working through exactly
/// the outage during which a kill switch was flipped.
/// </para>
/// </remarks>
public sealed class UpdateStatusViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private HandshakeResponse? _handshake;

    public UpdateStatusViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>The version this build reports.</summary>
    public static string InstalledVersion => BuildInfo.Version;

    /// <summary>The channel this build reports.</summary>
    public static string Channel => BuildInfo.Channel;

    /// <summary>The contract version this build speaks.</summary>
    public static int ContractVersion => ApiContract.Current;

    /// <summary>The authority's answer, or null when it could not be reached.</summary>
    public HandshakeResponse? Handshake
    {
        get => _handshake;
        private set
        {
            if (Set(ref _handshake, value))
            {
                OnPropertyChanged(nameof(Headline));
                OnPropertyChanged(nameof(Consequence));
                OnPropertyChanged(nameof(Severity));
                OnPropertyChanged(nameof(Guidance));
                OnPropertyChanged(nameof(BlocksWork));
                OnPropertyChanged(nameof(RequiresAttention));
            }
        }
    }

    public string Headline => UpdateFormatting.Headline(_handshake?.Policy);

    public string Consequence => UpdateFormatting.Consequence(_handshake?.Policy);

    public string Severity => UpdateFormatting.Severity(_handshake?.Policy);

    public string Guidance => UpdateFormatting.Guidance(_handshake);

    /// <summary>Whether the server will refuse this build's changes.</summary>
    public bool BlocksWork => UpdateFormatting.BlocksWork(_handshake);

    /// <summary>Whether the notice should be shown at all.</summary>
    /// <remarks>
    /// A current build says nothing. An update notice that is always present is an
    /// update notice nobody reads.
    /// </remarks>
    public bool RequiresAttention =>
        _handshake is not null && _handshake.Policy != "None";

    public override bool IsEmpty => _loaded && _handshake is null;

    /// <summary>Asks the release authority where this build stands.</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Handshake = await _api.HandshakeAsync(token).ConfigureAwait(true);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}
