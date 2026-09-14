using System.Globalization;
using AgencyOS.Client.Commands;
using AgencyOS.Reviewer.Surface;
using AgencyOS.Windows.Platform.Activation;
using System.IO;

namespace AgencyOS.Reviewer.Reachability;

/// <summary>
/// Cross-references the inventory to find what the product cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// The command registry already refuses duplicate identifiers and colliding
/// gestures at construction, and a unit test constructs the default set, so those
/// two classes of defect cannot survive a build. What no existing test asks is the
/// other direction: whether a command that is registered, labelled and advertised
/// in the palette is answered by anything at all.
/// </para>
/// <para>
/// That is the M13 defect class restated. M13 removed twenty-six palette entries
/// nothing dispatched. Nothing in the tree prevents the twenty-seventh.
/// </para>
/// </remarks>
public static class ReachabilityAnalyzer
{
    /// <summary>Commands the shell window answers itself rather than delegating.</summary>
    private static readonly IReadOnlySet<string> ShellOwned =
        new HashSet<string>(StringComparer.Ordinal) { "search.open", "sync.now" };

    /// <summary>Runs every cross-reference.</summary>
    public static IReadOnlyList<ReachabilityObservation> Analyze(
        SourceIndex source,
        IReadOnlyList<CodeBehindFacts> codeBehind,
        IReadOnlyList<XamlFile> markup,
        IReadOnlyList<ApiPath> contract,
        IReadOnlyList<ClientRoute> clientRoutes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(codeBehind);
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(clientRoutes);

        List<ReachabilityObservation> observations = [];

        observations.AddRange(UndispatchedCommands(codeBehind));
        observations.AddRange(StaleDispatches(codeBehind));
        observations.AddRange(MisplacedDispatches(codeBehind));
        observations.AddRange(UnmappableGestures());
        observations.AddRange(UnreachableDialogs(source, codeBehind, markup));
        observations.AddRange(DeepLinkReachability(source));
        observations.AddRange(WorkspaceCoverage(source));
        observations.AddRange(ApiCoverage(contract, clientRoutes));
        observations.AddRange(UnconsumedPlatformComponents(source));

        return [.. observations.OrderBy(x => x.Id, StringComparer.Ordinal)];
    }

