using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Unit.Architecture;

// SOURCE-PROOF: Scans committed files for credentials. The claim is about what the
// repository contains, so its files are the evidence.

/// <summary>
/// That no credential has been committed.
/// </summary>
/// <remarks>
/// <para>
/// A deterministic scan rather than a hosted service, because GitHub's own secret
/// scanning is plan-dependent on a private repository and a gate that might not be
/// running is not a gate. This one runs wherever the tests run (M15 §13,
/// ADR-0039).
/// </para>
/// <para>
/// The patterns are the shapes a leaked credential actually takes: a private key
/// block, a provider token with a recognizable prefix, a cloud access key, a
/// connection string carrying a password that is not the development default.
/// Entropy heuristics are deliberately not used — they find base64 test fixtures
/// and Git hashes, and a scanner that cries wolf gets disabled, which is the worst
/// outcome available.
/// </para>
/// <para>
/// False positives are allowed individually and never by weakening a pattern. The
/// one known exception is a test asserting that a diagnostic summary <em>withholds</em>
/// a password; excluding that file by name keeps the rule intact everywhere else.
/// </para>
/// </remarks>
public sealed partial class SecretScanTests
{
    /// <summary>
    /// Files whose contents legitimately look like a secret.
    /// </summary>
    /// <remarks>
    /// Each entry is a decision. Adding one means somebody looked and concluded the
    /// match is a fixture rather than a credential, and the list stays short enough
    /// that a reviewer can check that claim.
    /// </remarks>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["DiagnosticSummaryTests.cs"] =
            "Asserts that a diagnostic summary withholds a password-shaped value. "
                + "The literal is the thing being refused, not a credential.",
    };

    /// <summary>The development database password, which is not a secret.</summary>
    /// <remarks>
    /// It appears in the fallback connection string for a developer's local
    /// PostgreSQL and in CI, where the database is created and destroyed by the
    /// job. Treating it as a finding would mean either a permanent exception or a
    /// scanner nobody trusts.
    /// </remarks>
    private const string DevelopmentPassword = "postgres";

    [Fact]
    public void NoCommittedFileCarriesACredential()
    {
        List<string> findings = [];
        int scanned = 0;

        foreach (string file in Sources())
        {
            scanned++;

            if (Allowed.ContainsKey(Path.GetFileName(file)))
            {
                continue;
            }

            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match match in SecretShape().Matches(text))
            {
                string value = match.Value;

                // A connection string pointing at a throwaway development database
                // is configuration, not a credential.
                if (value.Contains(DevelopmentPassword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // An assignment whose right-hand side is a variable reference or a
                // function call carries no secret: it is code moving a value that
                // came from somewhere else. `$env:PGPASSWORD = $Password` is the
                // shape of handling a credential carefully, not of leaking one.
                if (Indirect().IsMatch(value))
                {
                    continue;
                }

                // Placeholders in documentation and templates.
                if (value.Contains('<', StringComparison.Ordinal)
                    || value.Contains("REPLACE", StringComparison.Ordinal)
                    || value.Contains("${", StringComparison.Ordinal)
                    || value.Contains("your-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                findings.Add($"{Path.GetRelativePath(RepositoryRoot, file)}: {Summarize(value)}");
            }
        }

        // A scanner that stopped finding files would pass for ever while checking
        // nothing, which is the failure mode structural tests are most prone to.
        Assert.True(
            scanned > 300,
            $"Only {scanned} files were scanned. The scan has stopped seeing the repository.");

        Assert.True(
            findings.Count == 0,
            "Possible credentials are committed. Rotate anything real, then either remove it "
                + "or add a reviewed entry to the allow list — never by loosening the pattern:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>Every allow-list entry still refers to a file that exists.</summary>
    /// <remarks>
    /// An exception that outlives the file it excused is an exception nobody
    /// revisits, and the next file with that name inherits it silently.
    /// </remarks>
    [Fact]
    public void EveryAllowedExceptionStillApplies()
    {
        HashSet<string> present = new(
            Sources().Select(Path.GetFileName)!, StringComparer.Ordinal);

        List<string> stale = [.. Allowed.Keys.Where(name => !present.Contains(name))];

        Assert.True(
            stale.Count == 0,
            $"These secret-scan exceptions name files that no longer exist: {string.Join(", ", stale)}");
    }

    /// <summary>Shows enough of a finding to locate it, without reprinting it.</summary>
    private static string Summarize(string value)
    {
        string collapsed = value.ReplaceLineEndings(" ").Trim();

        return collapsed.Length <= 32 ? collapsed : collapsed[..32] + "…";
    }

    // A private key block, a provider token with a known prefix, a cloud access
    // key, or a password assignment that is not obviously a placeholder.
    [GeneratedRegex(
        @"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----"
            + @"|xox[baprs]-[A-Za-z0-9-]{10,}"
            + @"|gh[pousr]_[A-Za-z0-9]{30,}"
            + @"|AKIA[0-9A-Z]{16}"
            + @"|sk-[A-Za-z0-9]{20,}"
            + @"|(?i:password|pwd)\s*=\s*[^\s"";,<>{}]{6,}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SecretShape();

    // A PowerShell or shell variable ($x, ${x}, %x%), an environment lookup, or a
    // Verb-Noun cmdlet call. None of these is a literal.
    [GeneratedRegex(
        @"=\s*(?:[$%]|@?\(|[A-Z][a-z]+-[A-Z]|Environment\.|Configuration\[|builder\.)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex Indirect();

    private static List<string> Sources()
    {
        string[] skipDirectories = ["obj", "bin", ".git", "artifacts", "node_modules"];
        string[] skipExtensions =
            [".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".ico", ".zip", ".jar", ".snk"];

        List<string> files = [];

        foreach (string file in Directory.EnumerateFiles(
            RepositoryRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(RepositoryRoot, file);

            if (relative.Split(Path.DirectorySeparatorChar).Any(
                    segment => skipDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (skipExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
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
