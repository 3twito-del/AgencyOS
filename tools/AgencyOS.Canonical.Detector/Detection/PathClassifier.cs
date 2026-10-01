namespace AgencyOS.Canonical.Detector.Detection;

/// <summary>
/// Sorts a changed path into the categories the Control Room reasons about.
/// </summary>
/// <remarks>
/// <para>
/// Classification is a path rule, not a reading of content. It says where a change
/// landed, never what the change means. A path can carry several categories: a
/// migration is also product source, and the closure record is also a review record.
/// </para>
/// <para>
/// A path is <em>canonical-relevant</em> unless its only category is
/// <see cref="Docs"/>. That follows the protocol's precedence list
/// (CANONICAL-STATE-PROTOCOL.md section 4), which names the Control Room artifacts,
/// release, review and ADR records as canonical sources and does not name the other
/// documents under <c>docs/</c>. Anything the rules do not recognise is
/// <see cref="Unclassified"/>, which is canonical-relevant: an unknown path is never
/// assumed harmless.
/// </para>
/// </remarks>
internal static class PathClassifier
{
    public const string ProductSource = "product_source";
    public const string SchemaMigration = "schema_migration";
    public const string ApiContract = "api_contract";
    public const string Domain = "domain";
    public const string Tests = "tests";
    public const string ContinuousIntegration = "ci";
    public const string Scripts = "scripts";
    public const string Tooling = "tooling";
    public const string BuildConfiguration = "build_configuration";
    public const string FormalSpecification = "formal_specification";
    public const string Configuration = "configuration";
    public const string Governance = "governance";
    public const string Docs = "docs";
    public const string ControlRoom = "control_room";
    public const string ControlRoomStaging = "control_room_staging";
    public const string ReleaseRecord = "release_record";
    public const string ReviewRecord = "review_record";
    public const string ClosureRecord = "closure_record";
    public const string Adr = "adr";
    public const string Unclassified = "unclassified";

    public const string ClosureRecordPath = "docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md";
    public const string CurrentStatePath = "docs/control-room/CURRENT-STATE.md";
    public const string DeltaLedgerPath = "docs/control-room/CANONICAL-DELTAS.md";
    public const string DecisionLedgerPath = "docs/control-room/DECISIONS.md";
    public const string StagingPrefix = "docs/control-room/pending/";

    private static readonly string[] BuildConfigurationFiles =
    [
        "AgencyOS.sln",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "NuGet.config",
        ".gitattributes",
        ".gitignore",
        ".editorconfig",
    ];

    /// <summary>The categories of one repository-relative path, in a fixed order.</summary>
    public static IReadOnlyList<string> Classify(string path)
    {
        List<string> categories = [];

        if (path.StartsWith("src/", StringComparison.Ordinal))
        {
            categories.Add(ProductSource);

            if (path.Contains("/Migrations/", StringComparison.Ordinal))
            {
                categories.Add(SchemaMigration);
            }

            if (path.StartsWith("src/AgencyOS.Contracts/", StringComparison.Ordinal)
                || path.Contains("openapi", StringComparison.OrdinalIgnoreCase))
            {
                categories.Add(ApiContract);
            }

            if (path.StartsWith("src/AgencyOS.Domain/", StringComparison.Ordinal))
            {
                categories.Add(Domain);
            }
        }
        else if (path.StartsWith("tests/", StringComparison.Ordinal))
        {
            categories.Add(Tests);
        }
        else if (path.StartsWith(".github/", StringComparison.Ordinal))
        {
            categories.Add(ContinuousIntegration);
        }
        else if (path.StartsWith("scripts/", StringComparison.Ordinal))
        {
            categories.Add(Scripts);
        }
        else if (path.StartsWith("tools/", StringComparison.Ordinal))
        {
            categories.Add(Tooling);
        }
        else if (path.StartsWith("build/", StringComparison.Ordinal)
            || path.StartsWith(".config/", StringComparison.Ordinal)
            || BuildConfigurationFiles.Contains(path, StringComparer.Ordinal))
        {
            categories.Add(BuildConfiguration);
        }
        else if (path.StartsWith("specs/", StringComparison.Ordinal))
        {
            categories.Add(FormalSpecification);
        }
        else if (path.StartsWith("config/", StringComparison.Ordinal))
        {
            categories.Add(Configuration);
        }
        else if (path == "CLAUDE.md" || path.StartsWith(".claude/", StringComparison.Ordinal))
        {
            categories.Add(Governance);
        }
        else if (path.StartsWith("docs/", StringComparison.Ordinal))
        {
            categories.Add(Docs);

            if (path.StartsWith("docs/control-room/", StringComparison.Ordinal))
            {
                categories.Add(path.StartsWith(StagingPrefix, StringComparison.Ordinal)
                    ? ControlRoomStaging
                    : ControlRoom);
            }
            else if (path.StartsWith("docs/releases/", StringComparison.Ordinal))
            {
                categories.Add(ReleaseRecord);
            }
            else if (path.StartsWith("docs/reviews/", StringComparison.Ordinal))
            {
                categories.Add(ReviewRecord);

                if (path == ClosureRecordPath)
                {
                    categories.Add(ClosureRecord);
                }
            }
            else if (path.StartsWith("docs/adr/", StringComparison.Ordinal))
            {
                categories.Add(Adr);
            }
        }
        else
        {
            categories.Add(Unclassified);
        }

        return categories;
    }

    /// <summary>Whether a path, by the path rule alone, could bear on canonical state.</summary>
    public static bool IsCanonicalRelevant(IReadOnlyList<string> categories) =>
        !(categories.Count == 1 && categories[0] == Docs);
}
