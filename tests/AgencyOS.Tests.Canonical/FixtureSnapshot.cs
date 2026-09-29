using System.Security.Cryptography;

namespace AgencyOS.Tests.Canonical;

// NOT-SOURCE-READ: Hashes every file in a throwaway git repository that the test built
// itself, including its .git directory, to prove the detector changed none of it. It
// reads nothing from the AgencyOS repository.

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
}
