using System;
using System.Net.Http;
using AgencyOS.Client;

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
