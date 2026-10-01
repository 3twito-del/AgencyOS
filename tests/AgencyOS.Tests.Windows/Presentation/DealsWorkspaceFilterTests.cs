using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Asserts the status filter's declared items and the page's mapping of
// a selection to a query in code-behind. Both are declarations.

/// <summary>
/// The status filter the Deals workspace opens on.
/// </summary>
/// <remarks>
/// The operational-alpha evaluation drove the workspace live and found one row
/// against three deals in the API: the filter opened on <c>Negotiating</c>, so an
/// agreed negotiation waiting to be papered was not in the list. The behaviour is
/// pinned in <c>DealWorkspaceDefaultTests</c>; this pins the markup that chooses it,
/// because the defect was one <c>IsSelected</c> attribute.
/// </remarks>
public sealed class DealsWorkspaceFilterTests
{
    /// <summary>The workspace opens on live work, not on one status.</summary>
    [Fact]
    public void TheDefaultSelectionIsOpenWork()
    {
        XElement selected = Assert.Single(
            StatusItems(), x => x.Attribute("IsSelected")?.Value == "True");

        Assert.Equal(
            string.Empty,
            selected.Attribute("Tag")?.Value ?? string.Empty);

        Assert.False(string.IsNullOrWhiteSpace(selected.Attribute("Content")?.Value));
    }

    /// <summary>Negotiating is still explicitly selectable.</summary>
    /// <remarks>
    /// The repair widens the default; it does not take away the narrower view an
    /// operator may still want.
    /// </remarks>
    [Fact]
    public void NegotiatingRemainsAvailable()
    {
        Assert.Contains(StatusItems(), x => x.Attribute("Tag")?.Value == "Negotiating");
    }

    /// <summary>Every non-terminal status is still reachable on its own.</summary>
    [Fact]
    public void EveryLiveStatusIsStillSelectable()
    {
        IReadOnlyList<XElement> items = StatusItems();

        foreach (string status in (string[])["Draft", "Negotiating", "TermsAgreed"])
        {
            Assert.Contains(items, x => x.Attribute("Tag")?.Value == status);
        }
    }

    /// <summary>Asking for everything is a separate, explicit choice.</summary>
    /// <remarks>
    /// It carries its own tag rather than the empty one, so "show me live work" and
    /// "show me everything, closed included" cannot be the same selection.
    /// </remarks>
    [Fact]
    public void EverythingIsItsOwnChoice()
    {
        XElement all = Assert.Single(StatusItems(), x => x.Attribute("Tag")?.Value == "all");

        Assert.NotEqual("True", all.Attribute("IsSelected")?.Value);
    }

    /// <summary>The page distinguishes the three cases rather than two.</summary>
    [Fact]
    public void ThePageMapsTheSelectionToOpenWorkOrAStatus()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "DealsPage.xaml.cs"));

        Assert.Contains("_list.OpenOnly = string.IsNullOrEmpty(selected)", code, StringComparison.Ordinal);
        Assert.Contains("\"all\"", code, StringComparison.Ordinal);
    }

    private static IReadOnlyList<XElement> StatusItems()
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement box = Assert.Single(
            XElement.Load(Path.Combine(
                RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "DealsPage.xaml"))
                .Descendants(),
            x => x.Attribute(name)?.Value == "StatusBox");

        return [.. box.Elements()];
    }

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
