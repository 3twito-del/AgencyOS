using System.Globalization;
using AgencyOS.Reviewer.Surface;
using System.IO;

namespace AgencyOS.Reviewer.Style;

/// <summary>One literal value written in markup.</summary>
/// <param name="Property">The property it was written on.</param>
/// <param name="Value">The literal, exactly as written.</param>
/// <param name="Occurrences">How many places write it.</param>
/// <param name="Sites">Where, as file:line.</param>
public sealed record StyleValue(
    string Property,
    string Value,
    int Occurrences,
    IReadOnlyList<string> Sites);

/// <summary>What the markup does with one visual concept.</summary>
/// <param name="Property">The property inventoried.</param>
/// <param name="DistinctValues">How many different literals appear.</param>
/// <param name="TotalOccurrences">How many times any literal appears.</param>
/// <param name="Values">Each literal, most common first.</param>
public sealed record StyleDimension(
    string Property,
    int DistinctValues,
    int TotalOccurrences,
    IReadOnlyList<StyleValue> Values);

/// <summary>A repeated concept rendered inconsistently.</summary>
/// <param name="Concept">What the outlier is an outlier of.</param>
/// <param name="Dominant">What most sites do.</param>
/// <param name="DominantCount">How many do it.</param>
/// <param name="Outliers">The sites that do something else.</param>
public sealed record StyleOutlier(
    string Concept,
    string Dominant,
    int DominantCount,
    IReadOnlyList<string> Outliers);

/// <summary>The whole style inventory.</summary>
/// <param name="Dimensions">One entry per inventoried property.</param>
/// <param name="Outliers">Repeated concepts a small minority renders differently.</param>
public sealed record StyleReport(
    IReadOnlyList<StyleDimension> Dimensions,
    IReadOnlyList<StyleOutlier> Outliers);

/// <summary>
/// Inventories what the markup actually does, so outliers can be seen.
/// </summary>
/// <remarks>
/// <para>
/// The purpose is not to ban literal values. AgencyOS writes <c>Padding="16"</c>
/// and <c>Spacing="8"</c> deliberately and consistently, and a rule against
/// literals would flag all of it. The purpose is the opposite: to find the place
/// where one surface writes something the other twenty-three do not, because that
/// is where the design language has drifted without anybody deciding to change it.
/// </para>
/// <para>
/// An outlier is reported only where a dominant value exists - at least four
/// times as common as the exception. Below that there is no established practice
/// to be an exception to, and reporting it would be taste dressed as evidence.
/// </para>
/// </remarks>
public static class XamlStyleInventory
{
    /// <summary>Properties whose literal values are worth counting.</summary>
    private static readonly string[] Inventoried =
    [
        "Margin", "Padding", "Spacing", "ColumnSpacing", "RowSpacing",
        "FontSize", "FontWeight", "FontFamily",
        "Width", "MinWidth", "MaxWidth", "Height", "MinHeight", "MaxHeight",
        "CornerRadius", "BorderThickness", "Opacity",
        "Foreground", "Background", "BorderBrush",
        "Style", "HorizontalAlignment", "VerticalAlignment",
    ];

