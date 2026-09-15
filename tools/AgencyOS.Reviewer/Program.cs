using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Client;
using AgencyOS.Client.Commands;
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

        // Before anything measures a rectangle. A process that has not claimed DPI
        // awareness is told the display runs at 96 whatever it is really doing,
        // and every effective-unit conclusion drawn from that is wrong.
        Native.SetProcessDpiAwarenessContext(Native.PerMonitorAwareV2);

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
                "personas" => Personas(options).GetAwaiter().GetResult(),
                "observe" => Observe(options),
                "layout" => Layout(options),
                "rebaseline" => Rebaseline(options),
                "dialogs" => Dialogs(options),
                "dialog-runtime" => DialogRuntime(options),
                "tab-probe" => TabProbeMode(options),
                "opener-probe" => OpenerProbeMode(options),
                "reach-probe" => ReachProbeMode(options),
                "validation" => Validation(options),
                "sync" => Sync(options),
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

    // -------------------------------------------------------------- tab-probe

    /// <summary>
    /// Establishes, step by step, why a tab-hosted dialog does not open.
    /// </summary>
    /// <remarks>
    /// Audit 002 could not separate "the page loses its state when a palette
    /// command runs" from "the harness never established that state". This walks
    /// the sequence and reports which step failed, so the answer is evidence
    /// rather than an absence.
    /// </remarks>
    private static int TabProbeMode(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: tab-probe needs --exe and --out.");

            return 2;
        }

        Directory.CreateDirectory(runDirectory);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["AGENCYOS_API_BASE"] = apiBase,
            ["AGENCYOS_ORGANIZATION_ID"] = organization,
            ["AGENCYOS_DEV_SUBJECT"] = subject,
        };

        using ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));

        TabProbe probe = new(app, runDirectory);

        // The cases this probe was written for, and any the caller names instead.
        // A parameter rather than more navigation logic: the probe's sequence is
        // fixed and proven, and what varies is only where to point it.
        (string Workspace, string Tab, string Command)[] cases =
        [
            ("Intelligence", "Theses", "intelligence.thesis.revise"),
            ("Intelligence", "Sources", "intelligence.source.record"),
            ("Intelligence", "Predictions", "intelligence.prediction.resolve"),
            ("Contracts", "Obligations", "obligation.resolve"),
        ];

        if (Option(options, "cases", string.Empty) is { Length: > 0 } requested)
        {
            cases =
            [
                .. requested
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.Split('/', StringSplitOptions.TrimEntries))
                    .Where(x => x.Length == 3)
                    .Select(x => (x[0], x[1], x[2])),
            ];
        }

        List<TabProbeResult> results = [];

        foreach ((string workspace, string tab, string command) in cases)
        {
            TabProbeResult result = probe.Probe(workspace, tab, command);

            results.Add(result);

            Console.WriteLine();
            Console.WriteLine("== " + workspace + " / " + tab + " / " + command);

            foreach (string step in result.Steps)
            {
                Console.WriteLine("   " + step);
            }
        }

        Write(Path.Combine(runDirectory, "tab-probe.json"), results);

        return 0;
    }

    // ------------------------------------------------------------------- sync

    /// <summary>Presses F9 and reports whether the status line answers.</summary>
    /// <remarks>
    /// Audit 002 §14. The residual on <c>AOS-R001-020</c> is not about navigation
    /// - that was settled from source - but about acknowledgement, and the
    /// footer's own caption is the acknowledgement the shell offers.
    /// </remarks>
    private static int Sync(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: sync needs --exe and --out.");

            return 2;
        }

        Directory.CreateDirectory(runDirectory);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["AGENCYOS_API_BASE"] = Option(options, "api", "http://127.0.0.1:5199"),
            ["AGENCYOS_ORGANIZATION_ID"] = Option(options, "org", string.Empty),
            ["AGENCYOS_DEV_SUBJECT"] = Option(options, "subject", "review-owner"),
        };

        using ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));

        app.Focus();
        Thread.Sleep(2500);
        app.Refresh();

        static string? Line(ReviewApp app) =>
            app.Snapshot().Flatten().FirstOrDefault(x => x.AutomationId == "SyncText")?.Name;

        string? before = Line(app);

        app.Capture(Path.Combine(runDirectory, "sync-before.png"));

        bool pressed = app.Keys.PressFunction(9);

        Thread.Sleep(3000);
        app.Refresh();

        string? after = Line(app);

        app.Capture(Path.Combine(runDirectory, "sync-after.png"));

        Write(Path.Combine(runDirectory, "sync.json"), new
        {
            TakenUtc = DateTimeOffset.UtcNow,
            F9Accepted = pressed,
            StatusLineBefore = before,
            StatusLineAfter = after,
            Changed = !string.Equals(before, after, StringComparison.Ordinal),
        });

        Console.WriteLine("  F9 accepted by the input layer: " + (pressed ? "yes" : "no"));
        Console.WriteLine("  status line before: " + (before ?? "«not found»"));
        Console.WriteLine("  status line after:  " + (after ?? "«not found»"));
        Console.WriteLine("  changed:            "
            + (string.Equals(before, after, StringComparison.Ordinal) ? "NO" : "yes"));

        return 0;
    }

    // ------------------------------------------------------------- validation

    /// <summary>
    /// Submits dialogs that are not ready and records what they say about it.
    /// </summary>
    /// <remarks>
    /// Audit 002 §8 forbids inferring an accessible association from screen
    /// proximity, so this asks the dialog instead of asking the layout: it
    /// submits an empty form, then looks for prose that was not there before and
    /// for a declared association leading to it.
    /// </remarks>
    private static int Validation(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string repository = Option(options, "repo", Directory.GetCurrentDirectory());
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string only = Option(options, "only", string.Empty);

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: validation needs --exe and --out.");

            return 2;
        }

        IReadOnlyList<DialogRecord> inventory = DialogScanner.Scan(SourceIndex.Load(repository));

        if (only.Length > 0)
        {
            HashSet<string> wanted = new(
                only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            inventory = [.. inventory.Where(x => wanted.Contains(x.DialogId))];
        }

        Directory.CreateDirectory(runDirectory);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["AGENCYOS_API_BASE"] = apiBase,
            ["AGENCYOS_ORGANIZATION_ID"] = organization,
            ["AGENCYOS_DEV_SUBJECT"] = subject,
        };

        using ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));

        DialogPass pass = new(app, runDirectory);
        ValidationProbe probe = new(app, pass, runDirectory);
        List<ValidationObservation> observations = [];

        foreach (DialogRecord dialog in inventory)
        {
            ValidationObservation observation = probe.Probe(dialog);

            observations.Add(observation);

            Console.WriteLine("  " + observation.Verdict.PadRight(20)
                + dialog.DialogId.PadRight(34)
                + "primary=" + (observation.PrimaryEnabledWhenEmpty ? "enabled " : "disabled")
                + "  new-text=" + observation.MessagesThatAppeared.Count
                    .ToString(CultureInfo.InvariantCulture)
                + "  " + (observation.TypedTextSurvived ?? string.Empty));
        }

        Write(Path.Combine(runDirectory, "validation.json"), new
        {
            RunId = Option(options, "run", "AUDIT-002C"),
            TakenUtc = DateTimeOffset.UtcNow,
            Observations = observations,
        });

        Console.WriteLine();

        foreach (IGrouping<string, ValidationObservation> group in observations
            .GroupBy(x => x.Verdict, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count()))
        {
            Console.WriteLine("  " + group.Key.PadRight(22)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        return 0;
    }

    // -------------------------------------------------------- dialog-runtime

    /// <summary>
    /// Opens every reachable dialog from the real interface and operates it.
    /// </summary>
    /// <remarks>
    /// The gate Audit 002 exists for. Audit 001 inventoried 61 dialogs and opened
    /// none; constructing one from a test would skip the precondition, the button
    /// and the enabling rule, which are most of what decides whether a person can
    /// use it.
    /// </remarks>
    private static int DialogRuntime(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string repository = Option(options, "repo", Directory.GetCurrentDirectory());
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string only = Option(options, "only", string.Empty);

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: dialog-runtime needs --exe and --out.");

            return 2;
        }

        IReadOnlyList<DialogRecord> inventory = DialogScanner.Scan(SourceIndex.Load(repository));

        if (only.Length > 0)
        {
            HashSet<string> wanted = new(
                only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            inventory = [.. inventory.Where(x => wanted.Contains(x.DialogId))];
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

        ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));
        DialogPass pass = new(app, runDirectory);
        List<DialogObservation> observations = [];
        List<string> closures = [];

        foreach (DialogRecord dialog in inventory)
        {
            DialogObservation observation = pass.Operate(dialog);

            observations.Add(observation);

            // A closed application is an observation about that dialog and a
            // reason to start again, not a reason to abandon the rest. Ending
            // the run here would cost fifty dialogs for one event.
            // Stop while the evidence is still intact. Phase C's traversal ran
            // the volume to zero and died mid-dialog, which costs the run rather
            // than a dialog.
            long free = Native.FreeSpaceBytes(Path.GetFullPath(runDirectory));

            if (free >= 0 && free < 5L * 1024 * 1024 * 1024)
            {
                Console.Error.WriteLine(
                    "reviewer: only "
                        + (free / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
                        + " MB free where this run is writing. Stopping at "
                        + dialog.DialogId + " so the evidence already gathered survives.");

                break;
            }

            if (observation.Outcome is "APPLICATION_CLOSED" or "WINDOW_UNREACHABLE")
            {
                closures.Add(dialog.DialogId + " (" + observation.Outcome + ")");

                Console.Error.WriteLine(
                    "reviewer: the application closed at " + dialog.DialogId
                        + "; starting it again and carrying on.");

                app.Dispose();

                app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));
                pass = new DialogPass(app, runDirectory);
            }

            Console.WriteLine("  " + observation.Outcome.PadRight(22) + dialog.DialogId.PadRight(34)
                + observation.How.PadRight(9)
                + (observation.Outcome == "OPENED"
                    ? "focus-in=" + (observation.FocusEnteredDialog ? "yes" : "NO")
                        + " escape-closed=" + (observation.ClosedOnEscape ? "yes" : "NO")
                        + " a11y=" + observation.Accessibility.Count
                            .ToString(CultureInfo.InvariantCulture)
                    : observation.Detail));
        }

        Write(Path.Combine(runDirectory, "dialog-runtime.json"), new
        {
            RunId = Option(options, "run", "AUDIT-002"),
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Executable = executable,
            Environment = environment,
            Observations = observations,
            RefusedKeystrokes = app.Keys.RefusedSends,
            ApplicationClosedAt = closures,
        });

        app.Dispose();

        Console.WriteLine();

        foreach (IGrouping<string, DialogObservation> group in observations
            .GroupBy(x => x.Outcome, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count()))
        {
            Console.WriteLine("  " + group.Key.PadRight(24)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        return 0;
    }

    // --------------------------------------------------------------- dialogs

    /// <summary>
    /// Reads every dialog the client declares, and what opens it.
    /// </summary>
    /// <remarks>
    /// Static only. It says what a person could reach if the code does what it
    /// reads like; whether they actually can is a runtime question, and Audit 002
    /// answers that separately by opening them.
    /// </remarks>
    private static int Dialogs(IReadOnlyDictionary<string, string> options)
    {
        string repository = Option(options, "repo", Directory.GetCurrentDirectory());
        string output = Option(options, "out", Path.Combine(
            repository, "artifacts", "reviewer", "run-002-audit", "dialog-inventory.json"));

        SourceIndex source = SourceIndex.Load(repository);
        IReadOnlyList<DialogRecord> dialogs = DialogScanner.Scan(source);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Write(output, new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RepositoryCommit = source.Commit,
            Dialogs = dialogs,
        });

        int reachable = dialogs.Count(x => x.Openings.Any(o => o.HasControl));
        int constructed = dialogs.Count(x => x.Openings.Count > 0);

        Console.WriteLine("dialogs declared          "
            + dialogs.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  constructed somewhere   "
            + constructed.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  with an opening control "
            + reachable.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  enabled at rest         "
            + dialogs.Count(x => x.Openings.Any(o => o.EnabledAtRest))
                .ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  opened from >1 page     "
            + dialogs.Count(x => x.Openings.Select(o => o.Page).Distinct(StringComparer.Ordinal).Count() > 1)
                .ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  with a raw-identifier field "
            + dialogs.Count(x => x.Fields.Any(f => f.TakesRawIdentifier))
                .ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  named by a test         "
            + dialogs.Count(x => x.TestReferences.Count > 0).ToString(CultureInfo.InvariantCulture));

        foreach (DialogRecord dialog in dialogs.Where(x => x.Openings.Count == 0))
        {
            Console.WriteLine("  NOTHING CONSTRUCTS      " + dialog.DialogId);
        }

        Console.WriteLine("  reachable by a command  "
            + dialogs.Count(x => x.Openings.Any(o => o.CommandId is not null))
                .ToString(CultureInfo.InvariantCulture));

        foreach (DialogRecord dialog in dialogs.Where(x =>
            x.Openings.Count > 0 && !x.Openings.Any(o => o.HasControl)))
        {
            DialogOpening first = dialog.Openings[0];

            Console.WriteLine("  NO OPENING CONTROL      " + dialog.DialogId
                + " (constructed in " + (first.Method ?? "?")
                + ", command " + (first.CommandId ?? "none") + ")");
        }

        Console.WriteLine("Written to " + output);

        return 0;
    }

    // ------------------------------------------------------------ rebaseline

    /// <summary>
    /// Re-runs only the detectors whose negative evidence was invalidated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Audit 001 reported five accessibility checks and one clipping check. Three
    /// of the accessibility checks and the clipping check depended on a UI
    /// Automation pattern name that the harness trimmed with the wrong suffix, so
    /// none of them could fire and their zeroes were not evidence. Repair Wave 002
    /// found the defect; this pass re-establishes what those detectors actually
    /// say.
    /// </para>
    /// <para>
    /// Not a second audit. It walks the seventeen workspaces because the original
    /// negative coverage across all of them is suspect, and it does not open the
    /// sixty-one dialogs, test other roles, or run anything that mutates.
    /// </para>
    /// </remarks>
    private static int Rebaseline(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string runId = Option(options, "run", "AUDIT-001R");
        string commit = Option(options, "commit", "unknown");

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: rebaseline needs --exe and --out.");

            return 2;
        }

        (int Width, int Height)[] sizes = ParseSizes(
            Option(options, "sizes", "900x700,1024x768,1280x720,1600x1000"));

        if (sizes.Length == 0)
        {
            Console.Error.WriteLine("reviewer: rebaseline needs at least one size.");

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

        AuditPass pass = new(app, runDirectory);

        (int desktopWidth, int desktopHeight) = sizes[^1];

        Console.WriteLine("  accessibility across "
            + AgencyOsWorkspaces.All.Count.ToString(CultureInfo.InvariantCulture)
            + " workspaces at " + desktopWidth.ToString(CultureInfo.InvariantCulture)
            + "x" + desktopHeight.ToString(CultureInfo.InvariantCulture));

        IReadOnlyList<AccessibilitySurvey> surveys =
            pass.SurveyWorkspaces(desktopWidth, desktopHeight);

        Console.WriteLine("  layout across every workspace at "
            + sizes.Length.ToString(CultureInfo.InvariantCulture) + " sizes");

        List<LayoutProbe> layout = [];

        foreach ((int width, int height) in sizes)
        {
            foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
            {
                layout.Add(pass.ProbeLayout(workspace.Label, width, height));
            }

            Console.WriteLine("    " + width.ToString(CultureInfo.InvariantCulture) + "x"
                + height.ToString(CultureInfo.InvariantCulture) + " done");
        }

        Console.WriteLine("  destination reachability");
        IReadOnlyList<DestinationReachability> destinations =
            pass.ProbeDestinations(desktopWidth, desktopHeight);

        Console.WriteLine("  declared gestures");
        IReadOnlyList<GestureProbe> gestures = pass.ProbeGestures();

        Console.WriteLine("  overlays");
        IReadOnlyList<SurfaceEvidence> overlays = pass.ProbeOverlays();

        RebaselineReport report = new(
            runId,
            started,
            DateTimeOffset.UtcNow,
            executable,
            commit,
            environment,
            [.. sizes.Select(x => x.Width.ToString(CultureInfo.InvariantCulture) + "x"
                + x.Height.ToString(CultureInfo.InvariantCulture))],
            Native.DisplayScale(),
            surveys,
            layout,
            destinations,
            gestures,
            overlays,
            app.Keys.RefusedSends);

        Write(Path.Combine(runDirectory, "runtime.json"), report);

        Summarise(report);

        return 0;
    }

    private static void Summarise(RebaselineReport report)
    {
        int scanned = report.Surfaces.Sum(x => x.ControlsScanned);
        int accessibility = report.Surfaces.Sum(x => x.Accessibility.Count);
        int speech = report.Surfaces.Sum(x => x.RowSpeech.Count);

        Console.WriteLine();
        Console.WriteLine("  display scale        "
            + report.DisplayScale.ToString("0.00", CultureInfo.InvariantCulture));
        Console.WriteLine("  workspaces reached   "
            + report.Surfaces.Count(x => x.Visited).ToString(CultureInfo.InvariantCulture)
            + "/" + report.Surfaces.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  controls scanned     " + scanned.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  accessibility hits   " + accessibility.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  row-speech hits      " + speech.ToString(CultureInfo.InvariantCulture));

        foreach (IGrouping<string, ControlReachability> group in report.Layout
            .SelectMany(x => x.Controls)
            .GroupBy(x => x.Verdict, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("  layout " + group.Key.PadRight(20)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        foreach (IGrouping<string, DestinationReachability> group in report.Destinations
            .GroupBy(x => x.Verdict, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("  destination " + group.Key.PadRight(15)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        foreach (IGrouping<string, GestureProbe> group in report.Gestures
            .GroupBy(x => x.Verdict, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("  gesture " + group.Key.PadRight(19)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        Console.WriteLine("  keystrokes refused   "
            + report.RefusedKeystrokes.ToString(CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- layout

    /// <summary>
    /// Measures the shell at several window sizes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <c>observe</c> on purpose. <c>observe</c> produces the audit's
    /// runtime.json, and its shape is what Run 001 and Run 002 are compared on;
    /// folding a size sweep into it would change that record to answer a question
    /// only two findings ask. This writes its own file and leaves that one alone.
    /// </para>
    /// <para>
    /// Sizes are physical pixels, as passed to the window manager. The display
    /// scale decides what they mean to a layout, so the report records the scale
    /// alongside them rather than implying every machine sees the same thing.
    /// </para>
    /// </remarks>
    private static int Layout(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string runId = Option(options, "run", "REVIEW-002-LAYOUT");

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: layout needs --exe and --out.");

            return 2;
        }

        (int Width, int Height)[] sizes = ParseSizes(
            Option(options, "sizes", "900x700,1024x768,1280x720,1600x1000"));

        string[] pages = Option(options, "pages", "Deals,Command Center,Intelligence,Sync and Offline")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (sizes.Length == 0 || pages.Length == 0)
        {
            Console.Error.WriteLine("reviewer: layout needs at least one size and one page.");

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

        AuditPass pass = new(app, runDirectory);
        List<LayoutProbe> probes = [];

        foreach ((int width, int height) in sizes)
        {
            foreach (string page in pages)
            {
                Console.WriteLine("  " + page + " at "
                    + width.ToString(CultureInfo.InvariantCulture) + "x"
                    + height.ToString(CultureInfo.InvariantCulture));

                probes.Add(pass.ProbeLayout(page, width, height));
            }
        }

        Write(
            Path.Combine(runDirectory, "layout.json"),
            new LayoutReport(runId, started, DateTimeOffset.UtcNow, executable, environment, probes));

        foreach (LayoutProbe probe in probes)
        {
            int visible = probe.Destinations.Count(x => x.FullyVisible);
            int covered = probe.Destinations.Count(x => x.OverlapsFooter > 0);

            Console.WriteLine(
                "  " + probe.SurfaceId
                + ": pane " + (probe.PaneExpanded ? "expanded" : "compact")
                + ", destinations fully visible " + visible.ToString(CultureInfo.InvariantCulture)
                + "/" + probe.Destinations.Count.ToString(CultureInfo.InvariantCulture)
                + ", covered by footer " + covered.ToString(CultureInfo.InvariantCulture)
                + ", footer " + probe.FooterHeight.ToString(CultureInfo.InvariantCulture) + "px"
                + ", content scrolls " + (probe.ContentScrolls ? "yes" : "no")
                + ", unreachable actions "
                + probe.ActionsOutsideWindow.Count.ToString(CultureInfo.InvariantCulture));
        }

        return 0;
    }

    private static (int Width, int Height)[] ParseSizes(string value)
    {
        List<(int, int)> sizes = [];

        foreach (string part in value.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = part.Split('x', 'X');

            if (pair.Length == 2
                && int.TryParse(pair[0], CultureInfo.InvariantCulture, out int width)
                && int.TryParse(pair[1], CultureInfo.InvariantCulture, out int height))
            {
                sizes.Add((width, height));
            }
            else
            {
                Console.Error.WriteLine("reviewer: ignoring unreadable size '" + part + "'.");
            }
        }

        return [.. sizes];
    }

    // -------------------------------------------------------------- personas

    /// <summary>
    /// Creates the audit's role profiles through the product's own commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Audit 002 could only ever review AgencyOS as its first owner. Membership
    /// was write-once and nothing could register a second user, so Member,
    /// Observer and Administrator were unreachable — not hard to arrange, but
    /// impossible (<c>AOS-R002-002</c>, <c>AOS-R002-006</c>).
    /// </para>
    /// <para>
    /// These are made the way an operator makes them: one authorized call each,
    /// against the running server. Nothing is written into PostgreSQL. If the
    /// product ever loses the capability again, this mode fails rather than
    /// quietly falling back to a hand-built identity.
    /// </para>
    /// </remarks>
    private static async Task<int> Personas(IReadOnlyDictionary<string, string> options)
    {
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string owner = Option(options, "subject", "review-owner");
        string output = Option(options, "out", string.Empty);

        if (organization.Length == 0)
        {
            Console.Error.WriteLine("reviewer: personas needs --org.");

            return 2;
        }

        using HttpClient http = new() { BaseAddress = new Uri(apiBase) };

        http.DefaultRequestHeaders.Add("X-AgencyOS-Dev-Subject", owner);
        http.DefaultRequestHeaders.Add("X-AgencyOS-Platform", "windows-x64");
        http.DefaultRequestHeaders.Add("X-AgencyOS-Channel", "forge");
        http.DefaultRequestHeaders.Add("X-AgencyOS-Client-Version", "0.1.0");
        http.DefaultRequestHeaders.Add(
            "X-AgencyOS-Api-Contract",
            AgencyOS.Contracts.ApiContract.Current.ToString(CultureInfo.InvariantCulture));

        string root = "/api/v1/organizations/" + organization;

        // Administrator is the RESTRICTED profile as well: broad read including
        // sensitive material, almost no operational write. AgencyOS has no way to
        // express an arbitrary custom permission set, and inventing one for an
        // audit would describe a product that does not exist.
        (string Persona, string Role)[] wanted =
        [
            ("member", "Member"),
            ("observer", "Observer"),
            ("restricted", "Administrator"),
        ];

        Dictionary<string, object> made = new(StringComparer.Ordinal)
        {
            ["owner"] = new { Subject = owner, Role = "Owner", Note = "the bootstrap owner" },
            ["non-member"] = new
            {
                Subject = "review-non-member",
                Role = "(none)",
                Note = "never registered; every route answers 401",
            },
        };

        foreach ((string persona, string role) in wanted)
        {
            string subject = "review-" + persona;

            using HttpResponseMessage response = await http
                .PostAsJsonAsync(
                    root + "/members",
                    new
                    {
                        externalSubject = subject,
                        displayName = "Review " + persona,
                        email = subject + "@review.invalid",
                        role,
                    })
                .ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Console.WriteLine("  " + persona.PadRight(12) + role.PadRight(16)
                + (int)response.StatusCode + "  " + Summary(body));

            made[persona] = new { Subject = subject, Role = role, Status = (int)response.StatusCode };
        }

        using HttpResponseMessage members = await http.GetAsync(root + "/members")
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("  members now: " + await members.Content.ReadAsStringAsync()
            .ConfigureAwait(false));

        if (output.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Write(output, made);
        }

        return members.IsSuccessStatusCode ? 0 : 1;
    }

    private static string Summary(string body) =>
        body.Length <= 120 ? body : body[..120];

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

    // --------------------------------------------------------- opener-probe

    /// <summary>
    /// Drives named openers and says what an operator would have seen.
    /// </summary>
    /// <remarks>
    /// Reproduction support for <c>AOS-R002-019</c>, added by Repair Wave 003A and
    /// scoped to it. It discovers nothing and traverses nothing: it takes the
    /// surfaces it is given, invokes each the way the product intends, and writes
    /// before/after evidence with a verdict that distinguishes a dialog opening,
    /// a refusal the operator can read, and the case this wave exists for — the
    /// opener that ran and said nothing at all.
    /// </remarks>
    private static int OpenerProbeMode(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string requested = Option(options, "cases", string.Empty);

        if (executable.Length == 0 || runDirectory.Length == 0)
        {
            Console.Error.WriteLine("reviewer: opener-probe needs --exe and --out.");

            return 2;
        }

        // The four surfaces AOS-R002-019 names, as the product reaches them. A
        // default rather than a discovery pass, because the finding names them.
        (string Dialog, string Workspace, string Tab, string Command, string? Control, bool Row)[] cases =
        [
            ("ConnectMailboxDialog", "Communications", "Mailboxes", "mailbox.connect",
                "Connect a mailbox", true),
            ("CreatePredictionDialog", "Intelligence", "Predictions",
                "intelligence.prediction.create", null, false),
            ("RecordSourceDialog", "Intelligence", "Sources",
                "intelligence.source.record", null, false),
            ("ResolvePredictionDialog", "Intelligence", "Predictions",
                "intelligence.prediction.resolve", null, true),
        ];

        if (requested.Length > 0)
        {
            cases =
            [
                .. requested
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.Split('/', StringSplitOptions.TrimEntries))
                    .Where(x => x.Length >= 4)
                    .Select(x => (
                        x[0],
                        x[1],
                        x[2],
                        x[3],
                        x.Length > 4 && x[4].Length > 0 ? x[4] : null,
                        x.Length > 5 && string.Equals(x[5], "row", StringComparison.OrdinalIgnoreCase))),
            ];
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

        OpenerProbe probe = new(app, runDirectory);
        List<OpenerProbeResult> results = [];

        foreach ((string dialog, string workspace, string tab, string command, string? control, bool row)
            in cases)
        {
            OpenerProbeResult result = probe.Probe(dialog, workspace, tab, command, control, row);

            results.Add(result);

            Console.WriteLine();
            Console.WriteLine("== " + dialog + "  ->  " + result.Outcome);

            foreach (string step in result.Steps)
            {
                Console.WriteLine("   " + step);
            }
        }

        Write(Path.Combine(runDirectory, "opener-probe.json"), new
        {
            RunId = Option(options, "run", "REPAIR-003A"),
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Executable = executable,
            Environment = environment,
            Results = results,
        });

        Console.WriteLine();

        foreach (IGrouping<string, OpenerProbeResult> group in results
            .GroupBy(x => x.Outcome, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count()))
        {
            Console.WriteLine("  " + group.Key.PadRight(32)
                + group.Count().ToString(CultureInfo.InvariantCulture));
        }

        return 0;
    }

    // ---------------------------------------------------------- reach-probe

    /// <summary>
    /// Says whether a named control can be operated, and reveals it when something
    /// can.
    /// </summary>
    /// <remarks>
    /// Added by the Audit 002 final closure slice for the two dialogs that were
    /// left unopened by the harness rather than by the product. It navigates to a
    /// workspace, selects a path of tabs, optionally selects a row at each stop,
    /// and then classifies one named control. It discovers nothing and opens
    /// nothing; what it produces is the answer to "could an operator click this".
    /// </remarks>
    private static int ReachProbeMode(IReadOnlyDictionary<string, string> options)
    {
        string executable = Option(options, "exe", string.Empty);
        string runDirectory = Option(options, "out", string.Empty);
        string apiBase = Option(options, "api", "http://127.0.0.1:5199");
        string organization = Option(options, "org", string.Empty);
        string subject = Option(options, "subject", "review-owner");
        string requested = Option(options, "cases", string.Empty);
        string size = Option(options, "size", "1600x1000");

        if (executable.Length == 0 || runDirectory.Length == 0 || requested.Length == 0)
        {
            Console.Error.WriteLine("reviewer: reach-probe needs --exe, --out and --cases.");

            return 2;
        }

        int width = 1600;
        int height = 1000;

        if (size.Split('x') is [string w, string h]
            && int.TryParse(w, CultureInfo.InvariantCulture, out int parsedWidth)
            && int.TryParse(h, CultureInfo.InvariantCulture, out int parsedHeight))
        {
            width = parsedWidth;
            height = parsedHeight;
        }

        Directory.CreateDirectory(runDirectory);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["AGENCYOS_API_BASE"] = apiBase,
            ["AGENCYOS_ORGANIZATION_ID"] = organization,
            ["AGENCYOS_DEV_SUBJECT"] = subject,
        };

        Console.WriteLine("Launching " + executable);

        using ReviewApp app = ReviewApp.Launch(executable, environment, TimeSpan.FromSeconds(60));

        OpenerProbe probe = new(app, runDirectory);
        List<OpenerProbeResult> results = [];

        // Workspace/TabPath/Target[/rows]. TabPath is separated by '>' so a nested
        // tab is expressible, which is what ParticipantList sits behind.
        foreach (string entry in requested.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split('/', StringSplitOptions.TrimEntries);

            if (parts.Length < 3)
            {
                Console.Error.WriteLine("reviewer: '" + entry + "' is not workspace/tabs/target.");

                continue;
            }

            string[] path =
            [
                .. parts[1].Split(
                    '>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ];

            OpenerProbeResult result = probe.Reach(parts[0], path, parts[2], width, height);

            results.Add(result);

            Console.WriteLine();
            Console.WriteLine("== " + parts[2] + "  ->  " + result.Outcome);

            foreach (string step in result.Steps)
            {
                Console.WriteLine("   " + step);
            }
        }

        Write(Path.Combine(runDirectory, "reach-probe.json"), new
        {
            RunId = Option(options, "run", "AUDIT-002-FINAL"),
            Executable = executable,
            Environment = environment,
            WindowSize = width + "x" + height,
            Results = results,
        });

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
        Console.WriteLine("  personas   --api <url> --org <guid> --subject <owner> [--out <file>]  audit role profiles");
        Console.WriteLine("  observe    --exe <path> --out <run-dir> --org <guid> [--api <url>] [--subject <s>]");
        Console.WriteLine("  layout     --exe <path> --out <run-dir> --org <guid> [--sizes 900x700,...] [--pages Deals,...]");
        Console.WriteLine("  rebaseline --exe <path> --out <run-dir> --org <guid> [--sizes 900x700,...]  re-runs corrected detectors only");
        Console.WriteLine("  dialogs    --repo <path> [--out <file>]                      the static dialog inventory");
        Console.WriteLine("  dialog-runtime --exe <path> --out <run-dir> --org <guid> [--only <DialogId,...>]  opens and operates them");
        Console.WriteLine("  validation --exe <path> --out <run-dir> --org <guid> [--only <DialogId,...>]      submits them unready");
        Console.WriteLine("  opener-probe --exe <path> --out <run-dir> --org <guid> [--cases <Dialog/Workspace/Tab/command[/control][/row],...>]  one opener at a time, with a verdict");
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
