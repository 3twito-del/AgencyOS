using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the prediction rows and the project role rows actually reach the words
/// their formatters were tested to say.
/// </summary>
/// <remarks>
/// <para>
/// F-14, F-15 and F-16. What a row says is decided in <c>ForecastRowTests</c>,
/// which executes <c>ForecastLine</c> and <c>RowLabel</c> against the shipped
/// response types. These prove the pages still route through them: a correct
/// formatter that no template binds is the F-07 failure, a claim with nothing
/// behind it.
/// </para>
/// </remarks>
public sealed class ForecastSurfaceTests
{
    /// <summary>No prediction field that needs formatting is bound raw anywhere on the page.</summary>
    /// <remarks>
    /// <c>ResolvesBy</c> raw wrote a UTC instant in the host's day/month order, and
    /// <c>CurrentProbabilityByDisplayName</c> raw put an unattributed name where an
    /// operator reads an owner.
    /// </remarks>
    [Theory]
    [InlineData("ResolvesBy")]
    [InlineData("CurrentProbabilityByDisplayName")]
    [InlineData("CurrentProbability")]
    public void NoPredictionFieldIsBoundRaw(string field)
    {
        string markup = Page("IntelligencePage");

        Assert.DoesNotMatch(new Regex(@"\{Binding " + field + @"\}"), markup);
    }

    /// <summary>The prediction row shows the forecast caption, the date and its announcement.</summary>
    [Fact]
    public void ThePredictionRowShowsTheForecastAndTheDate()
    {
        string inner = List("IntelligencePage", "PredictionList");

        Assert.Contains("StaticResource PredictionCaption", inner, StringComparison.Ordinal);
        Assert.Contains("StaticResource PredictionDue", inner, StringComparison.Ordinal);
        Assert.Contains("StaticResource RowLabel", inner, StringComparison.Ordinal);
    }

    /// <summary>The desk's overdue list writes its date the same way.</summary>
    [Fact]
    public void TheAwaitingListWritesItsDateTheSameWay()
    {
        string inner = List("IntelligencePage", "AwaitingList");

        Assert.Contains("StaticResource PredictionDue", inner, StringComparison.Ordinal);
        Assert.Contains("StaticResource RowLabel", inner, StringComparison.Ordinal);
    }

    /// <summary>Both converters the rows name are registered.</summary>
    [Theory]
    [InlineData("PredictionCaption")]
    [InlineData("PredictionDue")]
    public void TheConvertersTheRowsNameAreRegistered(string key)
    {
        string app = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "App.xaml"));

        Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
    }

    /// <summary>A project role row shows the title that tells it from its siblings.</summary>
    /// <remarks>
    /// F-16. The row bound type and status only, so three <c>Actor</c> roles were
    /// three identical rows. The title is optional, so it collapses where unset
    /// rather than leaving an empty line.
    /// </remarks>
    [Fact]
    public void AProjectRoleRowShowsItsTitle()
    {
        string inner = List("ProjectsPage", "RoleList");

        Assert.Contains("Text=\"{Binding Label}\"", inner, StringComparison.Ordinal);
        Assert.Contains(
            "{Binding Label, Converter={StaticResource PresentVisibility}}",
            inner,
            StringComparison.Ordinal);
        Assert.Contains("StaticResource RowLabel", inner, StringComparison.Ordinal);
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string List(string page, string name) =>
        Assert.Single(
            XDocument.Parse(Page(page)).Descendants(),
            x => (string?)x.Attribute(X + "Name") == name).ToString();

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", $"{page}.xaml"));

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
