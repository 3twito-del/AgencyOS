using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Publishing;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>A deliberate interruption, as if the process died at that point.</summary>
internal sealed class SimulatedCrash() : Exception("simulated crash");

/// <summary>
/// Wraps the publisher's git seam so a test can interfere at an exact call: crash before
/// or after it, or let another actor push first. The publisher's command policy still
/// applies, because the inner seam enforces it.
/// </summary>
internal sealed class InterferingGit(IPublisherGit inner) : IPublisherGit
{
    private readonly List<(string Subcommand, int Occurrence, bool After, Action Action)> _actions = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public InterferingGit Before(string subcommand, int occurrence, Action action)
    {
        _actions.Add((subcommand, occurrence, false, action));
        return this;
    }

    public InterferingGit After(string subcommand, int occurrence, Action action)
    {
        _actions.Add((subcommand, occurrence, true, action));
        return this;
    }

    public GitResult Run(params string[] arguments)
    {
        int count = _counts[arguments[0]] = _counts.GetValueOrDefault(arguments[0]) + 1;

        foreach ((string _, int _, bool _, Action action) in _actions.Where(x => !x.After && x.Subcommand == arguments[0] && x.Occurrence == count))
        {
            action();
        }

        GitResult result = inner.Run(arguments);

        foreach ((string _, int _, bool _, Action action) in _actions.Where(x => x.After && x.Subcommand == arguments[0] && x.Occurrence == count))
        {
            action();
        }

        return result;
    }
}

/// <summary>
/// A throwaway canonical repository: a bare "origin", the operator's clone the publisher
/// runs in, and a second clone that plays any other actor on the remote.
/// </summary>
/// <remarks>
/// Every git call here, the publisher's included, runs with the user's and the system's
/// configuration switched off, so no local hook, signing key or line-ending setting
/// changes a scenario. The origin is always a bare repository under the temporary
/// directory: nothing is ever pushed to AgencyOS.
/// </remarks>
internal sealed class PublisherFixture : IDisposable
{
    public const string Branch = "main";
    public const string CurrentState = "docs/control-room/CURRENT-STATE.md";
    public const string Deltas = "docs/control-room/CANONICAL-DELTAS.md";
    public const string Decisions = "docs/control-room/DECISIONS.md";

    public const string BaseCurrentState =
        "# AgencyOS current canonical state\n\n**Status:** CURRENT\n\n## Release identity\n\nRelease: ALPHA 0.1.0 build 97\n\n" +
        "## Next-generation stage\n\nNG-4 is NEXT and not authorized.\n\n## Latest published delta\n\n`DELTA-20260101-001`: PUBLISHED.\n";

    public const string BaseDeltas =
        "# AgencyOS canonical deltas\n\nThe ledger of candidate changes to canonical state.\n\n## Template\n\n```\n## DELTA-YYYYMMDD-NNN\n\nStatus:\nSeal authorizations: Pending\n```\n\n---\n\n# Entries\n\n" +
        "## DELTA-20260101-001\n\nStatus: PUBLISHED\n\nDetected: 2026-01-01T00:00:00Z\n\nPublished: 2026-01-01T00:10:00Z\n\nSources: fixture\n\n" +
        "Prior claim: none\n\nCandidate/new claim: the fixture baseline\n\nScope: fixture\n\nEvidence: fixture\n\nConflicts: None\n\n" +
        "Authority required: CONTROL_ROOM\n\nAdjudication: The Control Room accepted the fixture baseline.\n\nSupersedes: None\n\n" +
        "Unchanged: everything\n\nOpen: None\n\nPublication receipt: 1111111111111111111111111111111111111111 on origin/main; remote readback verified 2026-01-01T00:10:00Z\n";

    public const string BaseDecisions =
        "# AgencyOS Control Room decisions\n\n**Migration status.** Decisions are migrated under the protocol.\n\n---\n\n# Entries\n\n" +
        "## DECISION-20260101-001\n\nStatus: ACTIVE\n\nDate: 2026-01-01\n\nAuthority: OWNER\n\nQuestion: Fixture question?\n\n" +
        "Decision: The fixture decision.\n\nScope: fixture\n\nEvidence / provenance: fixture\n\nConsequences: fixture\n\nSupersedes: None\n\n" +
        "Unchanged: None\n\nOpen: None\n\nRecorded by: fixture\n\n" +
        "Publication receipt: 1111111111111111111111111111111111111111 on origin/main; remote readback verified 2026-01-01T00:10:00Z\n";

    /// <summary>A decision added in a later commit with its receipt still Pending, for content-bearing tests.</summary>
    public const string PendingDecision =
        "## DECISION-20260101-002\n\nStatus: ACTIVE\n\nDate: 2026-01-01\n\nAuthority: CONTROL_ROOM\n\nQuestion: A second fixture question?\n\n" +
        "Decision: The second fixture decision.\n\nScope: fixture\n\nEvidence / provenance: fixture\n\nConsequences: fixture\n\nSupersedes: None\n\n" +
        "Unchanged: None\n\nOpen: None\n\nRecorded by: fixture\n\nPublication receipt: Pending\n";

    private static readonly UTF8Encoding Utf8 = new(false);
    private int _clock;

