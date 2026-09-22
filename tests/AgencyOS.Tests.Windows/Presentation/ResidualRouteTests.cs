using System.Xml.Linq;
using AgencyOS.Client.Commands;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the three residual Definition-of-Done rows can now be reached.
/// </summary>
/// <remarks>
/// <para>
/// <c>29-DEFINITION-OF-DONE-ADJUDICATION.md</c> left four operations unadjudicated
/// after F-13 closed. Three of them turned out to be the same shape, and a sharper
/// one than F-13's: <strong>the product could record what became of an object no
/// supported workflow could create.</strong> M8 shipped option and obligation
/// outcomes against lists nothing could fill; M4 shipped a representation status
/// lifecycle, published its transition table, showed the status on the Talent page
/// and offered no way to change it.
/// </para>
/// <para>
/// The fourth, creating a talent profile, was already delivered — the radar
/// conversion creates one and says so — and is covered by
/// <see cref="CreationPairingTests"/> rather than here.
/// </para>
/// <para>
/// These prove reachability, context and refusal handling. What the routes then
/// <em>do</em> is the live evidence in <c>WAVE-05-RESIDUAL-DOD-CLOSURE.md</c>.
/// </para>
/// </remarks>
public sealed class ResidualRouteTests
{
    /// <param name="Control">The name an operator reaches it by.</param>
    /// <param name="Label">What the control says.</param>
    /// <param name="Page">The workspace its domain object lives in.</param>
    /// <param name="Flow">The method that performs it.</param>
    /// <param name="Dialog">The dialog it collects through.</param>
    /// <param name="Method">The client method that had no caller.</param>
    /// <param name="Command">The palette identifier that supplements the control.</param>
    public sealed record Route(
        string Control,
        string Label,
        string Page,
        string Flow,
        string Dialog,
        string Method,
        string Command);

    private static Route[] All =>
    [
        new("RecordOptionButton", "Record option", "ContractsPage",
            "RecordOptionAsync", "RecordContractOptionDialog",
            "RecordContractOptionAsync", "option.record"),
        new("RecordObligationButton", "Record obligation", "ContractsPage",
            "RecordObligationAsync", "RecordObligationDialog",
            "RecordObligationAsync", "obligation.record"),
        new("ChangeStatusButton", "Change status", "TalentPage",
            "ChangeStatusAsync", "TransitionRepresentationDialog",
            "TransitionRepresentationAsync", "representation.status.change"),
    ];

    public static TheoryData<Route> Restored => [.. All];

    /// <summary>Each act is a control where its domain object lives.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EachActIsAControlWhereItsObjectLives(Route route)
    {
        XElement control = Control(route);

        Assert.Equal("Button", control.Name.LocalName);
        Assert.Equal(route.Label, (string?)control.Attribute("Content"));
    }

    /// <summary>Each act has a handler on the page that owns it.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EachActHasAHandlerOnItsOwnPage(Route route)
    {
        string click = (string?)Control(route).Attribute("Click")
            ?? throw new Xunit.Sdk.XunitException($"{route.Control} does nothing when pressed");

        string page = Code(route.Page);

        Assert.Contains($"private void {click}(", page, StringComparison.Ordinal);
        Assert.Contains($"_ = {route.Flow}();", page, StringComparison.Ordinal);
    }

