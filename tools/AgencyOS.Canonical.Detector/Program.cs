using System.Globalization;
using AgencyOS.Canonical.Detector.Detection;
using AgencyOS.Canonical.Detector.Git;
using AgencyOS.Canonical.Detector.Output;

namespace AgencyOS.Canonical.Detector;

/// <summary>
/// Command-line entry point. Usage is in docs/control-room/CANONICAL-DETECTOR.md.
/// </summary>
/// <remarks>
/// The only thing this program writes is its own standard output and standard error.
/// </remarks>
internal static class Program
{
    public const int ExitNoRelevantChange = 0;
    public const int ExitReviewRequired = 10;
    public const int ExitDetectorFailure = 20;
    public const int ExitUsage = 64;

    private const string Usage =
        "Usage: AgencyOS.Canonical.Detector --repo <path> --branch <name> --baseline <sha> " +
        "--observed <sha|local-head|remote-tracking> [--detected-utc <yyyy-MM-ddTHH:mm:ssZ>] [--format json|alert]";

    public static int Main(string[] args)
    {
        // The packet is UTF-8 JSON (RFC 8785). On Windows the console otherwise defaults
        // to the OEM code page and would mangle every non-ASCII character it quotes.
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);

        Dictionary<string, string> options = new(StringComparer.Ordinal);

        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(Usage);
                return ExitUsage;
            }

            options[args[i][2..]] = args[i + 1];
        }

        string[] required = ["repo", "branch", "baseline", "observed"];

        if (required.Any(x => !options.ContainsKey(x))
            || options.Keys.Any(x => x is not ("repo" or "branch" or "baseline" or "observed" or "detected-utc" or "format")))
        {
            Console.Error.WriteLine(Usage);
            return ExitUsage;
        }

        string format = options.GetValueOrDefault("format", "json");

        if (format is not ("json" or "alert"))
        {
            Console.Error.WriteLine(Usage);
            return ExitUsage;
        }

        string detectedUtc = options.TryGetValue("detected-utc", out string? supplied)
            ? supplied
            : DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        object outcome = Run(options["repo"], new DetectorRequest(
            options["branch"], options["baseline"], options["observed"], detectedUtc));

        Console.Out.Write(format == "json" ? PacketWriter.Write(outcome) + "\n" : AlertWriter.Write(outcome));

        return outcome switch
        {
            DetectionReport { Result: Results.ReviewRequired } => ExitReviewRequired,
            DetectionReport => ExitNoRelevantChange,
            _ => ExitDetectorFailure,
        };
    }

    internal static object Run(string repository, DetectorRequest request) =>
        new Detection.Detector(new GitCli(repository)).Detect(request);
}
