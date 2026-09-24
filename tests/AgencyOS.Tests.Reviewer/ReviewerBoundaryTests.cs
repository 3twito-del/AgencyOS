using System.IO;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

// SOURCE-PROOF: Project references, the reviewer's assembly list and .gitignore
// entries are facts about the repository's files. The boundary asserted is a build-
// structure rule, not runtime behaviour.

/// <summary>
/// The reviewer may read the product. The product may not read the reviewer.
/// </summary>
/// <remarks>
/// <para>
/// A review tool that becomes a dependency of the thing it reviews stops being
/// independent, and starts being something whose findings the build has an
/// interest in. That is a structural property, so it is asserted structurally:
/// by reading the project files, not by convention or by review.
/// </para>
/// <para>
/// The direction is checked in both senses. No product project references the
/// reviewer, and the reviewer references only the three assemblies it needs to
/// read the client's own command registry and activation vocabulary - never
/// Domain, Application, Infrastructure or Api, whose internals a reviewer has no
/// business seeing.
/// </para>
/// </remarks>
public sealed class ReviewerBoundaryTests
{
    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("The repository root could not be located.");
        }
    }

    public static TheoryData<string> ProductProjects
    {
        get
        {
            TheoryData<string> data = [];

            foreach (string project in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories))
            {
                data.Add(Path.GetRelativePath(RepositoryRoot, project).Replace('\\', '/'));
            }

            foreach (string project in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src"), "*.fsproj", SearchOption.AllDirectories))
            {
                data.Add(Path.GetRelativePath(RepositoryRoot, project).Replace('\\', '/'));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ProductProjects))]
    public void NoProductProjectReferencesTheReviewer(string relativePath)
    {
        XDocument project = XDocument.Load(Path.Combine(RepositoryRoot, relativePath));

        IReadOnlyList<string> offending =
        [
            .. project
                .Descendants("ProjectReference")
                .Select(x => x.Attribute("Include")?.Value ?? string.Empty)
                .Where(x => x.Contains("Reviewer", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.True(
            offending.Count == 0,
            relativePath + " references the review harness: " + string.Join("; ", offending)
                + ". The reviewer reads the product; the product must not read the reviewer.");
    }

    [Fact]
    public void TheReviewerReadsOnlyTheClientFacingAssemblies()
    {
        XDocument project = XDocument.Load(
            Path.Combine(RepositoryRoot, "tools", "AgencyOS.Reviewer", "AgencyOS.Reviewer.csproj"));

        string[] permitted =
        [
            "AgencyOS.Client.csproj",
            "AgencyOS.Windows.Platform.csproj",
            "AgencyOS.Contracts.csproj",
        ];

        IReadOnlyList<string> references =
        [
            .. project
                .Descendants("ProjectReference")
                .Select(x => Path.GetFileName(x.Attribute("Include")?.Value ?? string.Empty)),
        ];

        Assert.NotEmpty(references);

        IReadOnlyList<string> unexpected =
            [.. references.Where(x => !permitted.Contains(x, StringComparer.Ordinal))];

        Assert.True(
            unexpected.Count == 0,
            "The reviewer references assemblies it has no business reading: "
                + string.Join("; ", unexpected));
    }

    [Fact]
    public void TheReviewerLivesOutsideTheProductTree()
    {
        Assert.True(
            Directory.Exists(Path.Combine(RepositoryRoot, "tools", "AgencyOS.Reviewer")),
            "The review harness belongs under tools/, outside the product architecture.");

        Assert.False(
            Directory.Exists(Path.Combine(RepositoryRoot, "src", "AgencyOS.Reviewer")),
            "The review harness must not live under src/.");
    }

    /// <summary>
    /// Runtime review artefacts are not committed.
    /// </summary>
    /// <remarks>
    /// Screenshots and automation trees are evidence for one run against one
    /// synthetic tenant. Committing them would put pictures of a database into
    /// the history permanently, which is the wrong default even when the data is
    /// synthetic (review brief section 6).
    /// </remarks>
    [Fact]
    public void ReviewArtefactsAreIgnoredByGit()
    {
        string ignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));

        Assert.Contains("artifacts/", ignore, StringComparison.Ordinal);
    }
}