    /// <summary>Commands nothing answers.</summary>
    private static IEnumerable<ReachabilityObservation> UndispatchedCommands(
        IReadOnlyList<CodeBehindFacts> codeBehind)
    {
        HashSet<string> dispatched = new(StringComparer.Ordinal);

        foreach (CodeBehindFacts facts in codeBehind)
        {
            foreach (string command in facts.DispatchedCommands)
            {
                dispatched.Add(command);
            }
        }

        int index = 0;

        foreach (CommandDefinition command in CommandRegistry.Default.Commands)
        {
            if (command.Action == CommandActionKind.Navigate)
            {
                // Navigation is answered by the shell from the workspace tag, so a
                // navigation command needs no page to answer it.
                continue;
            }

            if (ShellOwned.Contains(command.Id) || dispatched.Contains(command.Id))
            {
                continue;
            }

            index++;

            string gesture = command.HasGesture
                ? " and installs " + command.Gesture.Display
                : string.Empty;

            yield return new ReachabilityObservation(
                Identifier("DEAD-CMD", index),
                "command-registered-never-dispatched",
                command.Id,
                "'" + command.Label + "' is in the registry, appears in the palette" + gesture
                    + ", and no page's Execute answers it.",
                [
                    "src/AgencyOS.Client/Commands/AgencyOsCommands.cs",
                    command.Workspace is { } workspace
                        ? "expected answer on workspace '" + workspace + "'"
                        : "no workspace named",
                ],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>Pages answering an identifier the registry does not define.</summary>
    private static IEnumerable<ReachabilityObservation> StaleDispatches(
        IReadOnlyList<CodeBehindFacts> codeBehind)
    {
        int index = 0;

        foreach (CodeBehindFacts facts in codeBehind)
        {
            foreach (string command in facts.DispatchedCommands)
            {
                if (CommandRegistry.Default.Find(command) is not null)
                {
                    continue;
                }

                index++;

                yield return new ReachabilityObservation(
                    Identifier("STALE-CMD", index),
                    "handler-with-no-command",
                    command,
                    facts.TypeName + " answers '" + command + "', which no command in the registry "
                        + "names, so nothing can ever send it.",
                    [facts.RelativePath],
                    GapVerdict.UnreachableDefect);
            }
        }
    }

    /// <summary>
    /// Commands answered somewhere other than the workspace they name.
    /// </summary>
    /// <remarks>
    /// The shell navigates to <c>Workspace</c> before handing the command to
    /// whatever page is showing. A command answered only by a different page is
    /// therefore dispatched to a page that ignores it, which is the M13 "Open
    /// receivables does nothing unless Finance is open" defect with the navigation
    /// half fixed and the answering half still wrong.
    /// </remarks>
    private static IEnumerable<ReachabilityObservation> MisplacedDispatches(
        IReadOnlyList<CodeBehindFacts> codeBehind)
    {
        int index = 0;

        foreach (CommandDefinition command in CommandRegistry.Default.Commands)
        {
            if (command.Action != CommandActionKind.Invoke
                || command.Workspace is not { } workspace
                || ShellOwned.Contains(command.Id))
            {
                continue;
            }

            List<string> answeredBy =
            [
                .. codeBehind
                    .Where(x => x.DispatchedCommands.Contains(command.Id, StringComparer.Ordinal))
                    .Select(x => x.TypeName),
            ];

            if (answeredBy.Count == 0)
            {
                // Reported by UndispatchedCommands; not reported twice.
                continue;
            }

            string expected = PageTypeFor(workspace);

            if (answeredBy.Contains(expected, StringComparer.Ordinal))
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("MISPLACED-CMD", index),
                "command-answered-off-its-workspace",
                command.Id,
                "'" + command.Id + "' names workspace '" + workspace + "', so the shell navigates to "
                    + expected + " and hands the command there, but only "
                    + string.Join(", ", answeredBy) + " answers it.",
                ["src/AgencyOS.Windows/MainWindow.xaml.cs", .. answeredBy],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>
    /// Gestures the window's key mapper cannot express.
    /// </summary>
    /// <remarks>
    /// <c>TryMapGesture</c> installs nothing when it cannot map a key, which is
    /// the right failure but a silent one: the palette would still advertise the
    /// shortcut.
    /// </remarks>
    private static IEnumerable<ReachabilityObservation> UnmappableGestures()
    {
        int index = 0;

        foreach (CommandDefinition command in CommandRegistry.Default.GlobalGestures)
        {
            bool mappable = command.Gesture.Key
                is (>= CommandKey.D0 and <= CommandKey.D9)
                or (>= CommandKey.A and <= CommandKey.Z)
                or (>= CommandKey.F1 and <= CommandKey.F12)
                or CommandKey.Escape
                or CommandKey.Enter;

            if (mappable)
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("GESTURE-UNMAPPED", index),
                "advertised-gesture-not-installed",
                command.Id,
                "The palette shows " + command.Gesture.Display
                    + "; MainWindow.TryMapGesture cannot map that key, so no accelerator is installed.",
                ["src/AgencyOS.Windows/MainWindow.xaml.cs"],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>Dialogs nothing constructs.</summary>
    private static IEnumerable<ReachabilityObservation> UnreachableDialogs(
        SourceIndex source,
        IReadOnlyList<CodeBehindFacts> codeBehind,
        IReadOnlyList<XamlFile> markup)
    {
        HashSet<string> constructed = new(StringComparer.Ordinal);

        foreach (CodeBehindFacts facts in codeBehind)
        {
            foreach (string dialog in facts.OpenedDialogs)
            {
                constructed.Add(dialog);
            }
        }

        int index = 0;

        foreach (XamlFile file in markup)
        {
            if (!file.RelativePath.Contains("/Dialogs/", StringComparison.Ordinal))
            {
                continue;
            }

            string type = Path.GetFileNameWithoutExtension(file.RelativePath);

            if (constructed.Contains(type))
            {
                continue;
            }

            index++;

            IReadOnlyList<string> mentions = source.Mentioning(type, "src/");

            yield return new ReachabilityObservation(
                Identifier("DEAD-DIALOG", index),
                "dialog-never-opened",
                type,
                type + " is declared and nothing constructs it, so no interaction can open it.",
                [file.RelativePath, .. mentions],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>Whether anything turns a deep link into a destination.</summary>
    private static IEnumerable<ReachabilityObservation> DeepLinkReachability(SourceIndex source)
    {
        IReadOnlyList<string> parseCallers =
        [
            .. source
                .Mentioning("ActivationRouter.Parse", "src/")
                .Where(x => !x.StartsWith("src/AgencyOS.Windows.Platform/", StringComparison.Ordinal)),
        ];

        bool activationHandled = source.Files.TryGetValue("src/AgencyOS.Windows/App.xaml.cs", out string? app)
            && (app.Contains("AppInstance", StringComparison.Ordinal)
                || app.Contains("GetActivatedEventArgs", StringComparison.Ordinal)
                || app.Contains("ActivationRouter", StringComparison.Ordinal));

        if (parseCallers.Count > 0 && activationHandled)
        {
            yield break;
        }

        int index = 0;

        foreach (string route in ActivationRouter.KnownRoutes.OrderBy(x => x, StringComparer.Ordinal))
        {
            index++;

            yield return new ReachabilityObservation(
                Identifier("DEAD-LINK", index),
                "deep-link-with-no-reachable-target",
                ActivationRouter.Scheme + ":" + route,
                "The router understands this route and no running code calls ActivationRouter.Parse; "
                    + "App.OnLaunched inspects no activation arguments, so a link that reaches the "
                    + "process does nothing.",
                [
                    "src/AgencyOS.Windows.Platform/Activation/ActivationRouter.cs",
                    "src/AgencyOS.Windows/App.xaml.cs",
                ],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>Workspaces the shell cannot show, and pages nothing navigates to.</summary>
    private static IEnumerable<ReachabilityObservation> WorkspaceCoverage(SourceIndex source)
    {
        if (!source.Files.TryGetValue("src/AgencyOS.Windows/MainWindow.xaml.cs", out string? window))
        {
            yield break;
        }

        if (!source.Files.TryGetValue("src/AgencyOS.Windows/MainWindow.xaml", out string? markup))
        {
            yield break;
        }

        int index = 0;

        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            bool inNavigationPane = markup.Contains("Tag=\"" + workspace.Tag + "\"", StringComparison.Ordinal);
            bool inNavigateSwitch = window.Contains("\"" + workspace.Tag + "\" =>", StringComparison.Ordinal);

            if (inNavigationPane && inNavigateSwitch)
            {
                continue;
            }

            // command-center is the switch's default arm rather than a case, which
            // is correct and would otherwise be reported here.
            if (string.Equals(workspace.Tag, "command-center", StringComparison.Ordinal) && inNavigationPane)
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("WORKSPACE", index),
                "workspace-not-fully-wired",
                workspace.Tag,
                "'" + workspace.Tag + "' is declared in AgencyOsWorkspaces; navigation pane: "
                    + inNavigationPane.ToString(CultureInfo.InvariantCulture) + "; Navigate switch: "
                    + inNavigateSwitch.ToString(CultureInfo.InvariantCulture) + ".",
                ["src/AgencyOS.Windows/MainWindow.xaml", "src/AgencyOS.Windows/MainWindow.xaml.cs"],
                GapVerdict.Inconclusive);
        }
    }

    /// <summary>Contract paths with no Windows caller, and callers with no contract path.</summary>
    private static IEnumerable<ReachabilityObservation> ApiCoverage(
        IReadOnlyList<ApiPath> contract,
        IReadOnlyList<ClientRoute> clientRoutes)
    {
        HashSet<string> called = new(clientRoutes.Select(x => x.Shape), StringComparer.Ordinal);
        HashSet<string> published = new(contract.Select(x => x.Shape), StringComparer.Ordinal);

        int index = 0;

        foreach (ApiPath path in contract)
        {
            if (called.Contains(path.Shape))
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("API-UNCALLED", index),
                "contract-path-no-client-caller",
                path.Path,
                string.Join('/', path.Methods) + " " + path.Path
                    + " is published and the Windows client builds no request whose path matches it.",
                ["artifacts/openapi/AgencyOS.Api.json"],
                GapVerdict.Inconclusive);
        }

        index = 0;

        foreach (ClientRoute route in clientRoutes.DistinctBy(x => x.Shape, StringComparer.Ordinal))
        {
            if (published.Contains(route.Shape))
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("CLIENT-ORPHAN", index),
                "client-route-not-in-contract",
                route.Template,
                "The client builds " + route.Template + " and no published path has that shape, so the "
                    + "request cannot succeed against a server built from this contract.",
                [string.Create(CultureInfo.InvariantCulture, $"{route.SourceFile}:{route.Line}")],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>Platform policies with tests and no caller.</summary>
    private static IEnumerable<ReachabilityObservation> UnconsumedPlatformComponents(SourceIndex source)
    {
        (string Type, string File)[] components =
        [
            ("NotificationPolicy", "src/AgencyOS.Windows.Platform/Notifications/NotificationPolicy.cs"),
            ("DocumentHandoffPolicy", "src/AgencyOS.Windows.Platform/Documents/DocumentHandoffPolicy.cs"),
            ("DiagnosticSummary", "src/AgencyOS.Windows.Platform/Diagnostics/DiagnosticSummary.cs"),
            ("OperationTiming", "src/AgencyOS.Windows.Platform/Diagnostics/OperationTiming.cs"),
            ("ActivationRouter", "src/AgencyOS.Windows.Platform/Activation/ActivationRouter.cs"),
            ("LocalInferenceRunner", "src/AgencyOS.Windows.Platform/LocalInference/LocalInferenceRunner.cs"),
        ];

        int index = 0;

        foreach ((string type, string file) in components)
        {
            IReadOnlyList<string> callers =
            [
                .. source
                    .Mentioning(type, "src/")
                    .Where(x => !string.Equals(x, file, StringComparison.Ordinal)),
            ];

            IReadOnlyList<string> tests = source.Mentioning(type, "tests/");

            if (callers.Count > 0)
            {
                continue;
            }

            index++;

            yield return new ReachabilityObservation(
                Identifier("DEAD-PLATFORM", index),
                "platform-component-no-product-consumer",
                type,
                type + " is implemented and covered by "
                    + tests.Count.ToString(CultureInfo.InvariantCulture)
                    + " test file(s); no file outside its own declaration references it, so nothing "
                    + "in the running product uses it.",
                [file, .. tests],
                GapVerdict.UnreachableDefect);
        }
    }

    /// <summary>The page type the shell navigates to for a workspace tag.</summary>
    private static string PageTypeFor(string workspace) => workspace switch
    {
        "command-center" => "CommandCenterPage",
        "people" => "PeoplePage",
        "companies" => "CompaniesPage",
        "talent" => "TalentPage",
        "prospects" => "ProspectsPage",
        "projects" => "ProjectsPage",
        "packages" => "PackagesPage",
        "pipeline" => "PipelinePage",
        "deals" => "DealsPage",
        "contracts" => "ContractsPage",
        "finance" => "FinancePage",
        "documents" => "DocumentsPage",
        "communications" => "CommunicationsPage",
        "intelligence" => "IntelligencePage",
        "ai" => "AiPage",
        "saved-views" => "SavedViewsPage",
        "sync" => "SyncPage",
        _ => "CommandCenterPage",
    };

    private static string Identifier(string prefix, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{index:D3}");
}
