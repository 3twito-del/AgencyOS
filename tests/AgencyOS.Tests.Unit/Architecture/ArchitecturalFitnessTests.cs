using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Unit.Architecture;

// SOURCE-PROOF: Layer references and naming rules are properties of the project
// files and the source tree, so the monolith's shape is asserted where it is
// defined.

/// <summary>
/// The shape of the monolith, asserted rather than described.
/// </summary>
/// <remarks>
/// <para>
/// M14 concluded that AgencyOS should stay a modular monolith, and that conclusion
/// is only worth anything while the modularity is real. A layering rule that lives
/// in a document degrades one convenient reference at a time, each of which looks
/// harmless in the diff that introduces it (ADR-0037).
/// </para>
/// <para>
/// These are the rules whose violation would be invisible in behaviour: the build
/// still works, the tests still pass, and what has been lost is the property that
/// made "keep it in one process" the right answer.
/// </para>
/// </remarks>
public sealed partial class ArchitecturalFitnessTests
{
    /// <summary>
    /// Each project may reference only the layers beneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dependency direction is the whole of what "modular" means here. Domain
    /// knows nothing about how it is stored or served. Application knows the domain
    /// and not the database. Infrastructure implements what Application declares.
    /// Only the host knows all of them.
    /// </para>
    /// <para>
    /// A single reference the wrong way — Application reaching for a repository
    /// implementation, Domain reaching for EF — collapses a boundary that cost
    /// nothing to maintain and is very expensive to restore.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("AgencyOS.Contracts", new string[0])]
    [InlineData("AgencyOS.Domain", new[] { "AgencyOS.Deals.Rules", "AgencyOS.Finance.Rules" })]
    [InlineData("AgencyOS.Application", new[] { "AgencyOS.Domain" })]
    [InlineData("AgencyOS.Infrastructure", new[] { "AgencyOS.Application", "AgencyOS.Domain" })]
    [InlineData(
        "AgencyOS.Api",
        new[] { "AgencyOS.Application", "AgencyOS.Contracts", "AgencyOS.Infrastructure" })]
    public void EachProjectReferencesOnlyWhatItsLayerAllows(string project, string[] permitted)
    {
        HashSet<string> actual = ReferencesOf(project);
        HashSet<string> allowed = new(permitted, StringComparer.Ordinal);

        List<string> unexpected = [.. actual.Except(allowed).Order()];

        Assert.True(
            unexpected.Count == 0,
            $"{project} references {string.Join(", ", unexpected)}, which its layer "
                + "does not permit. A reference the wrong way collapses the boundary "
                + "that makes the monolith modular.");
    }

    /// <summary>
    /// The domain does not know it is stored in anything.
    /// </summary>
    /// <remarks>
    /// Stated separately from the project graph because it is the rule most likely
    /// to be broken by an attribute rather than a reference — one
    /// <c>[Column]</c> or <c>[Table]</c> and persistence has opinions inside the
    /// aggregate.
    /// </remarks>
    [Fact]
    public void TheDomainNamesNoPersistenceTechnology()
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "System.Data.Common",
            "System.Data.SqlClient",
        ];

        List<string> offenders = [];

        foreach (string file in SourcesOf("AgencyOS.Domain"))
        {
            string text = File.ReadAllText(file);

            offenders.AddRange(forbidden
                .Where(f => text.Contains(f, StringComparison.Ordinal))
                .Select(f => $"{Path.GetFileName(file)}: {f}"));
        }

        Assert.True(
            offenders.Count == 0,
            $"The domain names a persistence technology: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Every read that takes a page size clamps it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule behind §25: a caller must not be able to ask for a whole tenant.
    /// It holds everywhere today — ten query services at 200, search at 50, sync at
    /// 500 — and this is what stops the eleventh from being written without one.
    /// </para>
    /// <para>
    /// Checked over source rather than by calling every endpoint, because the point
    /// is to catch the service that is added and never given a test, which is
    /// exactly the one an endpoint-driven check would also miss.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPagedReadServiceClampsItsPageSize()
    {
        List<string> unbounded = [];
        int examined = 0;

        foreach (string file in SourcesOf("AgencyOS.Application"))
        {
            string name = Path.GetFileName(file);

            if (!name.EndsWith("QueryService.cs", StringComparison.Ordinal)
                && name is not ("SearchService.cs" or "SyncService.cs"))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            // Only services that actually accept a page size are in scope.
            if (!PageSizeParameter().IsMatch(text))
            {
                continue;
            }

            examined++;

            bool declaresCeiling = text.Contains("Maximum", StringComparison.Ordinal);
            bool clamps = text.Contains("Math.Clamp", StringComparison.Ordinal);

            if (!declaresCeiling || !clamps)
            {
                unbounded.Add(
                    $"{name} (ceiling: {declaresCeiling}, clamp: {clamps})");
            }
        }

        // A pattern that stopped matching would make this pass while checking
        // nothing, which is the failure mode a structural test is most prone to.
        Assert.True(
            examined >= 10,
            $"Only {examined} paged read services were examined. The scan has stopped "
                + "finding them, so this test is no longer checking anything.");

        Assert.True(
            unbounded.Count == 0,
            "These read services accept a page size without clamping it, so a caller "
                + $"can ask for the whole tenant: {string.Join("; ", unbounded)}");
    }

    /// <summary>The scan is looking at real source.</summary>
    /// <remarks>
    /// A structural suite whose file discovery silently found nothing would pass
    /// for ever and prove nothing.
    /// </remarks>
    [Fact]
    public void TheScanSeesTheRealSource()
    {
        Assert.True(SourcesOf("AgencyOS.Domain").Count > 50);
        Assert.True(SourcesOf("AgencyOS.Application").Count > 50);
    }

    // ------------------------------------------------------------- helpers

    // Nullable or not: search takes `int take = DefaultTake`, and a ceiling
    // matters just as much when the caller may omit the value.
    [GeneratedRegex(@"int\??\s+(limit|take|pageSize)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PageSizeParameter();

    private static HashSet<string> ReferencesOf(string project)
    {
        string csproj = Path.Combine(RepositoryRoot, "src", project, project + ".csproj");
        string text = File.ReadAllText(csproj);

        HashSet<string> references = new(StringComparer.Ordinal);

        foreach (Match match in ProjectReference().Matches(text))
        {
            string path = match.Groups["path"].Value;

            references.Add(Path.GetFileNameWithoutExtension(path.Replace('\\', '/')));
        }

        return references;
    }

    [GeneratedRegex(
        @"ProjectReference\s+Include=""(?<path>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectReference();

    private static List<string> SourcesOf(string project)
    {
        List<string> files = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", project), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(file);
        }

        return files;
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
