using System.Text.RegularExpressions;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// The checks the audit runs over a running automation tree.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions over <see cref="UiaNode"/>, in one file, so that every one of
/// them can be run against a tree built by hand. That is not tidiness. Audit 001
/// shipped five accessibility checks of which three could never fire — a pattern
/// name was trimmed with the wrong suffix, so every question of the form "does
/// this control support Invoke" answered no — and the audit read their zeroes as
/// findings of nothing.
/// </para>
/// <para>
/// A detector that has only ever been run against a real window is a detector
/// whose silence nobody can tell apart from good news. Each one here has a
/// positive control that must fire it and a negative control that must not, kept
/// as permanent tests in <c>AgencyOS.Tests.Reviewer</c>.
/// </para>
/// </remarks>
internal static class Detectors
{
    /// <summary>The patterns that make a control something a user acts on.</summary>
    private static readonly string[] Actionable =
        ["Invoke", "Toggle", "ExpandCollapse", "SelectionItem", "Value"];

    /// <summary>A row this long has stopped being something anybody listens to.</summary>
    private const int TooMuchToSay = 200;

    private static readonly Regex Identifier = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Two words run together, the way code spells them and people do not.</summary>
    private static readonly Regex DomainToken = new(
        @"\b\p{Lu}\p{Ll}+\p{Lu}\p{Ll}+\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // ------------------------------------------------------- accessibility

    /// <summary>Accessibility problems visible in a running automation tree.</summary>
    /// <param name="tree">The window's automation tree.</param>
    /// <returns>One observation per problem, in tree order.</returns>
    /// <remarks>
    /// Deliberately narrower than the static XAML suite, and complementary to it.
    /// The static suite proves the markup declares a name; this proves the running
    /// control exposes one, which is a different claim — a template, a converter or
    /// a runtime-populated item can lose it.
    /// </remarks>
    internal static IReadOnlyList<AccessibilityObservation> Accessibility(UiaNode tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        List<AccessibilityObservation> observations = [];
        UiaNode[] nodes = [.. Reviewable(tree)];

        foreach (UiaNode node in nodes)
        {
            bool actionable = node.Patterns.Any(x => Actionable.Contains(x, StringComparer.Ordinal));
            bool named = !string.IsNullOrWhiteSpace(node.Name);

            // A container whose focusable child does the work is how the framework
            // builds an AutoSuggestBox, not a control anybody is missing: the Edit
            // inside it is named, focusable, and where a screen reader lands.
            bool delegatesToAChild = node.Children
                .SelectMany(x => x.Flatten())
                .Any(x => x.IsKeyboardFocusable && !string.IsNullOrWhiteSpace(x.Name));

            if (actionable && !named && !delegatesToAChild && node.IsEnabled && !node.IsOffscreen)
            {
                observations.Add(new AccessibilityObservation(
                    "actionable-control-without-accessible-name",
                    node.Describe(),
                    "Supports " + string.Join('/', node.Patterns)
                        + " and exposes no name, so a screen reader announces only its type."));
            }

            if (actionable && !node.IsKeyboardFocusable && !delegatesToAChild
                && node.IsEnabled && !node.IsOffscreen
                && node.ControlType is not ("ListItem" or "DataItem" or "TreeItem" or "MenuItem"))
            {
                observations.Add(new AccessibilityObservation(
                    "actionable-control-not-focusable",
                    node.Describe(),
                    "Can be invoked and cannot be reached by keyboard."));
            }

            if (node.ControlType == "Text" && node.IsKeyboardFocusable && node.Patterns.Count == 0)
            {
                observations.Add(new AccessibilityObservation(
                    "decorative-element-is-focusable",
                    node.Describe(),
                    "A text element with no pattern takes a Tab stop."));
            }

            if (node.ControlType is "Edit" or "ComboBox" && !named && !node.IsOffscreen)
            {
                observations.Add(new AccessibilityObservation(
                    "input-without-label",
                    node.Describe(),
                    "An input with no accessible name cannot be described to its user."));
            }
        }

        // Two controls that announce identically on one surface are two controls a
        // screen-reader user cannot tell apart.
        foreach (IGrouping<string, UiaNode> group in nodes
            .Where(x => !string.IsNullOrWhiteSpace(x.Name)
                && x.Patterns.Contains("Invoke", StringComparer.Ordinal)
                && !x.IsOffscreen)
            .GroupBy(x => x.Name!, StringComparer.Ordinal)
            .Where(x => x.Count() > 1))
        {
            observations.Add(new AccessibilityObservation(
                "duplicate-accessible-name",
                group.Key,
                group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " invokable controls on this surface announce the same name."));
        }

        return observations;
    }

    // ---------------------------------------------------------- row speech

    /// <summary>
    /// What a list row would be read out as.
    /// </summary>
    /// <param name="tree">The window's automation tree.</param>
    /// <returns>One observation per row that says the wrong thing.</returns>
    /// <remarks>
    /// <para>
    /// The detector Audit 001 did not have. <c>AOS-R001-003</c> and
    /// <c>AOS-R001-012</c> were both found by reading captured trees by hand, which
    /// is why neither could be re-run and neither could regress visibly. This makes
    /// them a check.
    /// </para>
    /// <para>
    /// Three separate faults, reported separately because their repairs differ: a
    /// row reciting its record, a row saying an internal identifier out loud, and a
    /// row spelling a domain value the way the code does rather than the way the
    /// filter above it does.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<RowSpeechObservation> RowSpeech(UiaNode tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        List<RowSpeechObservation> observations = [];

        foreach (UiaNode node in Reviewable(tree))
        {
            if (node.ControlType is not ("ListItem" or "DataItem" or "TreeItem"))
            {
                continue;
            }

            string spoken = node.Name ?? string.Empty;

            if (spoken.Length == 0)
            {
                continue;
            }

            int identifiers = Identifier.Matches(spoken).Count;

            // The shape a C# record's ToString() has: Type { Member = value, … }.
            if (spoken.Contains(" { ", StringComparison.Ordinal)
                && spoken.Contains(" = ", StringComparison.Ordinal))
            {
                observations.Add(new RowSpeechObservation(
                    "row-announces-its-record",
                    node.Describe(),
                    spoken,
                    spoken.Length,
                    identifiers));
            }
            else if (identifiers > 0)
            {
                observations.Add(new RowSpeechObservation(
                    "row-announces-an-identifier",
                    node.Describe(),
                    spoken,
                    spoken.Length,
                    identifiers));
            }
            else if (spoken.Length > TooMuchToSay)
            {
                observations.Add(new RowSpeechObservation(
                    "row-announces-too-much",
                    node.Describe(),
                    spoken,
                    spoken.Length,
                    identifiers));
            }

            if (DomainToken.IsMatch(spoken))
            {
                observations.Add(new RowSpeechObservation(
                    "row-announces-a-domain-token",
                    node.Describe(),
                    spoken,
                    spoken.Length,
                    identifiers));
            }
        }

        return observations;
    }

