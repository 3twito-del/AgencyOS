using System.Text.Json.Serialization;

namespace AgencyOS.Reviewer.Ledger;

/// <summary>What kind of defect a finding is.</summary>
public enum FindingCategory
{
    /// <summary>The product does the wrong thing.</summary>
    Bug,

    /// <summary>The product does the right thing awkwardly.</summary>
    Ux,

    /// <summary>A control or layout is wrong.</summary>
    Ui,

    /// <summary>It renders wrongly.</summary>
    Visual,

    /// <summary>Assistive technology cannot use it.</summary>
    Accessibility,

    /// <summary>The keyboard cannot reach or operate it.</summary>
    Keyboard,

    /// <summary>The words are wrong, inconsistent or misleading.</summary>
    Copy,

    /// <summary>One idea is expressed two ways.</summary>
    Consistency,

    /// <summary>Implemented and unreachable.</summary>
    DeadSurface,

    /// <summary>The server offers it and the client does not, or the reverse.</summary>
    ApiUiGap,

    /// <summary>Authorization is right and the experience of it is not.</summary>
    SecurityUx,

    /// <summary>It is slow, or does more work than it needs to.</summary>
    Performance,

    /// <summary>Nothing would have caught this.</summary>
    TestGap,
}

/// <summary>How bad it is.</summary>
public enum Severity
{
    /// <summary>Data loss, security, integrity, or a catastrophic incorrect action.</summary>
    S0,

    /// <summary>A critical workflow is broken, or the product is materially unsafe.</summary>
    S1,

    /// <summary>A significant functional, UX or accessibility defect.</summary>
    S2,

    /// <summary>Moderate friction, inconsistency or polish.</summary>
    S3,

    /// <summary>An optional improvement.</summary>
    S4,
}

/// <summary>How sure the reviewer is that this is real.</summary>
public enum Confidence
{
    /// <summary>Reproduced, or proved from source.</summary>
    ConfirmedDefect,

    /// <summary>Strong evidence, not reproduced end to end.</summary>
    LikelyDefect,

    /// <summary>A judgment about design, stated as one.</summary>
    DesignRecommendation,

    /// <summary>The evidence does not settle it.</summary>
    Inconclusive,
}

/// <summary>Whether it happens every time.</summary>
public enum Reproducibility
{
    /// <summary>Every time.</summary>
    Always,

    /// <summary>Sometimes.</summary>
    Intermittent,

    /// <summary>Observed once.</summary>
    Once,

    /// <summary>Read from source rather than observed.</summary>
    Static,

    /// <summary>Could not be attempted in this environment.</summary>
    NotAttempted,
}

/// <summary>
/// One thing the review found.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Evidence"/> is required and is validated to be non-empty, because
/// §20 of the review brief forbids a finding that consists only of an opinion. A
/// design recommendation may be judgment-based, and it says so through
/// <see cref="Confidence.DesignRecommendation"/> rather than by omitting evidence.
/// </para>
/// <para>
/// Identifiers are stable. A later audit that finds the same defect reuses the
/// identifier rather than issuing a new one, so a repair queue can be tracked
/// across runs.
/// </para>
/// </remarks>
public sealed record Finding
{
    /// <summary>Stable identifier, for example <c>AOS-R001-014</c>.</summary>
    public required string Id { get; init; }

    /// <summary>What kind of defect.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required FindingCategory Category { get; init; }

    /// <summary>How bad.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Severity Severity { get; init; }

    /// <summary>How sure.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Confidence Confidence { get; init; }

    /// <summary>How often.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Reproducibility Reproducibility { get; init; }

    /// <summary>One-line statement of the defect.</summary>
    public required string Title { get; init; }

    /// <summary>Surface identifiers this affects.</summary>
    public required IReadOnlyList<string> Surface { get; init; }

    /// <summary>Which synthetic role saw it.</summary>
    public string Role { get; init; } = "owner";

    /// <summary>What had to be true first.</summary>
    public string Preconditions { get; init; } = string.Empty;

    /// <summary>How to see it.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>What should happen.</summary>
    public required string Expected { get; init; }

    /// <summary>What does happen.</summary>
    public required string Actual { get; init; }

    /// <summary>What proves it.</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>Screenshots, relative to the run directory.</summary>
    public IReadOnlyList<string> Screenshots { get; init; } = [];

    /// <summary>Log or trace lines that corroborate it.</summary>
    public IReadOnlyList<string> LogEvidence { get; init; } = [];

    /// <summary>Where a repair would probably go.</summary>
    public IReadOnlyList<string> LikelySourceFiles { get; init; } = [];

    /// <summary>Why it matters to an agency operator.</summary>
    public required string WhyItMatters { get; init; }

    /// <summary>What a repair would do. Not applied in Run 001.</summary>
    public required string SuggestedCorrection { get; init; }

    /// <summary>The test that should exist so this cannot come back.</summary>
    public string RegressionTestCandidate { get; init; } = string.Empty;

    /// <summary>What repairing it could break.</summary>
    public string RepairRisk { get; init; } = string.Empty;

    /// <summary>Checks the finding carries its own proof.</summary>
    public bool IsWellFormed =>
        Id.Length > 0
        && Title.Length > 0
        && Expected.Length > 0
        && Actual.Length > 0
        && Evidence.Count > 0
        && Surface.Count > 0
        && WhyItMatters.Length > 0
        && SuggestedCorrection.Length > 0;
}
