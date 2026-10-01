using System.Security.Cryptography;

namespace AgencyOS.Tests.Canonical;

// NOT-SOURCE-READ: Hashes and reads files in throwaway git repositories that the tests
// built themselves, including their .git directories, to prove a run changed none of
// them or to show a working-tree file's bytes. It reads nothing from the AgencyOS repository.

/// <summary>A byte-exact fingerprint of a directory tree.</summary>
internal static class FixtureSnapshot
{
    public static SortedDictionary<string, string> Take(string root)
    {
        SortedDictionary<string, string> files = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(root, file)] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return files;
    }

    /// <summary>A working-tree file's exact bytes, which line-end conversion may have altered.</summary>
    public static byte[] Bytes(string path) => File.ReadAllBytes(path);
}