    // -------------------------------------------------------- reachability

    /// <summary>
    /// Whether a control laid out away from the viewport can still be got to.
    /// </summary>
    /// <param name="bounds">The control's rectangle, or null when it reports none.</param>
    /// <param name="offscreen">What the tree says about its visibility.</param>
    /// <param name="window">The window's rectangle.</param>
    /// <param name="scrollable">Whether the control offers to scroll itself into view.</param>
    /// <param name="arrived">Where it ended up after being asked to, if it was asked.</param>
    /// <returns>The verdict for this control at this window size.</returns>
    /// <remarks>
    /// <para>
    /// "Off screen" and "cannot be reached" are different claims, and only the
    /// second is a defect. A control scrolled out of a viewport reports no
    /// rectangle and a user reaches it by scrolling; a control laid out past the
    /// window edge reports a rectangle that is not on the window, and no amount of
    /// scrolling helps.
    /// </para>
    /// <para>
    /// The distinction is the whole value of this pass. Audit 001's clipping
    /// detector could not fire at all, and a naive replacement that counted
    /// everything off screen would have reported a page of false defects instead.
    /// </para>
    /// </remarks>
    internal static string Reachability(
        Rectangle? bounds,
        bool offscreen,
        Rectangle window,
        bool scrollable,
        Rectangle? arrived)
    {
        if (bounds is { } shown && window.Intersects(shown) && !offscreen)
        {
            return ReachabilityVerdict.Reachable;
        }

        if (arrived is { } landed)
        {
            return window.Intersects(landed)
                ? ReachabilityVerdict.ScrollReachable
                : ReachabilityVerdict.ClippedUnreachable;
        }

        // Nothing scrolled it and nothing could: a rectangle off the window with no
        // way to bring it back is the defect AOS-R001-007 describes.
        return scrollable ? ReachabilityVerdict.Inconclusive : ReachabilityVerdict.ClippedUnreachable;
    }

