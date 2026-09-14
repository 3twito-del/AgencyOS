using System.Text.Json.Serialization;

namespace AgencyOS.Reviewer.Reachability;

/// <summary>How a gap between two parts of the product should be read.</summary>
/// <remarks>
/// Four values rather than a boolean, because "unused" and "wrong" are different
/// claims. An endpoint with no client caller may be deliberate; a dialog nothing
/// constructs is not. Forcing every gap into "defect" is what makes a reachability
/// report something people learn to ignore.
/// </remarks>
public enum GapVerdict
{
    /// <summary>Deliberate, and the source says so.</summary>
    Intentional,

    /// <summary>Something the product cannot reach and should be able to.</summary>
    UnreachableDefect,

    /// <summary>Server capability with no Windows path, by design.</summary>
    ApiOnlyByDesign,

    /// <summary>The evidence does not settle it.</summary>
    Inconclusive,
}

/// <summary>One thing the analysis found.</summary>
/// <param name="Id">Stable identifier for the observation.</param>
/// <param name="Kind">Short slug naming the class of gap.</param>
/// <param name="Subject">What the observation is about.</param>
/// <param name="Detail">What was observed, in one sentence.</param>
/// <param name="Evidence">Where to look.</param>
/// <param name="Verdict">How the harness classified it before review.</param>
public sealed record ReachabilityObservation(
    string Id,
    string Kind,
    string Subject,
    string Detail,
    IReadOnlyList<string> Evidence,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] GapVerdict Verdict);