    public PublisherFixture(bool operatorAutoCrlf = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "agencyos-publisher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        EmptyConfig = Path.Combine(Root, "empty.gitconfig");
        File.WriteAllText(EmptyConfig, string.Empty);
        Origin = Path.Combine(Root, "origin.git");
        Operator = Path.Combine(Root, "operator");
        Other = Path.Combine(Root, "other");
        string seed = Path.Combine(Root, "seed");

        Git(Root, "init", "-q", "--bare", "-b", Branch, Origin);
        Directory.CreateDirectory(seed);
        Git(seed, "init", "-q", "-b", Branch);
        Configure(seed, autoCrlf: false);

        WriteFile(seed, CurrentState, BaseCurrentState);
        WriteFile(seed, Deltas, BaseDeltas);
        WriteFile(seed, Decisions, BaseDecisions);
        WriteFile(seed, "docs/control-room/CANONICAL-STATE-PROTOCOL.md", "# Protocol\n");
        WriteFile(seed, "docs/notes.md", "Notes.\n");
        WriteFile(seed, "src/Product.cs", "class Product {}\n");
        Commit(seed, "baseline");

        WriteFile(seed, Decisions, BaseDecisions + "\n" + PendingDecision);
        PendingDecisionCommit = Commit(seed, "add a decision whose receipt is pending");

        Git(seed, "remote", "add", "origin", Origin);
        Git(seed, "push", "-q", "origin", Branch);

        Git(Root, "-c", "core.autocrlf=" + (operatorAutoCrlf ? "true" : "false"), "clone", "-q", Origin, Operator);
        Configure(Operator, operatorAutoCrlf);
        Git(Root, "clone", "-q", Origin, Other);
        Configure(Other, autoCrlf: false);
        Start = Head(Operator);
    }

    public string Root { get; }

    public string Origin { get; }

    public string Operator { get; }

    public string Other { get; }

    public string Start { get; }

    public string PendingDecisionCommit { get; }

    private string EmptyConfig { get; }

    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_GLOBAL"] = EmptyConfig,
    };

    public IPublisherGit PublisherGit() => new GitCli(Operator, Environment);

    public AgencyOS.Canonical.Publisher.Publishing.Publisher Publisher(IPublisherGit? git = null, PublisherHooks? hooks = null) =>
        new(git ?? PublisherGit(), Operator, () => $"2026-01-02T00:{Interlocked.Increment(ref _clock) % 60:00}:00Z", hooks);

    /// <summary>Another actor pushes an unrelated commit to the remote.</summary>
    public string ForeignPush(string text = "Someone else.\n")
    {
        Git(Other, "pull", "-q", "--ff-only", "origin", Branch);
        WriteFile(Other, "docs/foreign.md", text + Guid.NewGuid().ToString("N") + "\n");
        string sha = Commit(Other, "an unrelated commit");
        Git(Other, "push", "-q", "origin", Branch);
        return sha;
    }

    /// <summary>The operator's checkout back to the remote head, as a person recovering would do.</summary>
    public void ResetOperatorToRemote()
    {
        Git(Operator, "fetch", "-q", "origin");
        Git(Operator, "reset", "-q", "--hard", "origin/" + Branch);
        Git(Operator, "clean", "-q", "-fd");
    }

    public string RemoteHead() => Git(Origin, "rev-parse", "refs/heads/" + Branch).Trim();

    public static string Head(string repository) => GitStatic(repository, null, "rev-parse", "HEAD").Trim();

    public string OperatorHead() => Head(Operator);

    public string Status() => Git(Operator, "status", "--porcelain", "--untracked-files=all");

    public string Parent(string commit) => Git(Origin, "rev-parse", commit + "^").Trim();

    /// <summary>A commit's parent, looked up in the operator's clone, which also holds unpushed commits.</summary>
    public string LocalParent(string commit) => Git(Operator, "rev-parse", commit + "^").Trim();

    public byte[] Blob(string revision, string path) => GitBytes(Origin, "cat-file", "blob", revision + ":" + path);

    public string Text(string revision, string path) => Utf8.GetString(Blob(revision, path));

    public bool ExistsAt(string revision, string path) => Git(Origin, "ls-tree", revision, "--", path).Length != 0;

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Sha256(string text) => Sha256(Utf8.GetBytes(text));

    /// <summary>Builds a commit in the operator's checkout by hand, as a crashed or careless actor might.</summary>
    public string CommitInOperator(string message, params (string Path, string? Text)[] files)
    {
        foreach ((string path, string? text) in files)
        {
            if (text is null)
            {
                File.Delete(Path.Combine(Operator, path));
            }
            else
            {
                WriteFile(Operator, path, text);
            }
        }

        return Commit(Operator, message);
    }

    public string Git(string directory, params string[] arguments) => Utf8.GetString(Run(directory, Environment, arguments));

    private byte[] GitBytes(string directory, params string[] arguments) => Run(directory, Environment, arguments);

    private static string GitStatic(string directory, IReadOnlyDictionary<string, string>? environment, params string[] arguments) =>
        Utf8.GetString(Run(directory, environment, arguments));

    private void Configure(string repository, bool autoCrlf)
    {
        Git(repository, "config", "user.name", "AgencyOS Fixture");
        Git(repository, "config", "user.email", "fixture@agencyos.invalid");
        Git(repository, "config", "commit.gpgsign", "false");
        Git(repository, "config", "core.autocrlf", autoCrlf ? "true" : "false");
    }

    private string Commit(string repository, string message)
    {
        Git(repository, "add", "-A");
        _clock++;
        Git(repository, "commit", "-q", "-m", message);
        return Head(repository);
    }

    private static void WriteFile(string repository, string path, string text)
    {
        string full = Path.Combine(repository, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Utf8.GetBytes(text));
    }

    private static byte[] Run(string directory, IReadOnlyDictionary<string, string>? environment, string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> pair in environment ?? new Dictionary<string, string>())
        {
            start.Environment[pair.Key] = pair.Value;
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed in {directory}: {error.Result}");
        }

        return output.ToArray();
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
