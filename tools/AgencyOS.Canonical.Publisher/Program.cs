using AgencyOS.Canonical.Publisher.Git;
using AgencyOS.Canonical.Publisher.Publishing;

namespace AgencyOS.Canonical.Publisher;

/// <summary>
/// Command-line entry point. Usage is in docs/control-room/CANONICAL-PUBLISHER.md.
/// </summary>
/// <remarks>
/// It reads the payload or authorization file it is given, runs one command, and writes
/// the Publisher Receipt to standard output. It keeps no state of its own.
/// </remarks>
internal static class Program
{
    public const int ExitPublishedOrCorrected = 0;
    public const int ExitAwaitingSeal = 10;
    public const int ExitStopped = 20;
    public const int ExitFailedVerification = 30;
    public const int ExitUsage = 64;

    private const string Usage =
        "Usage:\n" +
        "  AgencyOS.Canonical.Publisher stage     --repo <path> --payload <file>\n" +
        "  AgencyOS.Canonical.Publisher authorize --repo <path> --authorization <file>\n" +
        "  AgencyOS.Canonical.Publisher resume    --repo <path> --branch <name> --delta <DELTA-ID>\n" +
        "  AgencyOS.Canonical.Publisher correct   --repo <path> --payload <file>";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);

        if (args.Length == 0 || (args.Length - 1) % 2 != 0)
        {
            Console.Error.WriteLine(Usage);
            return ExitUsage;
        }

        Dictionary<string, string> options = new(StringComparer.Ordinal);

        for (int i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(Usage);
                return ExitUsage;
            }

            options[args[i][2..]] = args[i + 1];
        }

        string[] expected = args[0] switch
        {
            "stage" or "correct" => ["repo", "payload"],
            "authorize" => ["repo", "authorization"],
            "resume" => ["repo", "branch", "delta"],
            _ => [],
        };

        if (expected.Length == 0 || !options.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
        {
            Console.Error.WriteLine(Usage);
            return ExitUsage;
        }

        string repo = Path.GetFullPath(options["repo"]);
        Publishing.Publisher publisher = new(new GitCli(repo), repo);

        Receipt receipt = args[0] switch
        {
            "stage" => publisher.Stage(File.ReadAllBytes(options["payload"])),
            "correct" => publisher.Correct(File.ReadAllBytes(options["payload"])),
            "authorize" => publisher.Authorize(File.ReadAllBytes(options["authorization"])),
            _ => publisher.Resume(options["branch"], options["delta"]),
        };

        Console.Out.Write(receipt.Render());
        return ExitCode(receipt.Result);
    }

    internal static int ExitCode(string result) => result switch
    {
        Results.PublishedVerified or Results.CorrectionVerified => ExitPublishedOrCorrected,
        Results.BasisVerifiedAwaitingSeal => ExitAwaitingSeal,
        Results.FailedSealValidation or Results.FailedRemoteVerification => ExitFailedVerification,
        _ => ExitStopped,
    };
}
