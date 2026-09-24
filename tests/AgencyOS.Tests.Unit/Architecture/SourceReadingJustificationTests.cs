using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Unit.Architecture;

// SOURCE-PROOF: Reads every test file in the repository to find the ones that read
// files and to check that each says what it reads and why. The rule is about the
// test source itself, so the test source is the evidence.

/// <summary>
/// That every test which reads a file says whether it is reading the product's source,
/// and if so, why that text is the right evidence.
/// </summary>
/// <remarks>
/// <para>
/// The operational regression gate (<c>18-…</c>) forbids treating the presence of a
/// string in a source file as proof of what an operator receives. Reality Closure
/// found fifty-seven tests doing exactly that under names like "never states" and
/// "is reachable" (F-07).
/// </para>
/// <para>
/// Reading source is still the right instrument for claims <em>about</em> source —
/// that a project references only what its layer allows, that no credential is
/// committed, that a template declares an accessible name. So this does not forbid
/// it. It requires every file that reads a file to declare which kind of reading it
/// does, where a reviewer reads it beside the assertions and can disagree:
/// </para>
/// <list type="bullet">
/// <item><c>// SOURCE-PROOF:</c> — it reads the repository's source or markup, and
/// why that text is authoritative for what it asserts.</item>
/// <item><c>// NOT-SOURCE-READ:</c> — it reads only what the test itself produced: a
/// log it captured, a dump or cache file it wrote.</item>
/// </list>
/// <para>
/// <strong>At the point of reading, not at the point of finding.</strong> The rule
/// keys on the read itself — text, bytes, a stream, XML or XAML — wherever it
/// happens, so a helper that takes an already-resolved path cannot hide one. A shared
/// helper that reads for several callers carries the declaration for all of them.
/// Nothing here lists approved test classes.
/// </para>
/// <para>
/// A declaration in a file that reads nothing is stale and fails. A
/// <c>NOT-SOURCE-READ</c> declaration in a file that names the product's source is
/// misclassified and fails.
/// </para>
/// </remarks>
public sealed partial class SourceReadingJustificationTests
{
    private const string SourceProof = "// SOURCE-PROOF:";

    private const string NotSource = "// NOT-SOURCE-READ:";

    /// <summary>The fewest characters a declaration may have and still say something.</summary>
    private const int ShortestReason = 60;

    /// <summary>What the rule concludes about one file.</summary>
    public enum Verdict
    {
        /// <summary>It reads nothing and declares nothing, or declares what it does.</summary>
        Sound,

        /// <summary>It reads a file and does not say which kind of read.</summary>
        Undeclared,

        /// <summary>It declares a read it does not perform.</summary>
        Stale,

        /// <summary>It declares both kinds, or one too briefly to disagree with.</summary>
        Malformed,

        /// <summary>It calls a read not-source while naming the product's source.</summary>
        Misclassified,
    }

    /// <summary>Every file in the repository's tests is sound under the rule.</summary>
    [Fact]
    public void EveryTestFileIsSoundUnderTheRule()
    {
        List<string> findings = [];

        foreach (string file in TestSources())
        {
            Verdict verdict = Judge(File.ReadAllText(file));

            if (verdict != Verdict.Sound)
            {
                findings.Add($"{Relative(file)}: {verdict}");
            }
        }

        Assert.True(
            findings.Count == 0,
            $"Declare every file read with '{SourceProof}' or '{NotSource}' and a reason of at least "
                + $"{ShortestReason} characters: " + string.Join("; ", findings));
    }

    /// <summary>
    /// The rule recognises the shapes it exists for.
    /// </summary>
    /// <remarks>
    /// Positive and negative controls, so a later edit that makes the rule vacuous
    /// fails here rather than passing quietly.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Controls))]
    public void TheRuleTellsTheShapesApart(string source, Verdict expected)
    {
        Assert.Equal(expected, Judge(source));
    }

    private const string Reason =
        " Reads the markup because which bindings a template declares is a markup fact.";

    private const string Produced =
        " Reads only the log file this test itself captured, never the repository's source.";