    /// <summary>
    /// The part of the tree the product is answerable for.
    /// </summary>
    /// <remarks>
    /// The window's caption buttons — Minimize, Maximize, Close — are drawn by the
    /// window frame, not by any markup in this repository, and Windows reaches them
    /// through the system menu rather than through the Tab order. Reporting them
    /// produced three identical observations on every surface and said nothing
    /// about AgencyOS.
    /// </remarks>
    /// <summary>Whether a node is the news that something was refused.</summary>
    /// <param name="node">A node from the tree.</param>
    /// <returns><see langword="true"/> when it is carrying a refusal.</returns>
    /// <remarks>
    /// An <c>InfoBar</c> reaches the tree as a group whose message is a text node
    /// beneath it, so both are read. Requiring the group alone missed the message
    /// and reported a page that was quoting the server as a page that had said
    /// nothing at all.
    /// </remarks>
    internal static bool Refusal(UiaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.ControlType is not "Group" and not "Text"
            || node.IsOffscreen
            || node.Name is not { } name)
        {
            return false;
        }

        // The words the product and the domain actually use when refusing.
        return name.Contains("did not happen", StringComparison.OrdinalIgnoreCase)
            || name.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || name.Contains(" must ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("must not", StringComparison.OrdinalIgnoreCase)
            || name.Contains("at most", StringComparison.OrdinalIgnoreCase)
            || name.Contains("at least", StringComparison.OrdinalIgnoreCase)
            || name.Contains("is required", StringComparison.OrdinalIgnoreCase)
            || name.Contains("already", StringComparison.OrdinalIgnoreCase)
            || name.Contains("not allowed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Picks the node that is actually the dialog.</summary>
    /// <param name="candidates">Every node that could be the dialog's root.</param>
    /// <param name="title">The title the dialog declares, when it declares one.</param>
    /// <returns>The candidate holding the dialog's controls, or none.</returns>
    /// <remarks>
    /// <para>
    /// WinUI hosts a <c>ContentDialog</c> behind popup windows, and more than one
    /// of them can carry the dialog's name while only one carries its content.
    /// Matching on the name alone picked the empty one for four dialogs, and an
    /// empty subtree makes every containment check fail — which is how the pass
    /// came to report that focus had not entered a dialog whose own text box had
    /// the focus.
    /// </para>
    /// <para>
    /// So content decides, and the title only breaks ties between candidates that
    /// have some. A candidate with nothing in it is never the dialog.
    /// </para>
    /// </remarks>
    internal static UiaNode? DialogRoot(IReadOnlyList<UiaNode> candidates, string? title)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Descendants, not interactivity. A dialog whose controls are all
        // disabled - which is most of them at the moment they open, because the
        // commit button waits for the form - still has content, and a host popup
        // has none at all.
        static int Content(UiaNode node) => node.Flatten().Skip(1).Count();

        UiaNode[] withContent = [.. candidates.Where(x => Content(x) > 0)];

        if (withContent.Length == 0)
        {
            return null;
        }

        if (title is { Length: > 0 })
        {
            UiaNode? titled = withContent.FirstOrDefault(x =>
                string.Equals(x.Name, title, StringComparison.Ordinal));

            if (titled is not null)
            {
                return titled;
            }
        }

        // The richest one, because a host popup that happens to hold a single
        // element is still not the dialog.
        return withContent.MaxBy(Content);
    }

    /// <summary>Every piece of prose a dialog is showing.</summary>
    /// <param name="nodes">The dialog's subtree.</param>
    /// <returns>The visible text, sorted so two readings can be compared.</returns>
    /// <remarks>
    /// An <c>InfoBar</c> reaches the automation tree as a group carrying its
    /// title and message, so a complaint and a piece of standing guidance look
    /// alike here. Telling them apart is done by reading twice and subtracting,
    /// not by guessing from the wording.
    /// </remarks>
    internal static IReadOnlyList<string> Prose(IReadOnlyList<UiaNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return
        [
            .. nodes
                .Where(x => x.ControlType is "Text" or "Group"
                    && !string.IsNullOrWhiteSpace(x.Name)
                    && !x.IsOffscreen)
                .Select(x => x.Name!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Where a dialog puts the news that an entry could not be accepted.
    /// </summary>
    /// <param name="primaryEnabledWhenEmpty">
    /// Whether the commit button was available on a form that was not ready.
    /// </param>
    /// <param name="refusalObserved">
    /// Whether a refusal was actually seen. Nothing is classified without one:
    /// a dialog that closed may simply have succeeded.
    /// </param>
    /// <param name="accessibleAssociation">
    /// Whether anything in the dialog declares an association leading to it.
    /// </param>
    /// <param name="stillOpen">Whether the dialog survived the submission.</param>
    /// <returns>One of <see cref="ValidationVerdict"/>.</returns>
    /// <remarks>
    /// Proximity is deliberately not an input. Audit 002 §8 forbids reading an
    /// association off the layout, and the only thing that makes one findable by
    /// an assistive technology is a declaration.
    /// </remarks>
    internal static string Validation(
        bool primaryEnabledWhenEmpty,
        bool refusalObserved,
        bool accessibleAssociation,
        bool stillOpen)
    {
        if (!primaryEnabledWhenEmpty)
        {
            // The form cannot be submitted wrong, so there is no error to place.
            return ValidationVerdict.ManualGate;
        }

        if (!refusalObserved)
        {
            // Either the submission was accepted, or it was refused somewhere
            // nothing could see. Both are readings that did not settle, and
            // neither is a finding about where an error was put.
            return ValidationVerdict.Inconclusive;
        }

        if (!stillOpen)
        {
            // It was refused, and the dialog it was refused in is gone, so the
            // message cannot be beside the entry it is about.
            return ValidationVerdict.Unassociated;
        }

        return accessibleAssociation
            ? ValidationVerdict.Associated
            : ValidationVerdict.VisuallyNearOnly;
    }

    internal static IEnumerable<UiaNode> Reviewable(UiaNode tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        if (tree.ControlType == "TitleBar")
        {
            yield break;
        }

        yield return tree;

        foreach (UiaNode child in tree.Children)
        {
            foreach (UiaNode node in Reviewable(child))
            {
                yield return node;
            }
        }
    }

    /// <summary>Whether a user can operate this control at all.</summary>
    internal static bool Interactive(UiaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsKeyboardFocusable
            || node.Patterns.Any(x => Actionable.Contains(x, StringComparer.Ordinal));
    }

    /// <summary>Whether this control is one a user would try to operate.</summary>
    internal static bool Operable(UiaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !string.IsNullOrWhiteSpace(node.Name)
            && node.Patterns.Any(x => x is "Invoke" or "Value" or "Toggle");
    }
}

/// <summary>The verdicts the layout pass can reach about one control.</summary>
internal static class ReachabilityVerdict
{
    /// <summary>On the window, where a user can see it.</summary>
    internal const string Reachable = "REACHABLE";

    /// <summary>Not on the window, but it came back when asked.</summary>
    internal const string ScrollReachable = "SCROLL_REACHABLE";

    /// <summary>Not on the window, and it would not come back.</summary>
    internal const string ClippedUnreachable = "CLIPPED_UNREACHABLE";

    /// <summary>Behind the shell's own small-window affordance rather than lost.</summary>
    internal const string CompactedByDesign = "COMPACTED_BY_DESIGN";

    /// <summary>The pass could not establish an answer.</summary>
    internal const string Inconclusive = "INCONCLUSIVE";
}
