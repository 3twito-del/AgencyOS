using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That an explanation written under a field can be reached from that field.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-011</c>. No dialog declared <c>AutomationProperties.DescribedBy</c>
/// anywhere — 0 of 63 — so a message about a control could be seen next to it and
/// not found from it. Labelling was already thorough; the missing link was the
/// second one.
/// </para>
/// <para>
/// Only part of that is settled. A hint sitting under one field describes that
/// field whatever else is decided, so those associations are declared here. The
/// manifestation the audit actually drove — a server refusal, which today appears
/// at page level after the dialog has closed — has nothing inside the dialog to
/// point at, and stays open under <c>AOS-R002-010</c>.
/// </para>
/// </remarks>
public sealed class FieldDescriptionTests
{
    /// <summary>The hints that belong to exactly one field say so.</summary>
    [Theory]
    [InlineData("AddOpportunityTargetDialog", "ContactBox", "ContactHint")]
    [InlineData("AddPackageElementDialog", "TargetBox", "TargetHint")]
    [InlineData("CreateOpportunityDialog", "SubjectBox", "SubjectHint")]
    [InlineData("LinkResearchItemDialog", "ItemBox", "ItemHint")]
    [InlineData("RecordSubmissionDialog", "MaterialBox", "SnapshotHint")]
    public void AFieldPointsAtTheHintBeneathIt(string dialog, string field, string hint)
    {
        string text = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Dialogs", dialog + ".xaml.cs"));

        Assert.Contains(
            $"AutomationProperties.GetDescribedBy({field}).Add({hint});",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>Both ends of every association exist in the markup.</summary>
    /// <remarks>
    /// An association naming a control that markup no longer has would throw when
    /// the dialog is constructed, and dialog construction is not reachable from a
    /// test. Reading both files is what catches a rename.
    /// </remarks>
    [Fact]
    public void EveryAssociationNamesTwoControlsThatExist()
    {
        List<string> broken = [];

        foreach (string code in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"),
            "*.xaml.cs",
            SearchOption.AllDirectories))
        {
            string markup = code[..^3];

            if (!File.Exists(markup))
            {
                continue;
            }

            XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

            HashSet<string> declared = XElement.Load(markup)
                .Descendants()
                .Select(x => x.Attribute(name)?.Value)
                .Where(x => x is { Length: > 0 })
                .Select(x => x!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (Match found in Regex.Matches(
                File.ReadAllText(code),
                @"GetDescribedBy\((?<field>\w+)\)\.Add\((?<hint>\w+)\)"))
            {
                foreach (string part in new[] { "field", "hint" })
                {
                    string control = found.Groups[part].Value;

                    if (!declared.Contains(control))
                    {
                        broken.Add($"{Path.GetFileName(code)}: {control}");
                    }
                }
            }
        }

        Assert.True(
            broken.Count == 0,
            "An association names a control the markup does not declare: "
                + string.Join("; ", broken));
    }

    /// <summary>
    /// The pattern the product had none of now exists.
    /// </summary>
    /// <remarks>
    /// The finding's root is that there was no shared way to do this at all. If
    /// the last association were deleted, the product would be back where the
    /// audit found it, and that should fail rather than pass quietly.
    /// </remarks>
    [Fact]
    public void TheProductDeclaresTheAssociationSomewhere()
    {
        int declared = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"),
                "*.xaml.cs",
                SearchOption.AllDirectories)
            .Sum(x => Regex.Matches(File.ReadAllText(x), @"GetDescribedBy\(\w+\)\.Add\(").Count);

        Assert.True(declared >= 5, $"Only {declared} field associations are declared.");
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