    public static TheoryData<string, Verdict> Controls => new()
    {
        // A direct read of the repository's source.
        { "string page = File.ReadAllText(Path.Combine(RepositoryRoot, \"src\", \"a.cs\"));", Verdict.Undeclared },
        { SourceProof + Reason + "\nstring page = File.ReadAllText(Path.Combine(RepositoryRoot, \"src\"));", Verdict.Sound },

        // A read through a helper handed an already-resolved path.
        { "static string Read(string path) => File.ReadAllText(path);", Verdict.Undeclared },
        { "static byte[] Read(string path) => File.ReadAllBytes(path);", Verdict.Undeclared },
        { "using var reader = new StreamReader(path);", Verdict.Undeclared },

        // XAML and XML loading.
        { "XElement page = XElement.Load(relativePath);", Verdict.Undeclared },
        { "XDocument page = XDocument.Load(path);", Verdict.Undeclared },

        // A log or artefact the test produced is declared as such, and is not a
        // source read — unless it also names the product's source.
        { NotSource + Produced + "\nstring kept = File.ReadAllText(_path);", Verdict.Sound },
        { NotSource + Produced + "\nstring kept = File.ReadAllText(Path.Combine(root, \"src\", \"a.cs\"));", Verdict.Misclassified },

        // A declaration with nothing to justify, and declarations that say too little.
        { SourceProof + Reason + "\nAssert.Equal(1, 1);", Verdict.Stale },
        { SourceProof + " Because.\nstring page = File.ReadAllText(path);", Verdict.Malformed },
        { SourceProof + Reason + "\n" + NotSource + Produced + "\nFile.ReadAllText(path);", Verdict.Malformed },

        // No read, no declaration.
        { "Assert.Equal(1, 1);", Verdict.Sound },
    };

    /// <summary>The scan sees the real tests.</summary>
    [Fact]
    public void TheScanSeesTheRealTests()
    {
        List<string> files = [.. TestSources().Select(Relative)];

        Assert.Contains(files, x => x.EndsWith("SourceReadingJustificationTests.cs", StringComparison.Ordinal));
        Assert.True(files.Count > 100, $"Only {files.Count} test files were found.");
    }

    /// <summary>What the rule concludes about one file's text.</summary>
    public static Verdict Judge(string text)
    {
        bool reads = Reads().IsMatch(text);
        string? source = Declaration(text, SourceProof);
        string? notSource = Declaration(text, NotSource);

        if (source is not null && notSource is not null)
        {
            return Verdict.Malformed;
        }

        string? declared = source ?? notSource;

        if (declared is null)
        {
            return reads ? Verdict.Undeclared : Verdict.Sound;
        }

        if (!reads)
        {
            return Verdict.Stale;
        }

        if (declared.Length < ShortestReason)
        {
            return Verdict.Malformed;
        }

        if (notSource is not null && NamesProductSource().IsMatch(text))
        {
            return Verdict.Misclassified;
        }

        return Verdict.Sound;
    }

    /// <summary>A declaration's reason, joined across its comment lines.</summary>
    private static string? Declaration(string text, string marker)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (!line.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            List<string> parts = [line[marker.Length..].Trim()];

            for (int j = i + 1; j < lines.Length; j++)
            {
                string next = lines[j].Trim();

                if (!next.StartsWith("//", StringComparison.Ordinal)
                    || next.StartsWith("///", StringComparison.Ordinal)
                    || next.StartsWith(SourceProof, StringComparison.Ordinal)
                    || next.StartsWith(NotSource, StringComparison.Ordinal))
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

    /// <summary>Every way a test can read a file's contents.</summary>
    [GeneratedRegex(@"File\.ReadAll(Text|Lines|Bytes)(Async)?|File\.ReadLines|File\.Open(Read|Text)|new\s+(StreamReader|FileStream)|X(Element|Document)\.Load")]
    private static partial Regex Reads();

    /// <summary>A path or helper that points at the product's own source.</summary>
    [GeneratedRegex(@"""src""|XamlFiles|\.xaml""|\.cs""")]
    private static partial Regex NamesProductSource();

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
