namespace AgencyOS.Client.Commands;

/// <summary>
/// One workspace in the shell: a navigation destination with a stable name.
/// </summary>
/// <param name="Tag">
/// The stable identifier. Never renamed and never positional — this is what
/// replaced the menu index that let <c>Ctrl+9</c> change meaning (ADR-0032).
/// </param>
/// <param name="Label">What the navigation item says.</param>
/// <param name="AccessKey">
/// The Alt-key mnemonic. Distinct from a command gesture: access keys are a
/// Windows affordance over the menu, and the registry owns accelerators.
/// </param>
public sealed record AgencyOsWorkspace(string Tag, string Label, string AccessKey);

/// <summary>
/// Every workspace the shell can show.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than only in XAML so that navigation is data both the
/// command registry and the window read from. A workspace with no navigation
/// command, or a command naming a workspace that does not exist, now fails a test
/// instead of failing silently at the keyboard.
/// </para>
/// <para>
/// Order matches the navigation pane, and is presentation only. Nothing addresses
/// a workspace by its position in this list.
/// </para>
/// </remarks>
public static class AgencyOsWorkspaces
{
    /// <summary>Every workspace, in the order the navigation pane shows them.</summary>
    public static IReadOnlyList<AgencyOsWorkspace> All { get; } =
    [
        new("command-center", "Command Center", "1"),
        new("people", "People", "2"),
        new("companies", "Companies", "3"),
        new("talent", "Talent", "4"),
        new("prospects", "Prospects", "5"),
        new("projects", "Projects", "6"),
        new("packages", "Packages", "7"),
        new("pipeline", "Pipeline", "8"),
        new("deals", "Deals", "0"),
        new("contracts", "Contracts", "C"),
        new("finance", "Finance", "F"),
        new("documents", "Documents", "D"),
        new("communications", "Communications", "E"),
        new("intelligence", "Intelligence", "I"),
        new("ai", "AI", "A"),
        new("saved-views", "Saved Views", "9"),
        new("sync", "Sync and Offline", "S"),
    ];

    /// <summary>Every workspace tag.</summary>
    public static IReadOnlySet<string> Tags { get; } =
        All.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);

    /// <summary>Looks a workspace up by tag, or null when it does not exist.</summary>
    public static AgencyOsWorkspace? Find(string tag) =>
        All.FirstOrDefault(x => string.Equals(x.Tag, tag, StringComparison.Ordinal));
}
