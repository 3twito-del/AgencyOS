using AgencyOS.Client.Commands;
using AgencyOS.Windows.Platform.Activation;
using System.IO;

namespace AgencyOS.Reviewer.Surface;

/// <summary>
/// Composes the static surface map from every scanner.
/// </summary>
/// <remarks>
/// The map answers one question: what is there to review. It draws no conclusions
/// — a surface being in the map says nothing about whether it can be reached,
/// which is the reachability analyzer's job, or whether it is any good, which is
/// the reviewer's.
/// </remarks>
public static class InventoryBuilder
{
    /// <summary>Builds the whole inventory.</summary>
    public static SurfaceMap Build(
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

        List<SurfaceRecord> surfaces =
        [
            .. Workspaces(source, codeBehind, markup, clientRoutes),
            .. Dialogs(source, codeBehind, markup),
            .. Commands(),
            .. DeepLinks(),
            .. Overlays(markup),
            .. ApiPaths(contract, clientRoutes),
            .. PlatformComponents(source),
        ];

        return new SurfaceMap(
            DateTimeOffset.UtcNow,
            source.Commit,
            [.. surfaces.OrderBy(x => x.SurfaceId, StringComparer.Ordinal)]);
    }

    private static IEnumerable<SurfaceRecord> Workspaces(
        SourceIndex source,
        IReadOnlyList<CodeBehindFacts> codeBehind,
        IReadOnlyList<XamlFile> markup,
        IReadOnlyList<ClientRoute> clientRoutes)
    {
        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            string pageType = PageTypeFor(workspace.Tag);
            string pagePath = "src/AgencyOS.Windows/Pages/" + pageType + ".xaml";

            XamlFile? page = markup.FirstOrDefault(x =>
                string.Equals(x.RelativePath, pagePath, StringComparison.OrdinalIgnoreCase));

            CodeBehindFacts? facts = codeBehind.FirstOrDefault(x =>
                string.Equals(x.TypeName, pageType, StringComparison.Ordinal));

            IReadOnlyList<string> commands =
            [
                .. CommandRegistry.Default.Commands
                    .Where(x => string.Equals(x.Workspace, workspace.Tag, StringComparison.Ordinal))
                    .Select(x => x.Id),
            ];

            Dictionary<string, string> notes = new(StringComparer.Ordinal)
            {
                ["accessKey"] = "Alt+" + workspace.AccessKey,
                ["dispatchedCommands"] = string.Join(
                    ", ",
                    facts?.DispatchedCommands ?? []),
                ["eventHandlers"] = (facts?.EventHandlers.Count ?? 0)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["tabs"] = string.Join(
                    ", ",
                    page?.Elements
                        .Where(x => x.Element is "PivotItem" or "TabViewItem")
                        .Select(x => x.Attribute("Header") ?? x.Name ?? "?")
                        ?? []),
            };

            yield return new SurfaceRecord
            {
                SurfaceId = "workspace." + workspace.Tag,
                Type = SurfaceKind.Workspace,
                Label = workspace.Label,
                SourceFile = page?.RelativePath ?? pagePath,
                NavigationPath = workspace.Tag,
                CommandIds = commands,
                Gesture = CommandRegistry.Default.Commands
                    .FirstOrDefault(x => x.Action == CommandActionKind.Navigate
                        && string.Equals(x.Workspace, workspace.Tag, StringComparison.Ordinal))
                    ?.Gesture.Display,
                RequiredPermission = null,
                ApiDependencies = ApiDependenciesFor(facts, clientRoutes),
                DeepLink = DeepLinkFor(workspace.Tag),
                Dialogs = facts?.OpenedDialogs ?? [],
                ExpectedStates = page is null ? [] : XamlScanner.States(page),
                TestReferences = source.Mentioning(pageType, "tests/"),
                Notes = notes,
            };
        }
    }

    private static IEnumerable<SurfaceRecord> Dialogs(
        SourceIndex source,
        IReadOnlyList<CodeBehindFacts> codeBehind,
        IReadOnlyList<XamlFile> markup)
    {
        foreach (XamlFile file in markup)
        {
            if (!file.RelativePath.Contains("/Dialogs/", StringComparison.Ordinal))
            {
                continue;
            }

            string type = Path.GetFileNameWithoutExtension(file.RelativePath);

            IReadOnlyList<string> openers =
            [
                .. codeBehind
                    .Where(x => x.OpenedDialogs.Contains(type, StringComparer.Ordinal))
                    .Select(x => x.TypeName),
            ];

            XamlElement? dialog = file.Elements.FirstOrDefault(x => x.Element == "ContentDialog");

            Dictionary<string, string> notes = new(StringComparer.Ordinal)
            {
                ["title"] = dialog?.Attribute("Title") ?? string.Empty,
                ["primaryButton"] = dialog?.Attribute("PrimaryButtonText") ?? string.Empty,
                ["secondaryButton"] = dialog?.Attribute("SecondaryButtonText") ?? string.Empty,
                ["closeButton"] = dialog?.Attribute("CloseButtonText") ?? string.Empty,
                ["defaultButton"] = dialog?.Attribute("DefaultButton") ?? string.Empty,
                ["openedBy"] = string.Join(", ", openers),
                ["inputs"] = file.Elements
                    .Count(x => XamlScanner.Interactive.Contains(x.Element))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            yield return new SurfaceRecord
            {
                SurfaceId = "dialog." + type,
                Type = SurfaceKind.Dialog,
                Label = dialog?.Attribute("Title") ?? type,
                SourceFile = file.RelativePath,
                NavigationPath = openers.Count > 0 ? WorkspaceOf(openers[0]) : null,
                CommandIds = [],
                ExpectedStates = XamlScanner.States(file),
                TestReferences = source.Mentioning(type, "tests/"),
                Notes = notes,
            };
        }
    }

    private static IEnumerable<SurfaceRecord> Commands()
    {
        foreach (CommandDefinition command in CommandRegistry.Default.Commands)
        {
            yield return new SurfaceRecord
            {
                SurfaceId = "command." + command.Id,
                Type = SurfaceKind.Command,
                Label = command.Label,
                SourceFile = "src/AgencyOS.Client/Commands/AgencyOsCommands.cs",
                NavigationPath = command.Workspace,
                CommandIds = [command.Id],
                Gesture = command.HasGesture ? command.Gesture.Display : null,
                RequiredPermission = command.RequiredPermission,
                Notes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = command.Category,
                    ["action"] = command.Action.ToString(),
                    ["scope"] = command.Scope.ToString(),
                    ["requiresContext"] = command.RequiresContext.ToString(),
                },
            };
        }
    }

    private static IEnumerable<SurfaceRecord> DeepLinks()
    {
        foreach (string route in ActivationRouter.KnownRoutes.OrderBy(x => x, StringComparer.Ordinal))
        {
            ActivationRoute parsed = ActivationRouter.Parse(
                ActivationRouter.Scheme + "://" + route + "/" + Guid.Empty.ToString("D"));

            yield return new SurfaceRecord
            {
                SurfaceId = "deeplink." + route.Replace('/', '.'),
                Type = SurfaceKind.DeepLink,
                Label = ActivationRouter.Scheme + "://" + route + "/{id}",
                SourceFile = "src/AgencyOS.Windows.Platform/Activation/ActivationRouter.cs",
                NavigationPath = parsed.IsResolved ? parsed.Workspace : null,
                DeepLink = ActivationRouter.Scheme + "://" + route + "/{id}",
                Notes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kind"] = parsed.IsResolved ? parsed.Kind.ToString() : parsed.Failure.ToString(),
                },
            };
        }
    }

    private static IEnumerable<SurfaceRecord> Overlays(IReadOnlyList<XamlFile> markup)
    {
        XamlFile? window = markup.FirstOrDefault(x =>
            x.RelativePath.EndsWith("MainWindow.xaml", StringComparison.Ordinal));

        if (window is null)
        {
            yield break;
        }

        (string Id, string Label, string Root, string Gesture)[] overlays =
        [
            ("overlay.command-palette", "Command palette", "PaletteLayer", "Ctrl+P"),
            ("overlay.global-search", "Global search", "SearchLayer", "Ctrl+K"),
        ];

        foreach ((string id, string label, string root, string gesture) in overlays)
        {
            IReadOnlyList<XamlElement> inside =
            [
                .. window.Elements.Where(x => x.Name is not null && x.Name.StartsWith(
                    root.Replace("Layer", string.Empty, StringComparison.Ordinal),
                    StringComparison.Ordinal)),
            ];

            yield return new SurfaceRecord
            {
                SurfaceId = id,
                Type = SurfaceKind.Overlay,
                Label = label,
                SourceFile = "src/AgencyOS.Windows/MainWindow.xaml",
                Gesture = gesture,
                ExpectedStates = [.. inside.Select(x => x.Name!)],
                Notes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rootElement"] = root,
                    ["declaredInMarkup"] = window.Elements
                        .Any(x => string.Equals(x.Name, root, StringComparison.Ordinal))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            };
        }
    }

    private static IEnumerable<SurfaceRecord> ApiPaths(
        IReadOnlyList<ApiPath> contract,
        IReadOnlyList<ClientRoute> clientRoutes)
    {
        ILookup<string, ClientRoute> byShape = clientRoutes.ToLookup(x => x.Shape, StringComparer.Ordinal);

        foreach (ApiPath path in contract)
        {
            IReadOnlyList<ClientRoute> callers = [.. byShape[path.Shape]];

            yield return new SurfaceRecord
            {
                SurfaceId = "api." + path.Path,
                Type = SurfaceKind.ApiPath,
                Label = string.Join('/', path.Methods) + " " + path.Path,
                SourceFile = "artifacts/openapi/AgencyOS.Api.json",
                ApiDependencies = [path.Path],
                Notes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["methods"] = string.Join(", ", path.Methods),
                    ["windowsClientCallers"] = string.Join(
                        ", ",
                        callers.Select(x => x.SourceFile + ":" + x.Line.ToString(
                            System.Globalization.CultureInfo.InvariantCulture))),
                },
            };
        }
    }

    private static IEnumerable<SurfaceRecord> PlatformComponents(SourceIndex source)
    {
        foreach ((string path, _) in source.Under("src/AgencyOS.Windows.Platform/", ".cs"))
        {
            string type = Path.GetFileNameWithoutExtension(path);

            yield return new SurfaceRecord
            {
                SurfaceId = "platform." + type,
                Type = SurfaceKind.PlatformComponent,
                Label = type,
                SourceFile = path,
                TestReferences = source.Mentioning(type, "tests/"),
                Notes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["productReferences"] = string.Join(
                        ", ",
                        source.Mentioning(type, "src/").Where(x => !string.Equals(x, path, StringComparison.Ordinal))),
                },
            };
        }
    }

    private static IReadOnlyList<string> ApiDependenciesFor(
        CodeBehindFacts? facts,
        IReadOnlyList<ClientRoute> clientRoutes)
    {
        if (facts is null || facts.ApiCalls.Count == 0)
        {
            return [];
        }

        // The page names client members, not routes. The mapping from member to
        // route lives inside the client, so the honest answer here is the member
        // names plus the count of routes the client knows - not a guessed path.
        _ = clientRoutes;

        return facts.ApiCalls;
    }

    private static string? DeepLinkFor(string workspace) => workspace switch
    {
        "talent" => "agencyos://person/{id}",
        "companies" => "agencyos://company/{id}",
        "deals" => "agencyos://deal/{id}",
        "contracts" => "agencyos://contract/{id}",
        "intelligence" => "agencyos://research/{id}",
        "ai" => "agencyos://ai/run/{id}",
        _ => null,
    };

    private static string? WorkspaceOf(string pageType) => pageType switch
    {
        "CommandCenterPage" => "command-center",
        "PeoplePage" => "people",
        "CompaniesPage" => "companies",
        "TalentPage" => "talent",
        "ProspectsPage" => "prospects",
        "ProjectsPage" => "projects",
        "PackagesPage" => "packages",
        "PipelinePage" => "pipeline",
        "DealsPage" => "deals",
        "ContractsPage" => "contracts",
        "FinancePage" => "finance",
        "DocumentsPage" => "documents",
        "CommunicationsPage" => "communications",
        "IntelligencePage" => "intelligence",
        "AiPage" => "ai",
        "SavedViewsPage" => "saved-views",
        "SyncPage" => "sync",
        _ => null,
    };

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
}
