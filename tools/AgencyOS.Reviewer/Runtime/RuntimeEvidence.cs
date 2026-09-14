namespace AgencyOS.Reviewer.Runtime;

/// <summary>One accessibility problem seen in a running tree.</summary>
/// <param name="Kind">Short slug naming the problem.</param>
/// <param name="Control">Which control, as the tree describes it.</param>
/// <param name="Detail">What is wrong.</param>
public sealed record AccessibilityObservation(string Kind, string Control, string Detail);

/// <summary>One stop on a Tab traversal.</summary>
/// <param name="Ordinal">How many Tabs had been pressed.</param>
/// <param name="Control">Where focus landed.</param>
/// <param name="Bounds">Its screen rectangle, or null when it has none.</param>
/// <param name="Offscreen">Whether the focused control was off screen.</param>
public sealed record FocusStop(int Ordinal, string Control, string? Bounds, bool Offscreen);

/// <summary>What a keyboard pass over one surface found.</summary>
/// <param name="Stops">Every focus stop, in order.</param>
/// <param name="DistinctStops">How many different controls focus reached.</param>
/// <param name="Trapped">Whether focus stopped changing before the cycle closed.</param>
/// <param name="OffscreenStops">Focus stops that were not visible.</param>
/// <param name="UnnamedStops">Focus stops with no accessible name.</param>
public sealed record KeyboardPass(
    IReadOnlyList<FocusStop> Stops,
    int DistinctStops,
    bool Trapped,
    int OffscreenStops,
    int UnnamedStops);

/// <summary>What pressing one declared gesture actually did.</summary>
/// <param name="CommandId">The command the registry says owns the gesture.</param>
/// <param name="Gesture">The gesture, as the palette writes it.</param>
/// <param name="ExpectedWorkspace">Where it should have gone.</param>
/// <param name="ObservedSelection">Which navigation item was selected afterwards.</param>
/// <param name="Sent">Whether the keystroke was delivered at all.</param>
/// <param name="Matched">Whether observation matched expectation.</param>
public sealed record GestureProbe(
    string CommandId,
    string Gesture,
    string? ExpectedWorkspace,
    string? ObservedSelection,
    bool Sent,
    bool Matched);

/// <summary>Everything observed on one surface at run time.</summary>
/// <param name="SurfaceId">Which surface.</param>
/// <param name="Visited">Whether the harness got there.</param>
/// <param name="Detail">What happened, when it did not.</param>
/// <param name="Screenshots">Captured evidence, relative to the run directory.</param>
/// <param name="TreePath">Where the automation snapshot was written.</param>
/// <param name="ControlCount">How many automation elements the surface produced.</param>
/// <param name="InteractiveCount">How many of them a user can operate.</param>
/// <param name="VisibleText">Distinct text the surface showed, for the copy pass.</param>
/// <param name="OpenNotices">Notices that were open when observed.</param>
/// <param name="Accessibility">Accessibility problems found in the tree.</param>
/// <param name="Keyboard">The keyboard pass, when one ran.</param>
/// <param name="ClippedControls">Controls whose bounds fell outside the window.</param>
public sealed record SurfaceEvidence(
    string SurfaceId,
    bool Visited,
    string Detail,
    IReadOnlyList<string> Screenshots,
    string? TreePath,
    int ControlCount,
    int InteractiveCount,
    IReadOnlyList<string> VisibleText,
    IReadOnlyList<string> OpenNotices,
    IReadOnlyList<AccessibilityObservation> Accessibility,
    KeyboardPass? Keyboard,
    IReadOnlyList<string> ClippedControls);

/// <summary>The whole runtime pass.</summary>
/// <param name="RunId">Identifier for this audit run.</param>
/// <param name="StartedUtc">When it began.</param>
/// <param name="FinishedUtc">When it ended.</param>
/// <param name="Executable">Which build was driven.</param>
/// <param name="ProcessId">Which process.</param>
/// <param name="WindowSize">The window size every capture was taken at.</param>
/// <param name="Environment">The environment the client was launched with, secrets excluded.</param>
/// <param name="Surfaces">One entry per surface the pass attempted.</param>
/// <param name="Gestures">What every declared global gesture actually did.</param>
/// <param name="Overlays">What the palette and global search did.</param>
/// <param name="RefusedKeystrokes">Sends the harness refused because focus had left the application.</param>
public sealed record RuntimeReport(
    string RunId,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string Executable,
    int ProcessId,
    string WindowSize,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<SurfaceEvidence> Surfaces,
    IReadOnlyList<GestureProbe> Gestures,
    IReadOnlyList<SurfaceEvidence> Overlays,
    int RefusedKeystrokes);
