using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

// SOURCE-PROOF: Asserts that controls declare accessible names, notice titles,
// unfixed text heights and themed colours. Each is a property of what the markup
// declares.

/// <summary>
/// That every control a keyboard or a screen reader has to reach can be named.
/// </summary>
/// <remarks>
/// <para>
/// A structural test over the shipped XAML rather than a checklist somebody
/// re-reads. Accessibility regressions are silent — a control with no accessible
/// name is announced as "list" or "button" and the page still looks right — so
/// the only way this stays true is if adding an unnamed control fails a build
/// (§Z, ADR-0034).
/// </para>
/// <para>
/// The rules encode how WinUI actually derives a name. A <c>Button</c> takes it
/// from <c>Content</c>, a <c>ComboBox</c> or <c>TextBox</c> from <c>Header</c>,
/// an <c>InfoBar</c> from <c>Title</c>. Controls with none of those — a
/// <c>ListView</c> especially — need <c>AutomationProperties.Name</c>, and that
/// is the gap this test was written to find.
/// </para>
/// <para>
/// This is automated evidence about markup. It is not evidence that anybody has
/// operated the application with a screen reader, and the M13 report says so.
/// </para>
/// </remarks>
public sealed class XamlAccessibilityTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Controls that carry their own accessible name, and where it comes from.
    /// </summary>
    /// <remarks>
    /// A control is acceptable when it has any of its listed attributes or an
    /// explicit <c>AutomationProperties.Name</c>. Listing the sources rather than
    /// requiring the explicit attribute everywhere keeps the markup readable: a
    /// button whose content is its label does not need the label twice.
    /// </remarks>
    private static readonly Dictionary<string, string[]> NameSources = new(StringComparer.Ordinal)
    {
        ["Button"] = ["Content"],
        ["HyperlinkButton"] = ["Content"],
        ["ToggleSwitch"] = ["Header"],
        ["CheckBox"] = ["Content"],
        ["RadioButton"] = ["Content"],
        // PlaceholderText is NOT a name source for a ComboBox. WinUI promotes it
        // for TextBox and AutoSuggestBox - Audit 001 confirmed that at runtime,
        // which is why those two keep it - and does not for ComboBox. This rule
        // said otherwise, so fifteen combo boxes announced nothing while 521
        // Windows tests passed (AOS-R001-004).
        ["ComboBox"] = ["Header"],
        ["TextBox"] = ["Header", "PlaceholderText"],
        ["AutoSuggestBox"] = ["Header", "PlaceholderText"],
        ["DatePicker"] = ["Header"],
        ["NumberBox"] = ["Header"],

        // No intrinsic source. A list announced as "list" tells a screen-reader
        // user nothing about which of the six on the page they are in.
        ["ListView"] = [],
        ["GridView"] = [],
        ["ProgressBar"] = [],
        ["ProgressRing"] = [],
    };

    public static TheoryData<string> XamlFiles
    {
        get
        {
            TheoryData<string> data = [];

            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"),
                "*.xaml",
                SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RepositoryRoot, file);

                // Build output holds copies of the same markup. Scanning them
                // would report every finding twice and would make the suite
                // depend on which configurations happen to have been built.
                if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                data.Add(relative);
            }

            return data;
        }
    }

    /// <summary>Every interactive control can be announced.</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void EveryInteractiveControlHasAnAccessibleName(string relativePath)
    {
        List<string> unnamed = [];

        foreach (XElement element in Load(relativePath).DescendantsAndSelf())
        {
            if (!NameSources.TryGetValue(element.Name.LocalName, out string[]? sources))
            {
                continue;
            }

            // Inside a DataTemplate the control is a row, not a control on the
            // page: it takes its name from the data it binds, and the template's
            // owner carries the name for the list as a whole.
            if (element.Ancestors().Any(x => x.Name.LocalName.EndsWith(
                "ItemTemplate", StringComparison.Ordinal)))
            {
                continue;
            }

            bool named = element.Attribute("AutomationProperties.Name") is not null
                || sources.Any(source => element.Attribute(source) is not null);

            if (!named)
            {
                unnamed.Add(
                    $"{element.Name.LocalName} "
                        + $"'{element.Attribute(Xaml + "Name")?.Value ?? "(anonymous)"}'");
            }
        }

        Assert.True(
            unnamed.Count == 0,
            $"{relativePath} has controls a screen reader cannot announce: "
                + string.Join("; ", unnamed));
    }

    /// <summary>
    /// Every notice carries a title.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <c>InfoBar</c> with only a message is announced without saying what kind
    /// of notice it is, and its severity is conveyed by colour alone — which is
    /// the one thing accessibility guidance is unambiguous about.
    /// </para>
    /// <para>
    /// A title set from code-behind counts. Several notices say different things
    /// depending on what happened — a verification bar titles itself with the
    /// verification state — and requiring a literal in the markup would only buy a
    /// placeholder that is overwritten before the bar is ever shown. What matters
    /// is that a title exists by the time a reader meets it, so the rule is that
    /// every notice is titled somewhere.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void EveryNoticeHasATitle(string relativePath)
    {
        string behind = Path.Combine(RepositoryRoot, relativePath + ".cs");
        string code = File.Exists(behind) ? File.ReadAllText(behind) : string.Empty;

        List<string> untitled =
        [
            .. Load(relativePath)
                .DescendantsAndSelf()
                .Where(x => x.Name.LocalName == "InfoBar")
                .Where(x => x.Attribute("Title") is null)
                .Select(x => x.Attribute(Xaml + "Name")?.Value)
                .Where(name => name is null
                    || !code.Contains($"{name}.Title", StringComparison.Ordinal))
                .Select(name => name ?? "(anonymous)"),
        ];

        Assert.True(
            untitled.Count == 0,
            $"{relativePath} has InfoBars whose severity is colour alone: "
                + string.Join(", ", untitled));
    }

    /// <summary>
    /// Nothing encodes meaning in a hard pixel size that scaling would break.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fixed <c>Height</c> on a text-bearing control clips at 150% and 200%
    /// scaling, which is where dense professional screens fail first. Widths are
    /// allowed — a column of a known size is a layout decision — but a height that
    /// text has to fit inside is not (§19).
    /// </para>
    /// <para>
    /// This catches the markup case. Whether the result is usable at 200% is a
    /// manual check, and the report does not claim otherwise.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void NoTextControlHasAFixedHeight(string relativePath)
    {
        string[] textBearing =
            ["TextBlock", "Button", "ComboBox", "CheckBox", "ToggleSwitch", "HyperlinkButton"];

        List<string> fixedHeight =
        [
            .. Load(relativePath)
                .DescendantsAndSelf()
                .Where(x => textBearing.Contains(x.Name.LocalName, StringComparer.Ordinal))
                .Where(x => x.Attribute("Height") is not null)
                .Select(x =>
                    $"{x.Name.LocalName} '{x.Attribute(Xaml + "Name")?.Value ?? "(anonymous)"}'"),
        ];

        Assert.True(
            fixedHeight.Count == 0,
            $"{relativePath} fixes the height of text that has to grow when a user "
                + $"scales it: {string.Join(", ", fixedHeight)}");
    }

    /// <summary>
    /// Nothing paints a colour of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A literal <c>#RRGGBB</c> survives a high-contrast theme unchanged, so a
    /// status the markup paints red stays red on a background the theme has just
    /// turned black — and a person who chose high contrast because they cannot
    /// separate those colours loses the distinction entirely (§18, §20).
    /// </para>
    /// <para>
    /// Theme brushes are the whole mechanism: they are what a contrast theme
    /// replaces. This was already true of every page when M13 checked, and the
    /// test is here so it stays true rather than because it needed fixing.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void NoMarkupHardCodesAColour(string relativePath)
    {
        string[] painted = ["Foreground", "Background", "BorderBrush", "Fill", "Stroke"];

        List<string> literal =
        [
            .. Load(relativePath)
                .DescendantsAndSelf()
                .SelectMany(element => element.Attributes()
                    .Where(a => painted.Contains(a.Name.LocalName, StringComparer.Ordinal))
                    .Where(a => a.Value.StartsWith('#'))
                    .Select(a => $"{element.Name.LocalName}.{a.Name.LocalName}={a.Value}")),
        ];

        Assert.True(
            literal.Count == 0,
            $"{relativePath} paints colours a contrast theme cannot replace: "
                + string.Join(", ", literal));
    }

    /// <summary>The suite is actually looking at the shipped markup.</summary>
    /// <remarks>
    /// A structural test whose file discovery silently found nothing would pass
    /// forever and prove nothing.
    /// </remarks>
    [Fact]
    public void TheSuiteSeesTheRealMarkup()
    {
        Assert.True(XamlFiles.Count > 20, $"Only {XamlFiles.Count} XAML files were found.");
    }

    private static XElement Load(string relativePath) =>
        XElement.Load(Path.Combine(RepositoryRoot, relativePath));

    /// <summary>Walks up from the test binary to the repository root.</summary>
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
