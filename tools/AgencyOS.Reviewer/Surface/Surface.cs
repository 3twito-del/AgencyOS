using System.Text.Json.Serialization;

namespace AgencyOS.Reviewer.Surface;

/// <summary>What kind of thing a surface is.</summary>
public enum SurfaceKind
{
    /// <summary>A navigation destination in the shell.</summary>
    Workspace,

    /// <summary>A XAML page hosted in the content frame.</summary>
    Page,

    /// <summary>A modal content dialog.</summary>
    Dialog,

    /// <summary>An entry in the command registry.</summary>
    Command,

    /// <summary>A deep-link route the activation router understands.</summary>
    DeepLink,

    /// <summary>A shell-level overlay such as the palette or global search.</summary>
    Overlay,

    /// <summary>A path in the published OpenAPI contract.</summary>
    ApiPath,

    /// <summary>A policy or component in the Windows platform assembly.</summary>
    PlatformComponent,
}

/// <summary>
/// One reviewable thing, named once.
/// </summary>
/// <remarks>
/// <para>
/// Every field is either read from source or left null. The inventory never
/// guesses: an unknown required permission is null rather than "none", because
/// the difference between "no permission is needed" and "nobody recorded which
/// permission is needed" is the difference between a fact and a hope.
/// </para>
/// <para>
/// <see cref="SurfaceId"/> is stable across runs so a finding can point at a
/// surface and still mean the same thing after the next audit.
/// </para>
/// </remarks>
public sealed record SurfaceRecord
{
    /// <summary>Stable identifier, unique across the map.</summary>
    public required string SurfaceId { get; init; }

    /// <summary>What kind of surface this is.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SurfaceKind Type { get; init; }

    /// <summary>Human-readable name as the product presents it.</summary>
    public string? Label { get; init; }

    /// <summary>Repository-relative file the surface is declared in.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Workspace tag reached to get here, when there is one.</summary>
    public string? NavigationPath { get; init; }

    /// <summary>Command identifiers associated with this surface.</summary>
    public IReadOnlyList<string> CommandIds { get; init; } = [];

    /// <summary>Keyboard gesture, as the product writes it.</summary>
    public string? Gesture { get; init; }

    /// <summary>Permission the client checks before offering this, when it checks one.</summary>
    public string? RequiredPermission { get; init; }

    /// <summary>API paths this surface is known to call.</summary>
    public IReadOnlyList<string> ApiDependencies { get; init; } = [];

    /// <summary>Deep link that reaches this surface, when one exists.</summary>
    public string? DeepLink { get; init; }

    /// <summary>Dialog types this surface opens.</summary>
    public IReadOnlyList<string> Dialogs { get; init; } = [];

    /// <summary>States the source shows this surface can be in.</summary>
    public IReadOnlyList<string> ExpectedStates { get; init; } = [];

    /// <summary>Test files that mention this surface.</summary>
    public IReadOnlyList<string> TestReferences { get; init; } = [];

    /// <summary>Free-form facts the scanners recorded, for the reviewer to read.</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The whole static inventory, as written to disk.</summary>
/// <param name="GeneratedAtUtc">When the scan ran.</param>
/// <param name="RepositoryCommit">Commit the scan describes.</param>
/// <param name="Surfaces">Every surface found.</param>
public sealed record SurfaceMap(
    DateTimeOffset GeneratedAtUtc,
    string RepositoryCommit,
    IReadOnlyList<SurfaceRecord> Surfaces);