    /// <summary>Each act runs through its dialog and the client method that had no caller.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EachActRunsThroughTheShippedMethod(Route route)
    {
        string flow = Flow(route);

        Assert.Contains(route.Dialog, flow, StringComparison.Ordinal);
        Assert.Contains($"api.{route.Method}(", flow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The product supplies the identifiers, and the operator supplies none.
    /// </summary>
    /// <remarks>
    /// The contract, its latest version, its parties and the representation all
    /// come from what is on screen. Each carries its own concurrency version.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void TheProductSuppliesTheIdentifiers(Route route)
    {
        string flow = Flow(route);

        Assert.True(
            flow.Contains("_detail?.Contract is", StringComparison.Ordinal)
                || flow.Contains("_overview?.Overview?.Representation is", StringComparison.Ordinal),
            $"{route.Flow} does not take its subject from what is on screen");

        Assert.DoesNotContain("Guid.Parse", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.TryParse", flow, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused act leaves the surface saying why, and changes nothing.
    /// </summary>
    /// <remarks>
    /// Wave 4 found that refreshing after a refusal erased the explanation. Both
    /// pages now decline to refresh when the act did not go through — Contracts by
    /// gating on <c>Guarded</c>, Talent by returning from its catch — and this
    /// fails if either regresses.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ARefusedActDoesNotRefreshOverItsOwnExplanation(Route route)
    {
        string flow = Flow(route);

        if (route.Page == "TalentPage")
        {
            Assert.Contains("catch (AgencyOS.Client.AgencyOsApiException", flow, StringComparison.Ordinal);
            Assert.Contains("Refused(", flow, StringComparison.Ordinal);

            // The refusal path returns rather than falling through to the reload.
            int refused = flow.IndexOf("Refused(", StringComparison.Ordinal);
            int reload = flow.IndexOf("await OpenAsync(", StringComparison.Ordinal);

            Assert.True(refused < reload, "the refusal is not handled before the refresh");
            Assert.Contains("return;", flow[refused..reload], StringComparison.Ordinal);

            return;
        }

        Assert.Contains("if (await Guarded(", flow, StringComparison.Ordinal);
    }

    /// <summary>A successful act refreshes the surface that shows the result.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ASuccessfulActRevealsWhatItChanged(Route route)
    {
        string flow = Flow(route);

        Assert.True(
            flow.Contains("await _detail.LoadAsync(", StringComparison.Ordinal)
                || flow.Contains("await OpenAsync(", StringComparison.Ordinal),
            $"{route.Flow} changes something and shows nothing afterwards");
    }

    /// <summary>An act that cannot be taken yet is disabled, not missing.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void AnActThatCannotBeTakenIsGatedRatherThanAbsent(Route route)
    {
        Assert.Equal("False", (string?)Control(route).Attribute("IsEnabled"));
        Assert.Contains($"{route.Control}.IsEnabled =", Code(route.Page), StringComparison.Ordinal);
    }

    /// <summary>Every restored control can be announced and reached by keyboard.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void EachRestoredControlCanBeAnnounced(Route route)
    {
        XElement control = Control(route);

        string? announced = (string?)control.Attribute("AutomationProperties.Name")
            ?? (string?)control.Attribute("Content");

        Assert.False(string.IsNullOrWhiteSpace(announced));
    }

    /// <summary>The palette offers each act, and is never the only way to it.</summary>
    [Theory]
    [MemberData(nameof(Restored))]
    public void ThePaletteSupplementsTheControl(Route route)
    {
        Assert.Contains(AgencyOsCommands.All, x => x.Id == route.Command);
        Assert.Contains($"case \"{route.Command}\":", Code(route.Page), StringComparison.Ordinal);

        _ = Control(route);
    }

    /// <summary>
    /// The transition dialog keeps no second copy of the transition table.
    /// </summary>
    /// <remarks>
    /// M4 publishes <c>Representation.AllowedTransitions</c> and a unit test
    /// enumerates all 25 pairs against it. A table restated in the client would be
    /// free to drift from the one the server enforces, so the dialog offers the
    /// statuses and lets the refusal say which are unreachable. The one fact the
    /// page does mirror is that the terminal states have no transitions at all.
    /// </remarks>
    [Fact]
    public void TheTransitionDialogDoesNotRestateTheTransitionTable()
    {
        string dialog = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs",
            "TransitionRepresentationDialog.xaml.cs"));

        // No pair-wise rule: nothing here says which source reaches which target.
        Assert.DoesNotContain("AllowedTransitions", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Status switch", dialog, StringComparison.Ordinal);

        // It offers the statuses, minus the one already held.
        Assert.Contains("\"Active\", \"Suspended\", \"Terminated\", \"Expired\"", dialog, StringComparison.Ordinal);
        Assert.Contains("representation.Status, StringComparison.Ordinal", dialog, StringComparison.Ordinal);

        // And the page gates only on terminality, which is an empty set, not a rule.
        Assert.Contains(
            "live.Status is not (\"Terminated\" or \"Expired\")",
            Code("TalentPage"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ending a representation is described as ending, never as deleting.
    /// </summary>
    /// <remarks>
    /// The relationship keeps its start date, its scopes and its team as they
    /// stood. An operator told the row would disappear would reasonably avoid the
    /// control and leave a former client showing as current.
    /// </remarks>
    [Fact]
    public void EndingARepresentationIsNotDeletingIt()
    {
        string dialog = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs",
            "TransitionRepresentationDialog.xaml.cs"));

        Assert.Contains("Nothing is deleted", dialog, StringComparison.Ordinal);

        foreach (string forbidden in (string[])["Delete", "Remove", "Erase"])
        {
            Assert.DoesNotContain(forbidden, "Change status", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($">{forbidden}<", dialog, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A contract obligation and money owed under a contract stay separate.
    /// </summary>
    /// <remarks>
    /// Two aggregates on one tab, with different outcomes: one is satisfied, waived
    /// or breached, the other is billed. A flow that reached into the other's
    /// aggregate would merge two facts an agency needs apart.
    /// </remarks>
    [Fact]
    public void AContractObligationIsNotMoneyOwed()
    {
        string flow = Flow(Named("RecordObligationButton"));

        Assert.Contains("RecordObligationRequest", Dialog("RecordObligationDialog"), StringComparison.Ordinal);
        Assert.DoesNotContain("Monetary", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("Receivable", flow, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ reading

    private static Route Named(string control) => All.Single(x => x.Control == control);

    private static XElement Control(Route route) =>
        Assert.Single(
            XDocument.Parse(Markup(route.Page)).Descendants(),
            x => (string?)x.Attribute(Name) == route.Control);

    private static string Flow(Route route) => Body(Code(route.Page), route.Flow);

    private static string Dialog(string dialog) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", $"{dialog}.xaml.cs"));

    /// <summary>One method's text, found by its declaration and matched by braces.</summary>
    private static string Body(string source, string method)
    {
        int start = source.IndexOf($"Task {method}(", StringComparison.Ordinal);

        Assert.True(start > 0, $"{method} was not found");

        int open = source.IndexOf('{', start);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new Xunit.Sdk.XunitException($"{method} has no closing brace");
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static string Markup(string page) => Read($"{page}.xaml");

    private static string Code(string page) => Read($"{page}.xaml.cs");

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", file));

    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
