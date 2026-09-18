using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That a list row is announced by its template, not by its record.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-018</c> recorded the command palette announcing
/// <c>PaletteCommand { Id = go.people, Title = Go to People, … }</c> and read the
/// cause as the name being set on the template's root rather than on the item
/// container. The palette does not do that on this build: all 46 rows announce
/// their command, and the container is what announces it.
/// </para>
/// <para>
/// Naming the template root is what fixes it. A root with
/// <c>AutomationProperties.Name</c> gets a peer of its own, and the
/// <c>ListViewItem</c> takes its name from that child instead of falling back to
/// the item's <c>ToString()</c> — which is what it does when the root is
/// anonymous. So the property worth pinning is that every row template names
/// itself; the finding is not reproducible, and this is what keeps it that way.
/// </para>
/// </remarks>
public sealed class RowNameStructureTests
{
    /// <summary>Every row template says what its row is.</summary>
    [Fact]
    public void EveryRowTemplateNamesItsRow()
    {
        List<string> anonymous = [];

        foreach (string markup in Markup())
        {
            foreach (XElement template in XElement.Load(markup)
                .Descendants()
                .Where(x => x.Name.LocalName == "DataTemplate"))
            {
                XElement? root = template.Elements().FirstOrDefault();

                if (root is not null && root.Attribute("AutomationProperties.Name") is null)
                {
                    anonymous.Add($"{Path.GetFileName(markup)}: {root.Name.LocalName}");
                }
            }
        }

        Assert.True(
            anonymous.Count == 0,
            "A row template leaves its row unnamed, so the container falls back to "
                + "the record's ToString() (AOS-R002-018): " + string.Join("; ", anonymous));
    }

    /// <summary>The palette, which the finding named, is one of them.</summary>
    /// <remarks>
    /// Named explicitly because the finding is about this surface, and because a
    /// product-wide sweep passing says nothing about whether the palette was in
    /// it.
    /// </remarks>
    [Fact]
    public void ThePaletteNamesItsRows()
    {
        XElement shell = XElement.Load(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml"));

        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement palette = Assert.Single(
            shell.Descendants(),
            x => x.Attribute(name)?.Value == "PaletteResults");

        XElement root = Assert.Single(palette
            .Descendants()
            .Where(x => x.Name.LocalName == "DataTemplate")
            .SelectMany(x => x.Elements()));

        Assert.NotNull(root.Attribute("AutomationProperties.Name"));
    }

    private static IEnumerable<string> Markup() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"),
                "*.xaml",
                SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

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
