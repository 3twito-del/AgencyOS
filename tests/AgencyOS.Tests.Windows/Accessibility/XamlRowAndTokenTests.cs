using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

// SOURCE-PROOF: Asserts that every row template declares an accessible name and
// routes domain tokens through a converter. Both are declarations in markup.

/// <summary>
/// The two markup rules Audit 001 asked for and the suite did not have.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XamlAccessibilityTests"/> deliberately skips everything inside an
/// item template, because a control in a row takes its name from the row's data
/// rather than from the markup. That exemption was doing more work than it was
/// meant to: it also excused the template's own root, which is the element whose
/// name the row announces. 115 rows across twelve surfaces read their entire
/// record aloud while 521 Windows tests passed (<c>AOS-R001-003</c>).
/// </para>
/// <para>
/// The second rule is the one the audit proposed for <c>AOS-R001-012</c>: a list
/// row may not show a domain token raw while the filter above it spells the same
/// value out in words.
/// </para>
/// </remarks>
public sealed class XamlRowAndTokenTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Properties whose values are domain tokens rather than prose.
    /// </summary>
    /// <remarks>
    /// Named rather than inferred. The client does not reference the domain
    /// assembly, so the test cannot ask a type whether a property is an enum, and
    /// a list that has to be maintained by hand is better than a rule that
    /// silently stops applying.
    /// </remarks>
    private static readonly string[] Tokens =
    [
        "Status", "Stage", "State", "Kind", "Type", "Direction",
        "Outcome", "Severity", "Discipline", "Priority",
    ];

    /// <summary>
    /// Every row says what it shows.
    /// </summary>
    /// <remarks>
    /// A <c>DataTemplate</c> root with no <c>AutomationProperties.Name</c> falls
    /// back to <c>ToString()</c> on the bound object. For a C# record that prints
    /// every property it has, identifiers included.
    /// </remarks>
    [Theory]
    [MemberData(nameof(XamlAccessibilityTests.XamlFiles), MemberType = typeof(XamlAccessibilityTests))]
    public void EveryRowTemplateSaysWhatItShows(string relativePath)
    {
        List<string> silent = [];

        foreach (XElement template in Load(relativePath)
            .DescendantsAndSelf()
            .Where(x => x.Name.LocalName == "DataTemplate"))
        {
            XElement? root = template.Elements().FirstOrDefault();

            if (root is null)
            {
                continue;
            }

            if (root.Attribute("AutomationProperties.Name") is null)
            {
                silent.Add(
                    $"<{root.Name.LocalName}> in template "
                        + $"'{template.Attribute(Xaml + "Key")?.Value ?? "(inline)"}'");
            }
        }

        Assert.True(
            silent.Count == 0,
            $"{relativePath} has rows that announce their whole record rather than "
                + $"what they show: {string.Join("; ", silent)}");
    }

    /// <summary>
    /// A domain token reaches the screen as words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter above a list offers "Talent employment". The row two inches
    /// below it read "TalentEmployment". They are the same value, and an operator
    /// cannot match what they picked to what they see.
    /// </para>
    /// <para>
    /// The rule is about the converter, not about the words: nothing here renames
    /// a domain member, and <c>Status</c> stays distinct from <c>Stage</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(XamlAccessibilityTests.XamlFiles), MemberType = typeof(XamlAccessibilityTests))]
    public void EveryDomainTokenIsShownAsWords(string relativePath)
    {
        List<string> raw = [];

        foreach (XElement text in Load(relativePath)
            .DescendantsAndSelf()
            .Where(x => x.Name.LocalName == "TextBlock"))
        {
            string? binding = text.Attribute("Text")?.Value;

            if (binding is null || !binding.StartsWith('{'))
            {
                continue;
            }

            string property = PropertyPath(binding);

            if (property.Length == 0
                || !Tokens.Contains(property, StringComparer.Ordinal)
                || binding.Contains("Converter=", StringComparison.Ordinal))
            {
                continue;
            }

            raw.Add(binding);
        }

        Assert.True(
            raw.Count == 0,
            $"{relativePath} shows domain tokens the way the code spells them rather "
                + $"than the way the filters do: {string.Join("; ", raw)}");
    }

    /// <summary>
    /// The last segment of a binding's property path.
    /// </summary>
    /// <remarks>
    /// Both binding syntaxes, because the client uses both: <c>x:Bind</c> where the
    /// page has a typed view model and <c>Binding</c> inside a template.
    /// </remarks>
    private static string PropertyPath(string binding)
    {
        string inner = binding.Trim('{', '}').Trim();

        foreach (string prefix in (string[])["x:Bind", "Binding"])
        {
            if (!inner.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string rest = inner[prefix.Length..].Trim();
            int end = rest.IndexOf(',', StringComparison.Ordinal);
            string path = (end < 0 ? rest : rest[..end]).Trim();
            int dot = path.LastIndexOf('.');

            return dot < 0 ? path : path[(dot + 1)..];
        }

        return string.Empty;
    }

    private static XElement Load(string relativePath) =>
        XElement.Load(Path.Combine(RepositoryRoot, relativePath));

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
