using System.Text;
using System.IO;

namespace AgencyOS.Reviewer.Surface;

/// <summary>
/// Every file the reviewer reads, loaded once.
/// </summary>
/// <remarks>
/// Loaded eagerly and kept in memory because every scanner wants the same few
/// hundred files, and a scanner that re-reads the tree is a scanner whose
/// results depend on when it ran.
/// </remarks>
public sealed class SourceIndex
{
    private SourceIndex(
        string root,
        IReadOnlyDictionary<string, string> files,
        string commit)
    {
        Root = root;
        Files = files;
        Commit = commit;
    }

    /// <summary>Repository root.</summary>
    public string Root { get; }

    /// <summary>Repository-relative path to file contents.</summary>
    public IReadOnlyDictionary<string, string> Files { get; }

    /// <summary>The commit the tree is at, or "unknown".</summary>
    public string Commit { get; }

    /// <summary>Loads the source tree, skipping build output.</summary>
    public static SourceIndex Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        string[] roots = ["src", "tests", "tools", "docs", "scripts", "config", "build"];

        foreach (string area in roots)
        {
            string directory = Path.Combine(root, area);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

                if (relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                    || relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string extension = Path.GetExtension(path);

                if (extension is not (".cs" or ".xaml" or ".fs" or ".csproj" or ".fsproj" or ".json"
                    or ".md" or ".ps1" or ".yaml" or ".yml" or ".tla" or ".cfg"))
                {
                    continue;
                }

                try
                {
                    files[relative] = File.ReadAllText(path, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // A file the reviewer cannot read is reported by its absence
                    // from the map rather than by stopping the scan.
                }
            }
        }

        return new SourceIndex(root, files, ReadCommit(root));
    }

    /// <summary>Files whose relative path matches a prefix and extension.</summary>
    public IEnumerable<KeyValuePair<string, string>> Under(string prefix, string extension) =>
        Files
            .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && x.Key.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Key, StringComparer.Ordinal);

    /// <summary>Repository-relative paths whose contents contain a literal.</summary>
    public IReadOnlyList<string> Mentioning(string literal, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(literal);

        return
        [
            .. Files
                .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && x.Value.Contains(literal, StringComparison.Ordinal))
                .Select(x => x.Key)
                .OrderBy(x => x, StringComparer.Ordinal),
        ];
    }

    private static string ReadCommit(string root)
    {
        try
        {
            string head = Path.Combine(root, ".git", "HEAD");

            if (!File.Exists(head))
            {
                return "unknown";
            }

            string content = File.ReadAllText(head).Trim();

            if (!content.StartsWith("ref:", StringComparison.Ordinal))
            {
                return content;
            }

            string reference = content[4..].Trim();
            string referencePath = Path.Combine(root, ".git", reference.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(referencePath))
            {
                return File.ReadAllText(referencePath).Trim();
            }

            string packed = Path.Combine(root, ".git", "packed-refs");

            if (!File.Exists(packed))
            {
                return "unknown";
            }

            foreach (string line in File.ReadLines(packed))
            {
                if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                {
                    return line.Split(' ')[0];
                }
            }

            return "unknown";
        }
        catch (IOException)
        {
            return "unknown";
        }
    }
}
