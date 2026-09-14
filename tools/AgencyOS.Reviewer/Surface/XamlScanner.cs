using System.Globalization;
using System.Xml.Linq;
using System.IO;

namespace AgencyOS.Reviewer.Surface;

/// <summary>One element in a XAML file.</summary>
/// <param name="File">Repository-relative path.</param>
/// <param name="Line">1-based line the element opens on.</param>
/// <param name="Element">Local element name, for example <c>Button</c>.</param>
/// <param name="Name">The <c>x:Name</c>, when it has one.</param>
/// <param name="Attributes">Every attribute, by local name.</param>
public sealed record XamlElement(
    string File,
    int Line,
    string Element,
    string? Name,
    IReadOnlyDictionary<string, string> Attributes)
{
    /// <summary>Reads an attribute by local name, or null.</summary>
    public string? Attribute(string name) =>
        Attributes.TryGetValue(name, out string? value) ? value : null;
}

/// <summary>What one XAML file contains.</summary>
/// <param name="RelativePath">Repository-relative path.</param>
/// <param name="RootElement">Page, Window or ContentDialog.</param>
/// <param name="ClassName">The <c>x:Class</c> the file declares.</param>
/// <param name="Elements">Every element, in document order.</param>
public sealed record XamlFile(
    string RelativePath,
    string RootElement,
    string? ClassName,
    IReadOnlyList<XamlElement> Elements);

/// <summary>
/// Reads the client's markup as data.
/// </summary>
/// <remarks>
/// XAML is XML, so this parses it rather than pattern-matching it. That matters
/// for the accessibility and consistency passes: an attribute written on a
/// following line, or a property set in element syntax, is invisible to a regex
/// and ordinary in real markup.
/// </remarks>
public static class XamlScanner
{
    /// <summary>Control elements a user can interact with.</summary>
    public static IReadOnlySet<string> Interactive { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Button", "HyperlinkButton", "ToggleButton", "RepeatButton", "DropDownButton", "SplitButton",
        "CheckBox", "RadioButton", "ToggleSwitch", "Slider", "ComboBox", "AutoSuggestBox",
        "TextBox", "PasswordBox", "RichEditBox", "NumberBox", "DatePicker", "TimePicker",
        "CalendarDatePicker", "ListView", "GridView", "TreeView", "ListBox", "Pivot", "TabView",
        "NavigationView", "MenuFlyoutItem", "ToggleMenuFlyoutItem", "AppBarButton", "AppBarToggleButton",
    };

    /// <summary>Elements that present a state rather than accept input.</summary>
    public static IReadOnlySet<string> StateElements { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "InfoBar", "ProgressBar", "ProgressRing",
    };

    /// <summary>Parses every XAML file under a prefix.</summary>
    public static IReadOnlyList<XamlFile> Scan(SourceIndex source, string prefix)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<XamlFile> files = [];

        foreach ((string path, string text) in source.Under(prefix, ".xaml"))
        {
            XDocument document;

            try
            {
                document = XDocument.Parse(text, LoadOptions.SetLineInfo);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            if (document.Root is not { } root)
            {
                continue;
            }

            List<XamlElement> elements = [];

            foreach (XElement element in document.Descendants())
            {
                Dictionary<string, string> attributes = new(StringComparer.Ordinal);

                foreach (XAttribute attribute in element.Attributes())
                {
                    attributes[attribute.Name.LocalName] = attribute.Value;
                }

                int line = element is System.Xml.IXmlLineInfo info && info.HasLineInfo()
                    ? info.LineNumber
                    : 0;

                elements.Add(new XamlElement(
                    path,
                    line,
                    element.Name.LocalName,
                    attributes.TryGetValue("Name", out string? name) ? name : null,
                    attributes));
            }

            files.Add(new XamlFile(
                path,
                root.Name.LocalName,
                root.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value,
                elements));
        }

        return files;
    }

    /// <summary>
    /// The states a surface's markup shows it can be in.
    /// </summary>
    /// <remarks>
    /// Read from what the markup declares rather than from what the page ought to
    /// have: a page with no error InfoBar has no error state to review, and saying
    /// so is more useful than assuming one exists.
    /// </remarks>
    public static IReadOnlyList<string> States(XamlFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        SortedSet<string> states = new(StringComparer.Ordinal);

        foreach (XamlElement element in file.Elements)
        {
            switch (element.Element)
            {
                case "ProgressBar" or "ProgressRing":
                    states.Add("loading");
                    break;

                case "InfoBar":
                    states.Add(element.Attribute("Severity") switch
                    {
                        "Error" => "error",
                        "Warning" => "warning",
                        "Success" => "success",
                        _ => "informational",
                    });
                    break;

                default:
                    break;
            }

            string? name = element.Name;

            if (name is not null && name.Contains("Empty", StringComparison.OrdinalIgnoreCase))
            {
                states.Add("empty");
            }
        }

        return [.. states];
    }

    /// <summary>Renders an element's position the way a reviewer cites it.</summary>
    public static string Cite(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{element.File}:{element.Line}");
    }
}