    /// <summary>Counts every literal and finds the exceptions.</summary>
    public static StyleReport Build(IReadOnlyList<XamlFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        Dictionary<string, Dictionary<string, List<string>>> counts = new(StringComparer.Ordinal);

        foreach (string property in Inventoried)
        {
            counts[property] = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        foreach (XamlFile file in files)
        {
            foreach (XamlElement element in file.Elements)
            {
                foreach (string property in Inventoried)
                {
                    if (element.Attribute(property) is not { } value)
                    {
                        continue;
                    }

                    Dictionary<string, List<string>> byValue = counts[property];

                    if (!byValue.TryGetValue(value, out List<string>? sites))
                    {
                        sites = [];
                        byValue[value] = sites;
                    }

                    sites.Add(XamlScanner.Cite(element));
                }
            }
        }

        List<StyleDimension> dimensions = [];

        foreach (string property in Inventoried)
        {
            Dictionary<string, List<string>> byValue = counts[property];

            if (byValue.Count == 0)
            {
                continue;
            }

            List<StyleValue> values =
            [
                .. byValue
                    .Select(x => new StyleValue(property, x.Key, x.Value.Count, [.. x.Value.Take(12)]))
                    .OrderByDescending(x => x.Occurrences)
                    .ThenBy(x => x.Value, StringComparer.Ordinal),
            ];

            dimensions.Add(new StyleDimension(
                property,
                values.Count,
                values.Sum(x => x.Occurrences),
                values));
        }

        return new StyleReport(dimensions, [.. FindOutliers(files), .. FindValueOutliers(dimensions)]);
    }

    /// <summary>
    /// Places where one element of a kind is styled unlike all the others.
    /// </summary>
    /// <remarks>
    /// The concrete question from the brief: if twenty-four detail headers use one
    /// shared style and two use inline values, say which two.
    /// </remarks>
    private static IEnumerable<StyleOutlier> FindOutliers(IReadOnlyList<XamlFile> files)
    {
        // Page titles: the first TextBlock in a page, which is the surface's name.
        List<(string Site, string Style)> titles = [];

        foreach (XamlFile file in files)
        {
            if (file.RootElement != "Page")
            {
                continue;
            }

            XamlElement? title = file.Elements
                .FirstOrDefault(x => x.Element == "TextBlock" && x.Attribute("Text") is not null);

            if (title is null)
            {
                continue;
            }

            titles.Add((
                XamlScanner.Cite(title),
                title.Attribute("Style") ?? "«inline: FontSize=" + (title.Attribute("FontSize") ?? "unset")
                    + " FontWeight=" + (title.Attribute("FontWeight") ?? "unset") + "»"));
        }

        foreach (StyleOutlier outlier in Dominance("page title styling", titles))
        {
            yield return outlier;
        }

        // Dialogs: primary button text, default button, and whether Escape closes.
        List<(string Site, string Style)> primaryButtons = [];
        List<(string Site, string Style)> defaultButtons = [];

        foreach (XamlFile file in files)
        {
            if (file.RootElement != "ContentDialog")
            {
                continue;
            }

            XamlElement root = file.Elements.Count > 0 ? file.Elements[0] : null!;

            XamlElement? dialog = file.Elements.FirstOrDefault(x => x.Element == "ContentDialog") ?? root;

            if (dialog is null)
            {
                continue;
            }

            string site = file.RelativePath;

            primaryButtons.Add((site, dialog.Attribute("PrimaryButtonText") ?? "«none»"));
            defaultButtons.Add((site, dialog.Attribute("DefaultButton") ?? "«unset»"));
        }

        foreach (StyleOutlier outlier in Dominance("dialog default button", defaultButtons))
        {
            yield return outlier;
        }

        // Close button text across dialogs: the word used to abandon a dialog.
        List<(string Site, string Style)> closeText = [];

        foreach (XamlFile file in files)
        {
            if (file.RootElement != "ContentDialog")
            {
                continue;
            }

            XamlElement? dialog = file.Elements.FirstOrDefault(x => x.Element == "ContentDialog");

            if (dialog?.Attribute("CloseButtonText") is { } text)
            {
                closeText.Add((file.RelativePath, text));
            }
        }

        foreach (StyleOutlier outlier in Dominance("dialog close-button wording", closeText))
        {
            yield return outlier;
        }

        // Page root padding: the gutter every workspace shares, or does not.
        List<(string Site, string Style)> gutters = [];

        foreach (XamlFile file in files)
        {
            if (file.RootElement != "Page")
            {
                continue;
            }

            XamlElement? container = file.Elements.FirstOrDefault(x =>
                x.Element is "Grid" or "StackPanel" or "ScrollViewer");

            if (container is not null)
            {
                gutters.Add((file.RelativePath, container.Attribute("Padding") ?? "«unset»"));
            }
        }

        foreach (StyleOutlier outlier in Dominance("page root padding", gutters))
        {
            yield return outlier;
        }
    }

    /// <summary>
    /// Literal values used once where a near neighbour is used many times.
    /// </summary>
    /// <remarks>
    /// Restricted to the numeric rhythm properties, where a single unexplained
    /// value is a real inconsistency rather than a legitimate one-off. A width of
    /// 460 used once is a layout decision; a spacing of 7 used once where
    /// everything else uses 8 is drift.
    /// </remarks>
    private static IEnumerable<StyleOutlier> FindValueOutliers(IReadOnlyList<StyleDimension> dimensions)
    {
        foreach (StyleDimension dimension in dimensions)
        {
            if (dimension.Property is not ("Spacing" or "ColumnSpacing" or "RowSpacing" or "Padding" or "FontSize"))
            {
                continue;
            }

            List<StyleValue> singletons = [.. dimension.Values.Where(x => x.Occurrences == 1)];

            if (singletons.Count == 0 || dimension.Values.Count == 0)
            {
                continue;
            }

            StyleValue dominant = dimension.Values[0];

            if (dominant.Occurrences < 4 * Math.Max(1, singletons.Count))
            {
                continue;
            }

            yield return new StyleOutlier(
                dimension.Property + " used once",
                dominant.Value,
                dominant.Occurrences,
                [
                    .. singletons.Select(x =>
                        x.Value + " at " + string.Join(", ", x.Sites)),
                ]);
        }
    }

    private static IEnumerable<StyleOutlier> Dominance(
        string concept,
        IReadOnlyList<(string Site, string Style)> observations)
    {
        if (observations.Count < 5)
        {
            yield break;
        }

        IGrouping<string, (string Site, string Style)>[] groups =
        [
            .. observations
                .GroupBy(x => x.Style, StringComparer.Ordinal)
                .OrderByDescending(x => x.Count()),
        ];

        if (groups.Length < 2)
        {
            yield break;
        }

        int dominantCount = groups[0].Count();
        int exceptions = observations.Count - dominantCount;

        // A practice is established only when the exception is rare. Four to one
        // is the line; below it there are simply two ways of doing something and
        // the reviewer should say so in prose rather than as an outlier.
        if (exceptions == 0 || dominantCount < 4 * exceptions)
        {
            yield break;
        }

        yield return new StyleOutlier(
            concept,
            groups[0].Key,
            dominantCount,
            [
                .. groups
                    .Skip(1)
                    .SelectMany(g => g.Select(x =>
                        string.Create(CultureInfo.InvariantCulture, $"{x.Site} uses {x.Style}"))),
            ]);
    }
}
