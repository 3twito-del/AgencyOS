using System;
using System.IO;
using System.Net.Http;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.Sync;
using AgencyOS.Contracts;

namespace AgencyOS.Windows;

/// <summary>
/// The client's composition root.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny. The Windows assembly holds views; everything with behaviour
/// lives in <c>AgencyOS.Client</c>, which references only the versioned contracts.
/// That is what keeps the compile-time rule from M0 true: this client cannot reach
/// persistence, application or domain code even by accident.
/// </para>
/// <para>
/// Connection settings come from the environment so a developer can point a build
/// at a LAB or ALPHA endpoint without editing code. There is no default tenant:
/// working in the wrong tenant is exactly the mistake the ring and tenant models
/// exist to prevent, so an unset tenant is an error the shell shows rather than a
/// value it guesses.
/// </para>
/// </remarks>
internal static class AppServices
{
    private const string BaseAddressVariable = "AGENCYOS_API_BASE";
    private const string OrganizationVariable = "AGENCYOS_ORGANIZATION_ID";
    private const string SubjectVariable = "AGENCYOS_DEV_SUBJECT";

    private static readonly Lazy<AppConfiguration> Configuration = new(Read);

    private static HttpClient? _http;
    private static IAgencyOsApi? _api;
    private static LocalCache? _cache;
    private static SyncEngine? _sync;
    private static string? _cacheFailure;

    /// <summary>Gets the resolved connection settings, valid or not.</summary>
    public static AppConfiguration Settings => Configuration.Value;

    /// <summary>Gets the API client, or null when the client is not configured.</summary>
    public static IAgencyOsApi? Api
    {
        get
        {
            if (!Settings.IsValid)
            {
                return null;
            }

            if (_api is not null)
            {
                return _api;
            }

            _http = new HttpClient { BaseAddress = Settings.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };

            AgencyOsSession session = new(
                Settings.BaseAddress!,
                Settings.OrganizationId,
                Settings.Subject);

            _api = new AgencyOsApiClient(_http, session);
            return _api;
        }
    }

    /// <summary>
    /// Gets the encrypted local cache, or null when there is none to open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One cache per channel, tenant and user, so a FORGE build can never read an
    /// ALPHA cache and two users on one workstation never share a file
    /// (<c>docs/05_RELEASE_RINGS.md</c>).
    /// </para>
    /// <para>
    /// A cache that cannot be opened is not fatal. It holds nothing canonical, so
    /// the client runs online-only and says why, rather than refusing to start.
    /// </para>
    /// </remarks>
    public static LocalCache? Cache
    {
        get
        {
            if (_cache is not null || !Settings.IsValid)
            {
                return _cache;
            }

            try
            {
                LocalCacheIdentity identity = new(
                    BuildInfo.Channel,
                    Settings.OrganizationId,
                    Settings.Subject ?? "unknown");

                _cache = LocalCache.Open(CacheRoot, identity, new DpapiCacheKeyProvider());
                _cacheFailure = null;
            }
            catch (LocalCacheUnusableException exception)
            {
                _cacheFailure = exception.Message;
            }

            return _cache;
        }
    }

    /// <summary>Gets the synchronization engine, or null when there is no cache to fill.</summary>
    public static SyncEngine? Sync
    {
        get
        {
            if (_sync is not null)
            {
                return _sync;
            }

            if (Api is not { } api || Cache is not { } cache)
            {
                return null;
            }

            _sync = new SyncEngine(api, cache);
            return _sync;
        }
    }

    /// <summary>Explains why the cache is unavailable, when it is.</summary>
    public static string? CacheFailure => _cacheFailure;

    /// <summary>Where caches live: per user, under local application data.</summary>
    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgencyOS",
        "cache");

    /// <summary>
    /// Deletes this identity's cache and everything queued in it.
    /// </summary>
    /// <remarks>
    /// Safe because nothing here is canonical: the next synchronization rebuilds it
    /// from the change feed. Queued commands go with it, which is why the surface
    /// offering this states how many are outstanding first.
    /// </remarks>
    public static void ResetCache()
    {
        if (!Settings.IsValid)
        {
            return;
        }

        _cache?.Dispose();
        _cache = null;
        _sync = null;

        LocalCache.Reset(
            CacheRoot,
            new LocalCacheIdentity(BuildInfo.Channel, Settings.OrganizationId, Settings.Subject ?? "unknown"),
            new DpapiCacheKeyProvider());
    }

    private static AppConfiguration Read()
    {
        string? baseAddressText = Environment.GetEnvironmentVariable(BaseAddressVariable);
        string? organizationText = Environment.GetEnvironmentVariable(OrganizationVariable);
        string? subject = Environment.GetEnvironmentVariable(SubjectVariable);

        Uri? baseAddress = null;

        if (!string.IsNullOrWhiteSpace(baseAddressText))
        {
            Uri.TryCreate(baseAddressText, UriKind.Absolute, out baseAddress);
        }

        _ = Guid.TryParse(organizationText, out Guid organizationId);

        return new AppConfiguration(baseAddress, organizationId, subject);
    }
}

/// <summary>Connection settings read from the environment.</summary>
/// <param name="BaseAddress">API root, for example <c>http://localhost:5199</c>.</param>
/// <param name="OrganizationId">Tenant to work in.</param>
/// <param name="Subject">Development identity subject.</param>
internal sealed record AppConfiguration(Uri? BaseAddress, Guid OrganizationId, string? Subject)
{
    public bool IsValid => BaseAddress is not null && OrganizationId != Guid.Empty;

    /// <summary>Explains what is missing, for the shell's error state.</summary>
    public string Describe()
    {
        if (BaseAddress is null)
        {
            return "Set AGENCYOS_API_BASE to the AgencyOS API root, for example http://localhost:5199";
        }

        if (OrganizationId == Guid.Empty)
        {
            return "Set AGENCYOS_ORGANIZATION_ID to the tenant this client should work in.";
        }

        return string.IsNullOrWhiteSpace(Subject)
            ? "Set AGENCYOS_DEV_SUBJECT to authenticate against a development ring."
            : $"{BaseAddress} · tenant {OrganizationId:D} · {Subject}";
    }
}
