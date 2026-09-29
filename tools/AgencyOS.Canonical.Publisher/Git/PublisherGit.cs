using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AgencyOS.Canonical.Publisher.Git;

/// <summary>The outcome of one git invocation. Standard output is kept as bytes.</summary>
internal sealed record GitResult(int ExitCode, byte[] Output, string StandardError)
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public bool Succeeded => ExitCode == 0;

    public string Text => Utf8.GetString(Output);
}

/// <summary>Every git operation the publisher performs goes through this seam.</summary>
internal interface IPublisherGit
{
    GitResult Run(params string[] arguments);
}

/// <summary>
/// The publisher's git write boundary. It is enforced before any process starts.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the detector, the publisher must write: it stages files, commits, fetches and
/// pushes. So the boundary is not "read-only". It is a closed list of subcommands, each
/// in the only shape the contract needs.
/// </para>
/// <para>
/// A push is exactly <c>push --porcelain origin &lt;full SHA&gt;:refs/heads/&lt;branch&gt;</c>:
/// a normal fast-forward of one commit. No "+" refspec and no force of any kind.
/// </para>
/// <para>
/// A commit is exactly <c>commit -q -m &lt;message&gt;</c>, so it cannot amend. A fetch
/// updates only the remote-tracking refs. Nothing can reset, check out, switch, rebase,
/// merge, tag, delete a branch or change configuration: those subcommands are not on
/// the list.
/// </para>
/// </remarks>
internal static partial class GitCommandPolicy
{
    public static readonly IReadOnlySet<string> Subcommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "fetch", "ls-remote", "rev-parse", "merge-base", "rev-list", "diff", "status",
        "symbolic-ref", "cat-file", "ls-tree", "ls-files", "for-each-ref", "add", "commit", "push",
    };

    private static readonly string[] ForbiddenOptions =
    [
        "--force", "-f", "--force-with-lease", "--force-if-includes", "--mirror", "--delete", "-d",
        "--amend", "--prune", "--all", "--tags", "--no-verify", "--allow-empty",
    ];

    public static void Validate(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || !Subcommands.Contains(arguments[0]))
        {
            throw Refuse(arguments, "the subcommand is not on the publisher's list");
        }

        foreach (string argument in arguments.Skip(1))
        {
            if (ForbiddenOptions.Any(x => argument == x || argument.StartsWith(x + "=", StringComparison.Ordinal)))
            {
                throw Refuse(arguments, $"'{argument}' is forbidden");
            }
        }

        switch (arguments[0])
        {
            case "push":
                if (arguments.Count != 4 || arguments[1] != "--porcelain" || arguments[2] != "origin"
                    || !PushRefspec().IsMatch(arguments[3]))
                {
                    throw Refuse(arguments, "a push must be exactly 'push --porcelain origin <full SHA>:refs/heads/<branch>'");
                }

                break;

            case "fetch":
                if (!arguments.Skip(1).All(x => x is "--no-tags" or "--quiet" or "origin"))
                {
                    throw Refuse(arguments, "a fetch may only update the remote-tracking refs of origin");
                }

                break;

            case "commit":
                if (arguments.Count != 4 || arguments[1] != "-q" || arguments[2] != "-m")
                {
                    throw Refuse(arguments, "a commit must be exactly 'commit -q -m <message>'");
                }

                break;

            case "add":
                if (arguments.Count < 4 || arguments[1] != "-A" || arguments[2] != "--"
                    || arguments.Skip(3).Any(x => x.StartsWith('-')))
                {
                    throw Refuse(arguments, "an add must be exactly 'add -A -- <paths>'");
                }

                break;
        }
    }

    private static InvalidOperationException Refuse(IReadOnlyList<string> arguments, string reason) =>
        new($"git {string.Join(' ', arguments)} was refused: {reason}.");

    [GeneratedRegex("^[0-9a-f]{40}:refs/heads/[A-Za-z0-9._/-]+$")]
    private static partial Regex PushRefspec();
}

/// <summary>Runs git after the command policy has accepted the arguments.</summary>
internal sealed class GitCli : IPublisherGit
{
    private readonly string _repository;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public GitCli(string repository, IReadOnlyDictionary<string, string>? environment = null)
    {
        _repository = repository;
        _environment = environment ?? new Dictionary<string, string>();
    }

    public GitResult Run(params string[] arguments)
    {
        GitCommandPolicy.Validate(arguments);

        ProcessStartInfo start = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(_repository);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (KeyValuePair<string, string> pair in _environment)
        {
            start.Environment[pair.Key] = pair.Value;
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("git could not be started.");

        Task<string> error = process.StandardError.ReadToEndAsync();
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();

        return new GitResult(process.ExitCode, output.ToArray(), error.GetAwaiter().GetResult());
    }
}
