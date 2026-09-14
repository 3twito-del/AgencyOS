using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Client;
using AgencyOS.Contracts;
using AgencyOS.Reviewer.Fixture;
using AgencyOS.Reviewer.Reachability;
using AgencyOS.Reviewer.Report;
using AgencyOS.Reviewer.Runtime;
using AgencyOS.Reviewer.Style;
using AgencyOS.Reviewer.Surface;

namespace AgencyOS.Reviewer;

/// <summary>
/// The AgencyOS review harness.
/// </summary>
/// <remarks>
/// <para>
/// Three modes that observe and one that is refused. <c>inventory</c> and
/// <c>observe</c> gather evidence; <c>report</c> renders it; <c>repair</c> exists
/// so that asking for it produces a refusal rather than nothing, which is the
/// difference between a policy and an omission.
/// </para>
/// <para>
/// Nothing here writes to product source. The harness's only outputs are under the
/// run directory, which is ignored by Git.
/// </para>
/// </remarks>
public static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Runs the harness.</summary>
    /// <param name="args">Mode and options.</param>
    /// <returns>Zero on success.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            Usage();

            return 2;
        }

        Dictionary<string, string> options = ParseOptions(args);

        try
        {
            return args[0] switch
            {
                "inventory" => Inventory(options),
                "fixture" => Fixture(options).GetAwaiter().GetResult(),
                "observe" => Observe(options),
                "report" => Render(options),
                "reproduce" => Reproduce(options),
                "repair" => Repair(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception failure) when (failure is IOException or InvalidOperationException
            or HttpRequestException or TimeoutException)
        {
            Console.Error.WriteLine("reviewer: " + failure.Message);

            return 1;
        }
    }

    // ------------------------------------------------------------- inventory

    /// <summary>Builds the static surface map, reachability graph and style inventory.</summary>
    private static int Inventory(IReadOnlyDictionary<string, string> options)
    {
        string repository = Option(options, "repo", Directory.GetCurrentDirectory());
        string output = Option(options, "out", Path.Combine(repository, "artifacts", "reviewer", "static"));
        string contractPath = Option(
            options, "contract", Path.Combine(repository, "artifacts", "openapi", "AgencyOS.Api.json"));

        Directory.CreateDirectory(output);

        Console.WriteLine("Reading " + repository);

        SourceIndex source = SourceIndex.Load(repository);

        Console.WriteLine("  " + source.Files.Count.ToString(CultureInfo.InvariantCulture)
            + " files at " + source.Commit[..Math.Min(12, source.Commit.Length)]);

        IReadOnlyList<CodeBehindFacts> codeBehind = CodeScanner.Scan(source);
        IReadOnlyList<XamlFile> markup = XamlScanner.Scan(source, "src/AgencyOS.Windows/");

        IReadOnlyList<ApiPath> contract = File.Exists(contractPath)
            ? ApiScanner.ReadContract(contractPath)
            : [];

        if (contract.Count == 0)
        {
            Console.Error.WriteLine(
                "  no OpenAPI document at " + contractPath
                    + " — the client/API reconciliation will be empty rather than wrong.");
        }

        IReadOnlyList<ClientRoute> clientRoutes = ApiScanner.ReadClientRoutes(source);

        SurfaceMap map = InventoryBuilder.Build(source, codeBehind, markup, contract, clientRoutes);
        IReadOnlyList<ReachabilityObservation> gaps =
            ReachabilityAnalyzer.Analyze(source, codeBehind, markup, contract, clientRoutes);
        StyleReport style = XamlStyleInventory.Build(markup);
        IReadOnlyList<string> dynamicDispatch = CodeScanner.DynamicDispatchSuspects(source);

        Write(Path.Combine(output, "REVIEW-001-SURFACE-MAP.json"), map);
        Write(Path.Combine(output, "reachability.json"), new
        {
            Observations = gaps,
            DynamicDispatchSuspects = dynamicDispatch,
        });
        Write(Path.Combine(output, "xaml-style.json"), style);
        Write(Path.Combine(output, "client-routes.json"), clientRoutes);
        Write(Path.Combine(output, "code-behind.json"), codeBehind);

        Console.WriteLine("  surfaces      " + map.Surfaces.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  markup files  " + markup.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  contract      " + contract.Count.ToString(CultureInfo.InvariantCulture) + " paths");
        Console.WriteLine("  client routes " + clientRoutes
            .DistinctBy(x => x.Shape, StringComparer.Ordinal).Count()
            .ToString(CultureInfo.InvariantCulture) + " distinct shapes");
        Console.WriteLine("  observations  " + gaps.Count.ToString(CultureInfo.InvariantCulture));

        foreach (IGrouping<string, ReachabilityObservation> group in gaps
            .GroupBy(x => x.Kind, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count()))
        {
            Console.WriteLine("    " + group.Count().ToString(CultureInfo.InvariantCulture).PadLeft(4)
                + "  " + group.Key);
        }

        if (dynamicDispatch.Count > 0)
        {
            Console.WriteLine("  WARNING: dispatch that is not a string literal in "
                + string.Join(", ", dynamicDispatch)
                + " — dead-command findings are unsound for those files.");
        }

        Console.WriteLine("Written to " + output);

        return 0;
    }

    // --------------------------------------------------------------- fixture

    /// <summary>Bootstraps a synthetic tenant and fills it with review data.</summary>
    private static async Task<int> Fixture(IReadOnlyDictionary<string, string> options)
    {
        string api = Option(options, "api", "http://127.0.0.1:5199");
        string token = Option(options, "token", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string output = Option(options, "out", string.Empty);

        if (token.Length == 0)
        {
            Console.Error.WriteLine("reviewer: --token is required; it is the server's bootstrap token.");

            return 2;
        }

        using HttpClient http = new() { BaseAddress = new Uri(api), Timeout = TimeSpan.FromSeconds(60) };

        http.DefaultRequestHeaders.Add("X-AgencyOS-Bootstrap-Token", token);

        // First-run initialization derives the tenant's release policy from the
        // calling client's own identity, so the fixture presents the identity of
        // the build it is about to review rather than inventing one.
        http.DefaultRequestHeaders.Add(ClientHeaders.Platform, "windows-x64");
        http.DefaultRequestHeaders.Add(ClientHeaders.Channel, BuildInfo.Channel);
        http.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, BuildInfo.Version);
        http.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            ApiContract.Current.ToString(CultureInfo.InvariantCulture));
        http.DefaultRequestHeaders.Add(ClientHeaders.BuildId, BuildInfo.BuildId);

        HttpResponseMessage response = await http
            .PostAsJsonAsync(
                "/api/v1/system/bootstrap",
                new
                {
                    OrganizationName = "Review Agency (synthetic)",
                    OrganizationLegalName = "Review Agency Synthetic Ltd",
                    OrganizationType = "Agency",
                    OwnerSubject = subject,
                    OwnerDisplayName = "Review Owner",
                    OwnerEmail = subject + "@review.invalid",
                })
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine("reviewer: bootstrap refused with "
                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ": " + body);

            return 1;
        }

        using JsonDocument bootstrap = JsonDocument.Parse(body);

        Guid organizationId = bootstrap.RootElement.GetProperty("organizationId").GetGuid();
        Guid ownerUserId = bootstrap.RootElement.GetProperty("ownerUserId").GetGuid();

        Console.WriteLine("Synthetic tenant " + organizationId.ToString("D", CultureInfo.InvariantCulture));

        using HttpClient clientHttp = new() { BaseAddress = new Uri(api), Timeout = TimeSpan.FromSeconds(60) };

        AgencyOsSession session = new(new Uri(api), organizationId, subject);
        AgencyOsApiClient client = new(clientHttp, session);

        SyntheticFixture fixture = new(client);
        FixtureReport report = await fixture.BuildAsync(organizationId, ownerUserId).ConfigureAwait(false);

        Console.WriteLine("  created " + report.Created.Count.ToString(CultureInfo.InvariantCulture)
            + ", refused " + report.Refused.Count.ToString(CultureInfo.InvariantCulture));

        foreach (string refusal in report.Refused)
        {
            Console.WriteLine("    refused: " + refusal);
        }

        if (output.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Write(output, report);
        }

        Console.WriteLine("AGENCYOS_ORGANIZATION_ID=" + organizationId.ToString("D", CultureInfo.InvariantCulture));

        return 0;
    }

    // --------------------------------------------------------------- observe

    /// <summary>Drives the running client and records what it saw.</summary>
    private static int Observe(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string runId = Option(options, "run", "REVIEW-001");

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: observe needs --exe and --out.");

            return 2;
        }

        Directory.CreateDirectory(runDirectory);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["AGENCYOS_API_BASE"] = apiBase,
            ["AGENCYOS_ORGANIZATION_ID"] = organization,
            ["AGENCYOS_DEV_SUBJECT"] = subject,
        };

        DateTimeOffset started = DateTimeOffset.UtcNow;

        Console.WriteLine("Launching " + executable);

        using ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));

        Console.WriteLine("  pid " + app.ProcessId.ToString(CultureInfo.InvariantCulture));

        app.Focus();
        app.Resize(1600, 1000);

        AuditPass pass = new(app, runDirectory);

        Console.WriteLine("  walking workspaces");
        IReadOnlyList<SurfaceEvidence> workspaces = pass.WalkWorkspaces();

        Console.WriteLine("  probing overlays");
        IReadOnlyList<SurfaceEvidence> overlays = pass.ProbeOverlays();

        Console.WriteLine("  probing declared gestures");
        IReadOnlyList<GestureProbe> gestures = pass.ProbeGestures();

        Console.WriteLine("  narrow-window layout");
        List<SurfaceEvidence> layout =
        [
            pass.ProbeNarrowWindow("Command Center", 900, 700),
            pass.ProbeNarrowWindow("Deals", 900, 700),
        ];

        RuntimeReport report = new(
            runId,
            started,
            DateTimeOffset.UtcNow,
            executable,
            app.ProcessId,
            app.Size(),
            environment,
            [.. workspaces, .. layout],
            gestures,
            overlays,
            app.Keys.RefusedSends);

        Write(Path.Combine(runDirectory, "runtime.json"), report);

        int visited = workspaces.Count(x => x.Visited);

        Console.WriteLine("  visited " + visited.ToString(CultureInfo.InvariantCulture) + "/"
            + workspaces.Count.ToString(CultureInfo.InvariantCulture) + " workspaces");
        Console.WriteLine("  gestures matched " + gestures.Count(x => x.Matched)
            .ToString(CultureInfo.InvariantCulture) + "/"
            + gestures.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  accessibility observations " + workspaces
            .Sum(x => x.Accessibility.Count).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Written to " + runDirectory);

        return 0;
    }

    // ---------------------------------------------------------------- report

    /// <summary>Renders the audit outputs from gathered evidence.</summary>
    private static int Render(IReadOnlyDictionary<string, string> options)
    {
        string runDirectory = Option(options, "out", string.Empty);
        string staticDirectory = Option(options, "static", Path.Combine(runDirectory, "..", "static"));

        if (runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: report needs --out.");

            return 2;
        }

        ReportWriter.Render(runDirectory, staticDirectory);

        Console.WriteLine("Rendered into " + runDirectory);

        return 0;
    }

    // ------------------------------------------------------------- reproduce

    private static int Reproduce(IReadOnlyDictionary<string, string> options)
    {
        string finding = Option(options, "finding", string.Empty);

        if (finding.Length == 0)
        {
            Console.Error.WriteLine("reviewer: reproduce needs --finding <id>.");

            return 2;
        }

        string runDirectory = Option(options, "out", string.Empty);
        string ledgerPath = Path.Combine(runDirectory, "REVIEW-001-FINDINGS.json");

        if (!File.Exists(ledgerPath))
        {
            Console.Error.WriteLine("reviewer: no ledger at " + ledgerPath + ".");

            return 1;
        }

        using JsonDocument ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));

        foreach (JsonElement entry in ledger.RootElement.GetProperty("findings").EnumerateArray())
        {
            if (!string.Equals(entry.GetProperty("id").GetString(), finding, StringComparison.Ordinal))
            {
                continue;
            }

            Console.WriteLine(JsonSerializer.Serialize(entry, Json));

            return 0;
        }

        Console.Error.WriteLine("reviewer: no finding " + finding + " in the ledger.");

        return 1;
    }

    // ---------------------------------------------------------------- repair

    /// <summary>
    /// Refuses. Repair is a later mode and is never reachable from an audit.
    /// </summary>
    /// <remarks>
    /// Present rather than absent so that the boundary is a stated policy the
    /// harness enforces, not something a future contributor has to infer.
    /// </remarks>
    private static int Repair()
    {
        Console.Error.WriteLine(
            "reviewer: repair is not available. Audit Run 001 establishes an unbiased baseline and "
                + "applies no product change. Repair runs only against explicitly approved finding "
                + "identifiers, in a later pass.");

        return 3;
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine("reviewer: unknown mode '" + mode + "'.");
        Usage();

        return 2;
    }

    private static void Usage()
    {
        Console.WriteLine("AgencyOS review harness");
        Console.WriteLine();
        Console.WriteLine("  inventory  --repo <path> [--out <dir>] [--contract <openapi.json>]");
        Console.WriteLine("  fixture    --api <url> --token <bootstrap> [--subject <s>] [--out <file>]");
        Console.WriteLine("  observe    --exe <path> --out <run-dir> --org <guid> [--api <url>] [--subject <s>]");
        Console.WriteLine("  report     --out <run-dir> [--static <dir>]");
        Console.WriteLine("  reproduce  --finding <id> --out <run-dir>");
        Console.WriteLine("  repair     refused during an audit");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);

        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string name = args[i][2..];
            string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";

            options[name] = value;
        }

        return options;
    }

    private static string Option(IReadOnlyDictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out string? value) ? value : fallback;

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }
}
