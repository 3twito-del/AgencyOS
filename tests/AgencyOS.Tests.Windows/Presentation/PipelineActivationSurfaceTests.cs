using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Structural guards on the Pipeline action row and the code-behind
// that creates and activates a pursuit. The behaviour is executed in
// OpportunityActivationTests (client) against the view model and the fake API.

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
