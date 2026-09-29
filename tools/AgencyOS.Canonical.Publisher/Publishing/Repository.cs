using System.Security.Cryptography;
using System.Text;
using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Ledger;

namespace AgencyOS.Canonical.Publisher.Publishing;

/// <summary>The paths the contract fixes.</summary>
internal static class CanonicalPaths
{
    public const string CurrentState = "docs/control-room/CURRENT-STATE.md";
    public const string Deltas = "docs/control-room/CANONICAL-DELTAS.md";
    public const string Decisions = "docs/control-room/DECISIONS.md";
    public const string PendingRoot = "docs/control-room/pending/";

    public static string PendingDirectory(string deltaId) => PendingRoot + deltaId;

    public static string StagedCurrentState(string deltaId) => PendingDirectory(deltaId) + "/CURRENT-STATE.next.md";

    public static string StagedPayload(string deltaId) => PendingDirectory(deltaId) + "/PUBLICATION-PAYLOAD.json";
}

/// <summary>What the working checkout and the remote look like at the start of a step.</summary>
internal sealed record Observation(string Remote, string Local, bool Clean);

/// <summary>A changed path between two trees.</summary>
internal sealed record Change(string Status, string Path);

/// <summary>
/// Repository evidence, read through the git seam. Every digest here is over git blob
/// bytes (contract section 2), never over a working-tree file.
/// </summary>
internal sealed class Repository
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly IPublisherGit _git;

    public Repository(IPublisherGit git, string root)
    {
        _git = git;
        Root = root;
    }

    public string Root { get; }

    /// <summary>The revision that means "the index".</summary>
    public const string Index = ":";

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public GitResult Git(params string[] arguments) => _git.Run(arguments);

    public string CurrentBranch()
    {
        GitResult result = Git("symbolic-ref", "--short", "-q", "HEAD");
        return result.Succeeded ? result.Text.Trim() : string.Empty;
    }

    public Observation Observe(string branch)
    {
        if (CurrentBranch() != branch)
        {
            throw Drift($"The checkout is not on {branch}.");
        }

        Fetch();
        string remote = RemoteHead(branch);
        string tracking = Resolve("refs/remotes/origin/" + branch)
            ?? throw Drift($"There is no remote-tracking ref for {branch}.");

        if (tracking != remote)
        {
            throw Drift($"After fetching, origin/{branch} is {tracking} but the remote reports {remote}.");
        }

        string local = Resolve("HEAD") ?? throw Drift("HEAD does not resolve.");
        GitResult status = Git("status", "--porcelain", "--untracked-files=all");

        if (!status.Succeeded)
        {
            throw Drift("git could not report the working tree status.");
        }

        return new Observation(remote, local, status.Output.Length == 0);
    }

    public void Fetch()
    {
        if (!Git("fetch", "--no-tags", "--quiet", "origin").Succeeded)
        {
            throw Drift("Fetching origin failed.");
        }
    }

    /// <summary>The branch head as the remote itself reports it, not the local copy.</summary>
    public string RemoteHead(string branch)
    {
        GitResult result = Git("ls-remote", "origin", "refs/heads/" + branch);
        string[] fields = result.Succeeded ? result.Text.Split(['\t', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];

        if (fields.Length != 2 || fields[1] != "refs/heads/" + branch)
        {
            throw Drift($"The remote does not report exactly one head for {branch}.");
        }

        return fields[0];
    }

    public string? Resolve(string revision)
    {
        GitResult result = Git("rev-parse", "--verify", "--quiet", revision + "^{commit}");
        return result.Succeeded ? result.Text.Trim() : null;
    }

    public string? Parent(string commit) => Resolve(commit + "^");

    public bool IsAncestor(string ancestor, string descendant) =>
        Git("merge-base", "--is-ancestor", ancestor, descendant).ExitCode == 0;

    public bool Exists(string revision, string path)
    {
        if (revision == Index)
        {
            GitResult listed = Git("ls-files", "--", path);
            return listed.Succeeded && listed.Output.Length != 0;
        }

        GitResult entry = Git("ls-tree", revision, "--", path);
        return entry.Succeeded && entry.Output.Length != 0;
    }

    public byte[]? TryBlob(string revision, string path)
    {
        if (!Exists(revision, path))
        {
            return null;
        }

        GitResult blob = Git("cat-file", "blob", (revision == Index ? string.Empty : revision) + ":" + path);
        return blob.Succeeded ? blob.Output : throw Drift($"{path} at {Describe(revision)} could not be read.");
    }

    public byte[] Blob(string revision, string path) =>
        TryBlob(revision, path) ?? throw Drift($"{path} does not exist at {Describe(revision)}.");

    public string Text(string revision, string path)
    {
        try
        {
            return StrictUtf8.GetString(Blob(revision, path));
        }
        catch (DecoderFallbackException)
        {
            throw Drift($"{path} at {Describe(revision)} is not valid UTF-8.");
        }
    }

    public ParsedLedger Ledger(string revision, string path, LedgerKind kind)
    {
        try
        {
            return ParsedLedger.Parse(Text(revision, path), kind);
        }
        catch (LedgerFormatException ex)
        {
            throw Drift($"{path} at {Describe(revision)} does not parse: {ex.Message}");
        }
    }

    public bool ObjectExists(string revision, string path) =>
        Git("cat-file", "-e", revision + ":" + path).ExitCode == 0;

    /// <summary>The paths that differ, from <paramref name="from"/> to <paramref name="to"/> or to the index.</summary>
    public IReadOnlyList<Change> Changes(string from, string to)
    {
        GitResult diff = to == Index
            ? Git("diff", "--cached", "--name-status", "-z", "--no-renames", "--no-ext-diff", from, "--")
            : Git("diff", "--name-status", "-z", "--no-renames", "--no-ext-diff", from, to, "--");

        if (!diff.Succeeded)
        {
            throw Drift("git could not list the changed paths.");
        }

        string[] fields = diff.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<Change> changes = [];

        for (int i = 0; i + 1 < fields.Length; i += 2)
        {
            changes.Add(new Change(fields[i], fields[i + 1]));
        }

        return changes;
    }

    public IReadOnlyList<string> PendingDirectories(string revision)
    {
        GitResult entries = Git("ls-tree", "--name-only", revision, "--", CanonicalPaths.PendingRoot);
        return entries.Succeeded
            ? [.. entries.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)]
            : throw Drift("The pending directory could not be listed.");
    }

    public IReadOnlyList<string> FilesUnder(string revision, string directory)
    {
        GitResult entries = Git("ls-tree", "-r", "--name-only", revision, "--", directory + "/");
        return entries.Succeeded
            ? [.. entries.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)]
            : throw Drift($"{directory} could not be listed.");
    }

    public IReadOnlyList<string> CommitsTouching(string head, string path)
    {
        GitResult list = Git("rev-list", "--reverse", head, "--", path);
        return list.Succeeded
            ? [.. list.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)]
            : throw Drift($"The history of {path} could not be listed.");
    }

    /// <summary>Writes exact bytes into the working tree.</summary>
    public void Write(string path, byte[] bytes)
    {
        string full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public void Delete(string path)
    {
        string full = FullPath(path);

        if (File.Exists(full))
        {
            File.Delete(full);
        }

        string? directory = Path.GetDirectoryName(full);

        while (directory is not null && directory.Length > Root.Length && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    public void StageAll()
    {
        if (!Git("add", "-A", "--", ".").Succeeded)
        {
            throw Drift("git could not stage the working tree.");
        }
    }

    public string Commit(string message)
    {
        GitResult commit = Git("commit", "-q", "-m", message);

        if (!commit.Succeeded)
        {
            throw Drift($"git could not commit: {commit.StandardError.Trim()}");
        }

        return Resolve("HEAD") ?? throw Drift("HEAD does not resolve after committing.");
    }

    /// <summary>A normal fast-forward push of one commit. Returns whether the remote accepted it.</summary>
    public bool Push(string commit, string branch) =>
        Git("push", "--porcelain", "origin", commit + ":refs/heads/" + branch).Succeeded;

    private string FullPath(string path)
    {
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Split('/').Any(x => x is ".." or "." or ""))
        {
            throw new PublisherFailure(FailureClasses.ScopeViolation, $"'{path}' is not a plain repository-relative path.");
        }

        return Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Describe(string revision) => revision == Index ? "the index" : revision;

    private static PublisherFailure Drift(string detail) => new(FailureClasses.PreconditionDrift, detail);
}
