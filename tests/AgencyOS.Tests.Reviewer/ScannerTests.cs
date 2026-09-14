using System.IO;
using AgencyOS.Reviewer.Ledger;
using AgencyOS.Reviewer.Reachability;
using AgencyOS.Reviewer.Style;
using AgencyOS.Reviewer.Surface;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// The deterministic half of the harness, tested against the real repository.
/// </summary>
/// <remarks>
/// These run in CI. They need no desktop, no database and no network: every
/// assertion is over source the repository already contains. What they protect is
/// the harness's own soundness — a scanner that quietly stops matching would turn
/// an audit's silence into a clean bill of health.
/// </remarks>
public sealed class ScannerTests
{
    private static readonly Lazy<SourceIndex> Source = new(() => SourceIndex.Load(RepositoryRoot));

    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("The repository root could not be located.");
        }
    }

    [Fact]
    public void TheScannerSeesTheRealWindowsClient()
    {
        IReadOnlyList<XamlFile> markup = XamlScanner.Scan(Source.Value, "src/AgencyOS.Windows/");

        Assert.True(
            markup.Count > 60,
            "Only " + markup.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " markup files were parsed. The client has more than sixty, so the scan is not "
                + "reading the product.");

        Assert.Contains(markup, x => x.RelativePath.EndsWith("MainWindow.xaml", StringComparison.Ordinal));
        Assert.Contains(markup, x => x.RootElement == "ContentDialog");
        Assert.Contains(markup, x => x.RootElement == "Page");
    }

    /// <summary>
    /// The dispatch scan reads the target-typed form the client actually writes.
    /// </summary>
    /// <remarks>
    /// This is the check the harness got wrong first. Matching only
    /// <c>new XDialog(...)</c> reported all sixty-one dialogs as unreachable,
    /// because every page writes <c>XDialog dialog = new() { … }</c>. A dead-surface
    /// scanner that is wrong in that direction is worse than none.
    /// </remarks>
    [Fact]
    public void DialogConstructionIsDetectedInTheFormTheClientWrites()
    {
        IReadOnlyList<CodeBehindFacts> facts = CodeScanner.Scan(Source.Value);

        IReadOnlyList<string> constructed =
        [
            .. facts.SelectMany(x => x.OpenedDialogs).Distinct(StringComparer.Ordinal),
        ];

        Assert.True(
            constructed.Count > 40,
            "Only " + constructed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " dialogs were seen being constructed. The client opens more than forty, so the "
                + "construction pattern is not being matched.");

        Assert.Contains("CreateDealDialog", constructed);
        Assert.Contains("NewPersonDialog", constructed);
    }

    [Fact]
    public void CommandDispatchIsReadFromTheExecuteBodyOnly()
    {
        IReadOnlyList<CodeBehindFacts> facts = CodeScanner.Scan(Source.Value);

        CodeBehindFacts finance = facts.Single(x => x.TypeName == "FinancePage");

        Assert.Contains("payment.allocate", finance.DispatchedCommands);
        Assert.Contains("go.ledger", finance.DispatchedCommands);

        // Pages switch over other strings - tab names, statuses - outside Execute.
        // Counting those would hide the very defect the scan exists to find.
        Assert.DoesNotContain("Receivables", finance.DispatchedCommands);
    }

    [Fact]
    public void NoPageDispatchesOnANonLiteralIdentifier()
    {
        IReadOnlyList<string> suspects = CodeScanner.DynamicDispatchSuspects(Source.Value);

        Assert.True(
            suspects.Count == 0,
            "These files dispatch on something other than a string literal, which makes every "
                + "'command never dispatched' finding unsound: " + string.Join("; ", suspects));
    }

    [Fact]
    public void RouteShapesIgnoreParameterSpelling()
    {
        Assert.Equal(
            ApiScanner.Shape("/api/v1/organizations/{organizationId}/people/{personId}"),
            ApiScanner.Shape("/api/v1/organizations/{org}/people/{id}"));

        Assert.NotEqual(
            ApiScanner.Shape("/api/v1/organizations/{id}/people"),
            ApiScanner.Shape("/api/v1/organizations/{id}/companies"));
    }

    /// <summary>
    /// A query string appended to a segment is not a route parameter.
    /// </summary>
    /// <remarks>
    /// The client writes <c>$"{TenantRoot}/people{query}"</c>. Treating that hole as
    /// a parameter produced <c>/people{}</c>, which matches no published path, and
    /// the harness reported the product's most-used endpoint as one the contract
    /// does not describe.
    /// </remarks>
    [Fact]
    public void ClientRoutesDropAppendedQueryStrings()
    {
        IReadOnlyList<ClientRoute> routes = ApiScanner.ReadClientRoutes(Source.Value);

        Assert.Contains(routes, x => x.Shape == "/api/v1/organizations/{}/people");
        Assert.DoesNotContain(routes, x => x.Shape.Contains("people{}", StringComparison.Ordinal));

        Assert.True(
            routes.Count > 150,
            "Only " + routes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " client routes were found; the client calls far more.");
    }

    [Fact]
    public void EveryCommandInTheRegistryIsAnsweredBySomething()
    {
        IReadOnlyList<CodeBehindFacts> codeBehind = CodeScanner.Scan(Source.Value);
        IReadOnlyList<XamlFile> markup = XamlScanner.Scan(Source.Value, "src/AgencyOS.Windows/");

        IReadOnlyList<ReachabilityObservation> observations = ReachabilityAnalyzer.Analyze(
            Source.Value, codeBehind, markup, [], []);

        IReadOnlyList<ReachabilityObservation> dead =
        [
            .. observations.Where(x => x.Kind is "command-registered-never-dispatched"
                or "handler-with-no-command"
                or "command-answered-off-its-workspace"
                or "advertised-gesture-not-installed"),
        ];

        Assert.True(
            dead.Count == 0,
            "Commands that nothing answers, or handlers no command can reach: "
                + string.Join("; ", dead.Select(x => x.Id + " " + x.Subject)));
    }

    [Fact]
    public void TheStyleInventoryReadsTheProductsOwnPractice()
    {
        StyleReport report = XamlStyleInventory.Build(
            XamlScanner.Scan(Source.Value, "src/AgencyOS.Windows/"));

        Assert.Contains(report.Dimensions, x => x.Property == "Padding" && x.TotalOccurrences > 50);
        Assert.Contains(report.Dimensions, x => x.Property == "Style" && x.TotalOccurrences > 100);

        // An outlier is only reported where a dominant practice exists to be an
        // exception to; otherwise it is taste presented as evidence.
        Assert.All(report.Outliers, outlier => Assert.True(
            outlier.DominantCount >= 4 * outlier.Outliers.Count || outlier.Concept.EndsWith("used once", StringComparison.Ordinal),
            outlier.Concept + " was reported without a dominant practice behind it."));
    }

    [Fact]
    public void AFindingWithNoEvidenceIsNotWellFormed()
    {
        Finding bare = new()
        {
            Id = "TEST-001",
            Category = FindingCategory.Ux,
            Severity = Severity.S3,
            Confidence = Confidence.DesignRecommendation,
            Reproducibility = Reproducibility.Static,
            Title = "This looks bad",
            Surface = ["workspace.deals"],
            Expected = "Better",
            Actual = "Worse",
            Evidence = [],
            WhyItMatters = "It does",
            SuggestedCorrection = "Change it",
        };

        Assert.False(bare.IsWellFormed);

        Assert.True((bare with { Evidence = ["screenshot.png"] }).IsWellFormed);
    }
}
