using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgencyOS.Canonical.Detector.Detection;
using AgencyOS.Canonical.Detector.Git;
using AgencyOS.Canonical.Detector.Output;

namespace AgencyOS.Tests.Canonical;

/// <summary>
/// A throwaway git repository with deterministic commits, so that the same fixture
/// always has the same SHAs and the detector's output can be compared byte for byte.
/// </summary>
/// <remarks>
/// Commits are made with a fixed author, committer and clock, and with the user's and
/// the system's git configuration switched off, so no local setting (a hook, a signing
/// key, autocrlf) can change what is committed. The fixture is the only thing that
/// writes: it is how a scenario is set up, never part of what is tested.
/// </remarks>
internal sealed class GitFixture : IDisposable
{
    public const string Branch = "main";

    private int _clock;

    public GitFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "agencyos-detector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        EmptyConfig = Path.Combine(Root, "..", "empty.gitconfig");

        if (!File.Exists(EmptyConfig))
        {
            File.WriteAllText(EmptyConfig, string.Empty);
        }

        Git("init", "-q", "-b", Branch);
    }

    public string Root { get; }

    private string EmptyConfig { get; }

    public GitFixture Write(string path, string content)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return this;
    }

    public GitFixture Delete(string path)
    {
        Git("rm", "-q", path);
        return this;
    }

    /// <summary>Commits everything in the tree and returns the new commit's SHA.</summary>
    public string Commit(string message)
    {
        Git("add", "-A");
        _clock++;
        Git("commit", "-q", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    public string Checkout(string reference)
    {
        Git("checkout", "-q", reference);
        return Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>Runs the detector through the same code path as the command line.</summary>
    public object Detect(string baseline, string observed, IGitReader? reader = null) =>
        new Detector(reader ?? new GitCli(Root)).Detect(
            new DetectorRequest(Branch, baseline, observed, "2026-01-01T12:00:00Z"));

    public static string Json(object outcome) => PacketWriter.Write(outcome);

    public static JsonElement Parse(object outcome) =>
        JsonDocument.Parse(PacketWriter.Write(outcome)).RootElement.Clone();

    public static string Alert(object outcome) => AlertWriter.Write(outcome);

    private string Git(params string[] arguments)
    {
        string timestamp = $"2026-01-01T00:00:{_clock:00}Z";

        ProcessStartInfo start = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Root,
        };

        foreach (string option in new[]
        {
            "-c", "core.autocrlf=false",
            "-c", "commit.gpgsign=false",
            "-c", "core.hooksPath=.no-hooks",
            "-c", "user.name=AgencyOS Fixture",
            "-c", "user.email=fixture@agencyos.invalid",
        })
        {
            start.ArgumentList.Add(option);
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = EmptyConfig;
        start.Environment["GIT_AUTHOR_DATE"] = timestamp;
        start.Environment["GIT_COMMITTER_DATE"] = timestamp;
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Result}");
        }

        return output;
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
