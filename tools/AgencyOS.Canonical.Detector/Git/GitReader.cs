using System.Diagnostics;
using System.Text;

namespace AgencyOS.Canonical.Detector.Git;

/// <summary>The outcome of one git invocation.</summary>
/// <remarks>
/// Standard output is kept as bytes, so that a blob digest is computed over exactly
/// what git stored, never over a decoded and re-encoded copy.
/// </remarks>
internal sealed record GitResult(int ExitCode, byte[] Output, string StandardError)
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public bool Succeeded => ExitCode == 0;

    public string Text => Utf8.GetString(Output);
}

/// <summary>
/// Everything the detector knows about a repository comes through this seam, so a test
/// can make one kind of evidence unavailable and check that it is reported as unknown.
/// </summary>
internal interface IGitReader
{
    GitResult Run(params string[] arguments);
}

/// <summary>
/// Runs git, and only the subcommands that cannot change a repository.
/// </summary>
/// <remarks>
/// The detector is read-only by construction rather than by care. A subcommand outside
/// <see cref="ReadOnlySubcommands"/> is refused before any process starts, so no code
/// path can commit, fetch, push, tag, check out or reset. <c>--no-optional-locks</c>
/// stops <c>git status</c> refreshing the index, which is otherwise a write.
/// </remarks>
internal sealed class GitCli : IGitReader
{
    internal static readonly IReadOnlySet<string> ReadOnlySubcommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "rev-parse",
        "merge-base",
        "rev-list",
        "diff",
        "for-each-ref",
        "status",
        "symbolic-ref",
        "cat-file",
        "ls-tree",
    };

    private readonly string _repository;

    public GitCli(string repository)
    {
        _repository = repository;
    }

    public GitResult Run(params string[] arguments)
    {
        if (arguments.Length == 0 || !ReadOnlySubcommands.Contains(arguments[0]))
        {
            string name = arguments.Length == 0 ? "(none)" : arguments[0];
            throw new InvalidOperationException(
                $"'{name}' is not a read-only git subcommand, and the detector runs no other kind.");
        }

        ProcessStartInfo start = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        start.ArgumentList.Add("--no-optional-locks");
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(_repository);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("git could not be started.");

        Task<string> error = process.StandardError.ReadToEndAsync();
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();

        return new GitResult(process.ExitCode, output.ToArray(), error.GetAwaiter().GetResult());
    }
}
