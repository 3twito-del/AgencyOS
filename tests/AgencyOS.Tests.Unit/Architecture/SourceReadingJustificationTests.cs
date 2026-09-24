using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Unit.Architecture;

// SOURCE-PROOF: Reads every test file in the repository to find the ones that read
// repository text and to check that each says why. The rule is about the test
// source itself, so the test source is the evidence.

/// <summary>
/// That every test which reads the repository's own text says why that text is
/// the right evidence.
/// </summary>
/// <remarks>
/// <para>
/// The operational regression gate (<c>18-…</c>) forbids treating the presence of a
/// string in a source file as proof of what an operator receives. Reality Closure
/// found fifty-seven tests doing exactly that under names like "never states" and
/// "is reachable" (F-07), and a string in a file is evidence that a binding exists,
/// never that its meaning arrives.
/// </para>
/// <para>
/// Reading source is still the right instrument for claims <em>about</em> source —
/// that a project references only what its layer allows, that no credential is
/// committed, that a template declares an accessible name. So this does not forbid
/// it. It requires the test to say, in a <c>// SOURCE-PROOF:</c> declaration, why
/// the text it reads is authoritative for what it asserts, where a reviewer reads
/// it beside the assertions and can disagree.
/// </para>
/// <para>
/// <strong>Mechanical, not a list of approved names.</strong> A file needs the
/// declaration when it both locates the repository and reads a file's text. A file
/// that declares one and no longer reads repository text fails too, so the
/// declarations cannot outlive what they justify.
/// </para>
/// </remarks>
public sealed partial class SourceReadingJustificationTests
{
    private const string Marker = "// SOURCE-PROOF:";

    /// <summary>The fewest characters a declaration may have and still say something.</summary>
    private const int ShortestReason = 60;

    /// <summary>Every test file that reads repository text declares why.</summary>
    [Fact]
    public void EveryTestReadingRepositoryTextDeclaresWhy()
    {
        List<string> undeclared = [];

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);

            if (ReadsRepositoryText(text) && Reason(text) is null)
            {
                undeclared.Add(Relative(file));
            }
        }

        Assert.True(
            undeclared.Count == 0,
            "These tests read the repository's text without saying why that text is the "
                + $"right evidence for what they assert. Add a '{Marker}' declaration: "
                + string.Join(", ", undeclared));
    }

    /// <summary>Every declaration says enough to be disagreed with.</summary>
    [Fact]
    public void EveryDeclarationGivesAReason()
    {
        List<string> thin = [];

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);

            if (text.Contains(Marker, StringComparison.Ordinal)
                && (Reason(text)?.Length ?? 0) < ShortestReason)
            {
                thin.Add(Relative(file));
            }
        }

        Assert.True(
            thin.Count == 0,
            $"These declarations are shorter than {ShortestReason} characters: " + string.Join(", ", thin));
    }

    /// <summary>No declaration outlives the reading it justified.</summary>
    [Fact]
    public void NoDeclarationStandsInAFileThatReadsNothing()
    {
        List<string> stale = [];

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);

            if (text.Contains(Marker, StringComparison.Ordinal) && !ReadsRepositoryText(text))
            {
                stale.Add(Relative(file));
            }
        }

        Assert.True(stale.Count == 0, "Declarations with nothing to justify: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The detector recognises the shapes it exists for.
    /// </summary>
    /// <remarks>
    /// Positive and negative controls, so a later edit that makes the rule vacuous
    /// fails here rather than passing quietly.
    /// </remarks>
    [Theory]
    [InlineData("var root = Find(\"AgencyOS.sln\"); File.ReadAllText(Path.Combine(root, \"src\", \"a.cs\"));", true)]
    [InlineData("string page = File.ReadAllText(Path.Combine(RepositoryRoot, \"src\"));", true)]
    [InlineData("XElement.Load(Path.Combine(RepositoryRoot, relativePath));", true)]
    [InlineData("byte[] dump = await File.ReadAllBytesAsync(path); Find(\"AgencyOS.sln\");", false)]
    [InlineData("string captured = File.ReadAllText(logPath);", false)]
    [InlineData("Assert.Equal(1, 1);", false)]
    public void TheDetectorTellsTheShapesApart(string source, bool reads)
    {
        Assert.Equal(reads, ReadsRepositoryText(source));
    }

    /// <summary>The scan sees the real tests.</summary>
    [Fact]
    public void TheScanSeesTheRealTests()
    {
        List<string> files = [.. TestSources().Select(Relative)];

        Assert.Contains(files, x => x.EndsWith("SourceReadingJustificationTests.cs", StringComparison.Ordinal));
        Assert.True(files.Count > 100, $"Only {files.Count} test files were found.");
    }

    /// <summary>
    /// Whether a file reads the repository's own text.
    /// </summary>
    /// <remarks>
    /// Two conditions, both required: it locates the repository, and it reads a
    /// file's text. Reading bytes a test produced, or a log it captured, is neither.
    /// </remarks>
    private static bool ReadsRepositoryText(string text) =>
        LocatesRepository().IsMatch(text) && ReadsText().IsMatch(text);

    /// <summary>The declaration's reason, joined across its comment lines.</summary>
    private static string? Reason(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (!line.StartsWith(Marker, StringComparison.Ordinal))
            {
                continue;
            }

            List<string> parts = [line[Marker.Length..].Trim()];

            for (int j = i + 1; j < lines.Length; j++)
            {
                string next = lines[j].Trim();

                if (!next.StartsWith("//", StringComparison.Ordinal) || next.StartsWith("///", StringComparison.Ordinal))
                {
                    break;
                }

                parts.Add(next[2..].Trim());
            }

            return string.Join(' ', parts).Trim();
        }

        return null;
    }

    private static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) =>
        Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');

    [GeneratedRegex(@"AgencyOS\.sln|RepositoryRoot|XamlFiles")]
    private static partial Regex LocatesRepository();

    [GeneratedRegex(@"File\.ReadAllText|File\.ReadAllLines|File\.ReadLines|XElement\.Load|XDocument\.Load")]
    private static partial Regex ReadsText();

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
