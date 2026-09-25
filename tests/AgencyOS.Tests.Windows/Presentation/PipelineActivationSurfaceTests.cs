using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Structural guards on the Pipeline action row and the code-behind
// that creates, activates and reveals a pursuit. The activation outcomes are executed
// in OpportunityActivationTests (client) against the view model and the fake API;
// the reveal guard is structural only, because the page cannot be built off a UI thread.

/// <summary>
/// That a pursuit created in the Windows client can be found and activated there.
/// </summary>
/// <remarks>
/// <para>
/// The fresh final-candidate RC at <c>2d2b46a</c> stopped before its operator
/// handoff: a pursuit is created as Draft, only an Active pursuit accepts market
/// activity (a target move, a negotiation), and nothing in the Windows client
/// activated one. The new Draft also vanished from the list, which defaults to
/// Active, as soon as it was created.
/// </para>
/// <para>
/// <strong>What these prove, and no more.</strong> That the action is declared on the
/// Pipeline action row, named for what it does, reachable as an ordinary button,
/// offered only for a Draft, wired to the existing status command through the view
/// model, never run by creation, and that creation reveals the pursuit it made. Page
/// code-behind cannot be constructed off a UI thread; live discoverability is for the
/// release-candidate run.
/// </para>
/// </remarks>
public sealed class PipelineActivationSurfaceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The action sits on the Pipeline action row, beside the other pursuit actions.</summary>
    [Fact]
    public void TheActionIsAnOrdinaryNamedButtonOnTheActionRow()
    {
        XElement page = XElement.Load(Source("PipelinePage.xaml"));
        XElement button = Named(page, "ActivateButton");

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("Activate opportunity", button.Attribute("Content")?.Value);
        Assert.Equal("Activate opportunity", button.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("OnActivateClick", button.Attribute("Click")?.Value);

        // Same row as New opportunity and Move target.
        Assert.Same(Named(page, "NewButton").Parent, button.Parent);
        Assert.Same(Named(page, "StageButton").Parent, button.Parent);

        // Not taken out of the keyboard order; shown only when there is a Draft.
        Assert.Null(button.Attribute("IsTabStop"));
        Assert.Equal("Collapsed", button.Attribute("Visibility")?.Value);
    }

    /// <summary>
    /// The action is offered for a Draft only, and runs the view model's activation.
    /// </summary>
    [Fact]
    public void TheActionIsOfferedForADraftAndActivatesThroughTheViewModel()
    {
        string code = File.ReadAllText(Source("PipelinePage.xaml.cs"));

        Assert.Contains(
            "ActivateButton.Visibility = _detail.CanActivate ? Visibility.Visible : Visibility.Collapsed;",
            Body(code, "private void RenderDetail()"),
            StringComparison.Ordinal);

        string activate = Body(code, "private async Task ActivateAsync()");

        Assert.Contains("_detail.ActivateAsync()", activate, StringComparison.Ordinal);
        Assert.Contains("Guarded(", activate, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creating a pursuit reveals the one created - still Draft - and never activates it.
    /// </summary>
    [Fact]
    public void CreationRevealsTheDraftAndDoesNotActivateIt()
    {
        string code = File.ReadAllText(Source("PipelinePage.xaml.cs"));
        string create = Body(code, "private async Task CreateAsync()");

        Assert.Contains("created = await api.CreateOpportunityAsync(", create, StringComparison.Ordinal);
        Assert.Contains("Reveal(made.Opportunity.Id)", create, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeOpportunityStatus", create, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateAsync", create, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only an activation the server accepted and the view model then read back is
    /// revealed and announced as active; an accepted one whose refresh failed says so.
    /// </summary>
    /// <remarks>
    /// Correcting <c>9c51443</c>, where any activation that did not throw was
    /// announced, including one whose re-read had failed.
    /// </remarks>
    [Fact]
    public void OnlyAConfirmedActivationIsRevealedAndAnnounced()
    {
        string code = File.ReadAllText(Source("PipelinePage.xaml.cs"));
        string activate = Body(code, "private async Task ActivateAsync()");

        // One request, through the page's refusal path.
        Assert.Single(Occurrences(activate, "_detail.ActivateAsync()"));
        Assert.Contains("Guarded(", activate, StringComparison.Ordinal);

        // Everything that claims success sits behind the confirmed outcome.
        const string Confirmed = "if (outcome != OpportunityActivation.Activated)";
        int gate = activate.IndexOf(Confirmed, StringComparison.Ordinal);

        Assert.True(gate >= 0, "Success is not gated on the confirmed outcome.");
        Assert.Contains("return;", Body(activate, Confirmed), StringComparison.Ordinal);

        string before = activate[..gate];

        Assert.DoesNotContain("Reveal(", before, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Opportunity activated\"", before, StringComparison.Ordinal);
        Assert.DoesNotContain(" is active", before, StringComparison.Ordinal);

        string after = activate[gate..];

        Assert.Contains("Reveal(id)", after, StringComparison.Ordinal);
        Assert.Contains("\"Opportunity activated\"", after, StringComparison.Ordinal);

        // Accepted but not re-read: an honest notice, not a refusal, not a retry.
        string unrefreshed = Body(activate, "if (outcome == OpportunityActivation.AcceptedNotRefreshed)");

        Assert.Contains("DetailWarning(", unrefreshed, StringComparison.Ordinal);
        Assert.Contains("accepted", unrefreshed, StringComparison.Ordinal);
        Assert.Contains("could not be refreshed. Refresh before continuing.", unrefreshed, StringComparison.Ordinal);
        Assert.Contains("return;", unrefreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("DetailError(", unrefreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("Reveal(", unrefreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateAsync", unrefreshed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reveal widens every filter before any load starts, and starts exactly one load
    /// against the widened filters; a filter the operator changes still reloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Correcting <c>9c51443</c>. Setting the status and kind boxes raises their
    /// SelectionChanged, wired to <c>OnFilterChanged</c>, which started a load each time
    /// - against a filter still half-widened. That load could complete without the
    /// pursuit, and <c>SelectRevealed</c> would then report it missing and clear
    /// <c>_reveal</c> before the final load arrived.
    /// </para>
    /// <para>
    /// Structural, not live: it shows the guard is set around every programmatic filter
    /// change, that the handler honours it, and that one load follows. That WinUI
    /// raises SelectionChanged synchronously inside the guard is the platform's
    /// behaviour for a programmatic selection, not something run here.
    /// </para>
    /// </remarks>
    [Fact]
    public void RevealWidensEveryFilterBeforeItsOneLoad()
    {
        XElement page = XElement.Load(Source("PipelinePage.xaml"));

        // The filters that can start a load, and how.
        Assert.Equal("OnFilterChanged", Named(page, "StatusBox").Attribute("SelectionChanged")?.Value);
        Assert.Equal("OnFilterChanged", Named(page, "KindBox").Attribute("SelectionChanged")?.Value);

        // A checkbox's Click is raised by the operator, not by setting IsChecked; the
        // search box loads on submission only, not on its text being set.
        Assert.Equal("OnFilterChanged", Named(page, "AwaitingBox").Attribute("Click")?.Value);
        Assert.Null(Named(page, "AwaitingBox").Attribute("Checked"));
        Assert.Null(Named(page, "AwaitingBox").Attribute("Unchecked"));
        Assert.Null(Named(page, "SearchBox").Attribute("TextChanged"));

        string code = File.ReadAllText(Source("PipelinePage.xaml.cs"));
        string reveal = Body(code, "public void Reveal(Guid record)");

        int raise = reveal.IndexOf("_widening = true;", StringComparison.Ordinal);
        int lower = reveal.IndexOf("_widening = false;", StringComparison.Ordinal);

        Assert.True(raise >= 0 && lower > raise, "Reveal does not guard its filter changes.");

        // Lowered in a finally, so a throw cannot leave the operator's filters dead.
        string guarded = reveal[raise..lower];

        Assert.Contains("finally", guarded, StringComparison.Ordinal);

        // Every programmatic filter change is inside the guard.
        foreach (string change in (string[])
            [
                "Choose(StatusBox, string.Empty);",
                "Choose(KindBox, string.Empty);",
                "AwaitingBox.IsChecked = false;",
                "SearchBox.Text = string.Empty;",
            ])
        {
            Assert.Single(Occurrences(reveal, change));
            Assert.Contains(change, guarded, StringComparison.Ordinal);
        }

        // Exactly one load, after the guard is lowered; none inside it.
        Assert.Single(Occurrences(reveal, "LoadAsync()"));
        Assert.DoesNotContain("LoadAsync()", reveal[..lower], StringComparison.Ordinal);

        // The handler starts nothing while the guard is up, and loads otherwise.
        string handler = Body(code, "private void OnFilterChanged(object sender, RoutedEventArgs e)");
        const string Guard = "if (_list is null || _widening)";
        int check = handler.IndexOf(Guard, StringComparison.Ordinal);
        int load = handler.IndexOf("_ = LoadAsync();", StringComparison.Ordinal);

        Assert.True(check >= 0, "The filter handler ignores the reveal guard.");
        Assert.Contains("return;", Body(handler, Guard), StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync", Body(handler, Guard), StringComparison.Ordinal);
        Assert.True(load > check, "The filter handler no longer loads for the operator.");

        // Nothing else raises or lowers the guard, so it cannot swallow an operator's change.
        Assert.Single(Occurrences(code, "_widening = true;"));
        Assert.Single(Occurrences(code, "_widening = false;"));

        // The request is settled only by a completed load.
        Assert.Single(Occurrences(code, "SelectRevealed();"));
        Assert.Contains("SelectRevealed();", Body(code, "private async Task LoadAsync()"), StringComparison.Ordinal);
    }

    private static List<int> Occurrences(string text, string value)
    {
        List<int> found = [];

        for (int i = text.IndexOf(value, StringComparison.Ordinal);
             i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            found.Add(i);
        }

        return found;
    }

    private static XElement Named(XElement root, string name) =>
        root.DescendantsAndSelf().Single(x => x.Attribute(Xaml + "Name")?.Value == name);

    /// <summary>A method's body, from its signature to the matching brace.</summary>
    private static string Body(string code, string signature)
    {
        int start = code.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start >= 0, signature + " is not declared.");

        int open = code.IndexOf('{', start);
        int depth = 0;

        for (int i = open; i < code.Length; i++)
        {
            depth += code[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return code[open..(i + 1)];
            }
        }

        return code[open..];
    }

    private static string Source(string file) =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", file);

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
