using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Structural guards on the prospects surface and the talent-profile
// dialog: the markup that exposes the step and the code-behind that wires it. The
// behaviour is executed in SignedClientTalentProfileTests (client and integration).

/// <summary>
/// Where the talent-profile step is offered after signing a client, and how.
/// </summary>
/// <remarks>
/// <para>
/// The final-candidate RC at <c>f29a2ee</c> stopped at C7 step 2: a client signed on
/// the prospects surface could not reach Talent or a talent pursuit, because both are
/// built from talent profiles and signing does not create one. Owner decision C kept
/// the two apart and made the profile an explicit operator step.
/// </para>
/// <para>
/// <strong>What these prove, and no more.</strong> That the step is declared where the
/// client is signed, named for what it does, reachable as an ordinary button, and
/// opens a dialog whose fields are labelled and whose person cannot be changed. Page
/// and dialog code-behind cannot be constructed off a UI thread, so this is markup and
/// wiring; whether an operator finds it live is for the release-candidate run.
/// </para>
/// </remarks>
public sealed class SignedClientProfileSurfaceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The step sits directly beneath the line that says the person is now a client.
    /// </summary>
    [Fact]
    public void TheStepIsOfferedWhereTheClientIsSigned()
    {
        XElement page = XElement.Load(Source("Pages", "ProspectsPage.xaml"));
        XElement line = Named(page, "ProspectLine");
        XElement? next = line.ElementsAfterSelf().FirstOrDefault();

        Assert.NotNull(next);
        Assert.Equal("InfoBar", next!.Name.LocalName);
        Assert.Equal("ProfileBar", next.Attribute(Xaml + "Name")?.Value);

        // Closed until there is somebody to say it about; the view model decides.
        Assert.Equal("False", next.Attribute("IsOpen")?.Value);
    }

    /// <summary>
    /// The action is an ordinary button, named for what it does, in the bar's action slot.
    /// </summary>
    [Fact]
    public void TheActionIsAnOrdinaryNamedButton()
    {
        XElement page = XElement.Load(Source("Pages", "ProspectsPage.xaml"));
        XElement button = Named(page, "CreateProfileButton");

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("InfoBar.ActionButton", button.Parent?.Name.LocalName);
        Assert.Equal("Create talent profile…", button.Attribute("Content")?.Value);
        Assert.Equal("Create talent profile…", button.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("OnCreateProfileClick", button.Attribute("Click")?.Value);

        // Not taken out of the keyboard order.
        Assert.Null(button.Attribute("IsTabStop"));
        Assert.Null(button.Attribute("IsEnabled"));
    }

    /// <summary>
    /// The handler opens the dialog and creates the profile only on confirmation; signing
    /// never does.
    /// </summary>
    [Fact]
    public void TheProfileIsCreatedOnlyByTheConfirmedStep()
    {
        string code = File.ReadAllText(Source("Pages", "ProspectsPage.xaml.cs"));

        string create = Body(code, "private async Task CreateProfileAsync()");

        Assert.Contains("CreateTalentProfileDialog dialog = new(client.DisplayName)", create, StringComparison.Ordinal);
        Assert.Contains("!= ContentDialogResult.Primary", create, StringComparison.Ordinal);
        Assert.Contains(".CreateTalentProfileAsync(dialog.CareerStage)", create, StringComparison.Ordinal);

        string convert = Body(code, "private async Task ConvertAsync()");

        Assert.DoesNotContain("CreateTalentProfile", convert, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dialog names its buttons truthfully, commits on Enter, and labels its field.
    /// </summary>
    [Fact]
    public void TheDialogIsLabelledAndCommitsOnEnter()
    {
        XElement dialog = XElement.Load(Source("Dialogs", "CreateTalentProfileDialog.xaml"));

        Assert.Equal("Create talent profile", dialog.Attribute("Title")?.Value);
        Assert.Equal("Create", dialog.Attribute("PrimaryButtonText")?.Value);
        Assert.Equal("Cancel", dialog.Attribute("CloseButtonText")?.Value);
        Assert.Equal("Primary", dialog.Attribute("DefaultButton")?.Value);

        XElement stage = Named(dialog, "CareerStageBox");

        Assert.Equal("Career stage", stage.Attribute("Header")?.Value);
        Assert.Equal("Career stage", stage.Attribute("AutomationProperties.Name")?.Value);
    }

    /// <summary>
    /// The person cannot be changed in the dialog: career stage is its only choice.
    /// </summary>
    [Fact]
    public void TheDialogHasNoPersonPicker()
    {
        XElement dialog = XElement.Load(Source("Dialogs", "CreateTalentProfileDialog.xaml"));

        string[] inputs = [.. dialog.Descendants()
            .Where(x => x.Name.LocalName is "ComboBox" or "AutoSuggestBox" or "TextBox" or "ListView")
            .Select(x => x.Attribute(Xaml + "Name")?.Value ?? x.Name.LocalName)];

        Assert.Equal(["CareerStageBox"], inputs);
    }

    /// <summary>
    /// The career stages offered are exactly the domain's, and Unknown - its honest
    /// unassessed value - is the default.
    /// </summary>
    [Fact]
    public void TheStagesAreTheDomainsAndUnknownIsTheDefault()
    {
        XElement stage = Named(XElement.Load(Source("Dialogs", "CreateTalentProfileDialog.xaml")), "CareerStageBox");

        string[] offered = [.. stage.Elements().Select(x => x.Attribute("Tag")?.Value ?? string.Empty)];

        string domain = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Domain", "Talent", "TalentProfile.cs"));

        string[] declared = [.. Regex.Matches(
                Regex.Match(domain, @"public enum CareerStage\s*\{(?<body>[^}]*)\}").Groups["body"].Value,
                @"(?<name>\w+)\s*=\s*\d+")
            .Select(x => x.Groups["name"].Value)];

        Assert.Equal(declared, offered);

        XElement selected = Assert.Single(stage.Elements(), x => x.Attribute("IsSelected")?.Value == "True");

        Assert.Equal("Unknown", selected.Attribute("Tag")?.Value);
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

    private static string Source(string folder, string file) =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", folder, file);

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
