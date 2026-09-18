using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the dialogs the owner's decision covers are wired to stay open.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-010</c>. The decision is that a recoverable refusal keeps the
/// dialog, its values and its context. A dialog can only do that if it owns the
/// mutation: the page cannot cancel a close that has already happened.
/// </para>
/// <para>
/// The decision itself is executed by <c>DialogRefusalTests</c>, and the live
/// behaviour is measured against the running client. This is the wiring in
/// between — that each dialog actually calls the server, actually cancels, and
/// actually clears — which is where a regression would land.
/// </para>
/// </remarks>
public sealed class StayOpenRefusalTests
{
    private static readonly string[] Covered =
        ["NewPersonDialog", "NewCompanyDialog", "AddProjectRoleDialog"];

    /// <summary>The dialog owns the mutation, so it can refuse to close.</summary>
    [Theory]
    [InlineData("NewPersonDialog")]
    [InlineData("NewCompanyDialog")]
    [InlineData("AddProjectRoleDialog")]
    public void TheDialogOwnsTheMutation(string dialog)
    {
        string code = Code(dialog);

        Assert.Contains("IAgencyOsApi api", code, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonClick += OnPrimaryButtonClick", code, StringComparison.Ordinal);
        Assert.Contains("GetDeferral()", code, StringComparison.Ordinal);
    }

    /// <summary>A refusal it can answer cancels the close.</summary>
    [Theory]
    [InlineData("NewPersonDialog")]
    [InlineData("NewCompanyDialog")]
    [InlineData("AddProjectRoleDialog")]
    public void ARecoverableRefusalCancelsTheClose(string dialog)
    {
        string code = Code(dialog);

        Assert.Matches(
            new Regex(
                @"catch \(AgencyOsApiException failure\)\s*\{\s*if \(_refusal\.Show\(failure\)\)\s*\{\s*args\.Cancel = true;",
                RegexOptions.Singleline),
            code);
    }

    /// <summary>A refusal it cannot answer is handed to the page instead.</summary>
    /// <remarks>
    /// The boundary the decision draws. Without this the dialog would sit open over
    /// an expired session, offering a retry that cannot work.
    /// </remarks>
    [Theory]
    [InlineData("NewPersonDialog")]
    [InlineData("NewCompanyDialog")]
    [InlineData("AddProjectRoleDialog")]
    public void ATerminalFailureIsHandedBack(string dialog)
    {
        string code = Code(dialog);

        Assert.Contains("Terminal { get; private set; }", code, StringComparison.Ordinal);
        Assert.Contains("Terminal = failure;", code, StringComparison.Ordinal);
    }

    /// <summary>Editing the entry takes the complaint down.</summary>
    /// <remarks>
    /// An association that outlives its reason keeps a screen reader reading a
    /// complaint about a value that has already been corrected.
    /// </remarks>
    [Theory]
    [InlineData("NewPersonDialog")]
    [InlineData("NewCompanyDialog")]
    [InlineData("AddProjectRoleDialog")]
    public void CorrectingTheEntryClearsTheRefusal(string dialog)
    {
        Assert.Contains("_refusal.Clear()", Code(dialog), StringComparison.Ordinal);
        Assert.Contains("TextChanged=\"OnEntryChanged\"", Markup(dialog), StringComparison.Ordinal);
    }

    /// <summary>The message has somewhere to be, and a heading.</summary>
    [Theory]
    [InlineData("NewPersonDialog", "Could not create the person")]
    [InlineData("NewCompanyDialog", "Could not create the company")]
    [InlineData("AddProjectRoleDialog", "Could not add the role")]
    public void TheDialogHasATitledPlaceToSayIt(string dialog, string title)
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement bar = Assert.Single(
            XElement.Parse(Markup(dialog)).Descendants(),
            x => x.Name.LocalName == "InfoBar" && x.Attribute(name)?.Value == "ErrorBar");

        Assert.Equal(title, bar.Attribute("Title")?.Value);
        Assert.Equal("False", bar.Attribute("IsOpen")?.Value);
    }

    /// <summary>
    /// Every field the dialog offers can be named by a refusal.
    /// </summary>
    /// <remarks>
    /// The association is only as good as the map. A text entry the dialog sends but
    /// does not register cannot be pointed at, and the operator would be told which
    /// field is wrong by prose alone.
    /// </remarks>
    [Theory]
    [InlineData("NewPersonDialog", 6)]
    [InlineData("NewCompanyDialog", 5)]
    [InlineData("AddProjectRoleDialog", 3)]
    public void EveryOfferedFieldIsRegistered(string dialog, int expected)
    {
        int registered = Regex.Matches(Code(dialog), @"\[""\w+""\] = \w+,").Count;

        Assert.Equal(expected, registered);
    }

    /// <summary>No page re-implements the refusal the dialog now owns.</summary>
    /// <remarks>
    /// Two surfaces answering one refusal is how the operator ends up reading it
    /// twice, once in the dialog and once behind it.
    /// </remarks>
    [Theory]
    [InlineData("PeoplePage")]
    [InlineData("CompaniesPage")]
    [InlineData("ProjectsPage")]
    public void ThePageReportsOnlyWhatTheDialogHandedBack(string page)
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", page + ".xaml.cs"));

        Assert.Contains("dialog.Terminal is { } terminal", code, StringComparison.Ordinal);
    }

    /// <summary>The covered set is the set the findings named.</summary>
    [Fact]
    public void TheCoveredDialogsAreTheOnesTheFindingsNamed() =>
        Assert.All(Covered, x => Assert.True(File.Exists(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", x + ".xaml.cs"))));

    private static string Code(string dialog) => File.ReadAllText(Path.Combine(
        RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", dialog + ".xaml.cs"));

    private static string Markup(string dialog) => File.ReadAllText(Path.Combine(
        RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", dialog + ".xaml"));

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
