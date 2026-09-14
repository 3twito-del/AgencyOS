using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That the accessibility checks fire, and fire on the right things.
/// </summary>
/// <remarks>
/// <para>
/// Three of these checks reported zero on every surface of Audit 001 because a
/// pattern name was trimmed with the wrong suffix and no control ever matched.
/// The audit read those zeroes as findings of nothing. They were findings of a
/// check that could not run.
/// </para>
/// <para>
/// Every test here builds a tree by hand. A check that is only ever exercised
/// against a real window is a check whose silence nobody can distinguish from
/// good news — which is precisely how the original defect survived a whole audit.
/// </para>
/// </remarks>
public sealed class AccessibilityCheckTests
{
    /// <summary>An unnamed button that can be pressed is reported.</summary>
    /// <remarks>
    /// The test that would have failed in Run 001 and did not.
    /// </remarks>
    [Fact]
    public void AnUnnamedActionableControlIsReported()
    {
        UiaNode tree = Window(Node("Button", name: null, patterns: ["Invoke"], focusable: true));

        AccessibilityObservation observation = Assert.Single(
            AuditPass.Accessibility(tree),
            x => x.Kind == "actionable-control-without-accessible-name");

        Assert.Contains("Invoke", observation.Detail, StringComparison.Ordinal);
    }

    /// <summary>A named button is not reported.</summary>
    [Fact]
    public void ANamedActionableControlIsNotReported() =>
        Assert.Empty(AuditPass.Accessibility(
            Window(Node("Button", "Record an offer", ["Invoke"], focusable: true))));

    /// <summary>A control the keyboard cannot reach is reported.</summary>
    [Fact]
    public void AnActionableControlOutsideTheTabOrderIsReported() =>
        Assert.Single(
            AuditPass.Accessibility(Window(Node("Button", "Record an offer", ["Invoke"]))),
            x => x.Kind == "actionable-control-not-focusable");

    /// <summary>An input with no label is reported.</summary>
    /// <remarks>
    /// The one check that did work in Run 001, and the one that found
    /// <c>AOS-R001-004</c>. It is here so that fixing the other three cannot
    /// quietly break it.
    /// </remarks>
    [Fact]
    public void AnInputWithNoLabelIsReported() =>
        Assert.Single(
            AuditPass.Accessibility(Window(Node("ComboBox", name: null, focusable: true))),
            x => x.Kind == "input-without-label");

    /// <summary>Two controls that announce the same thing are reported.</summary>
    [Fact]
    public void TwoControlsThatAnnounceAlikeAreReported() =>
        Assert.Single(
            AuditPass.Accessibility(Window(
                Node("Button", "Open", ["Invoke"], focusable: true),
                Node("Button", "Open", ["Invoke"], focusable: true))),
            x => x.Kind == "duplicate-accessible-name");

    /// <summary>
    /// The window frame is not the product's markup.
    /// </summary>
    /// <remarks>
    /// Minimize, Maximize and Close are drawn by the window frame and reached
    /// through the system menu. Counting them added three identical observations
    /// to each of nineteen surfaces and told nobody anything about AgencyOS.
    /// </remarks>
    [Fact]
    public void TheWindowsCaptionButtonsAreNotReviewed()
    {
        UiaNode tree = Window(
            Node("TitleBar", "AgencyOS", children:
            [
                Node("Button", "Minimize", ["Invoke"]),
                Node("Button", "Maximize", ["Invoke"]),
                Node("Button", "Close", ["Invoke"]),
            ]));

        Assert.Empty(AuditPass.Accessibility(tree));
    }

    /// <summary>
    /// A wrapper whose child does the work is not a control anybody is missing.
    /// </summary>
    /// <remarks>
    /// WinUI builds an AutoSuggestBox as an unnamed Group holding a named,
    /// focusable Edit. A screen reader lands on the Edit. Reporting the Group
    /// would be reporting the framework's tree shape.
    /// </remarks>
    [Fact]
    public void AContainerWhoseChildCarriesTheControlIsNotReported()
    {
        UiaNode tree = Window(
            Node("Group", name: null, patterns: ["Invoke"], children:
            [
                Node("Edit", "Title", ["Value"], focusable: true),
            ]));

        Assert.Empty(AuditPass.Accessibility(tree));
    }

    /// <summary>
    /// An empty wrapper is still reported.
    /// </summary>
    /// <remarks>
    /// The exclusion above has to be narrow. A container with nothing focusable
    /// inside it is a control that genuinely cannot be reached, and letting the
    /// shape of the rule swallow that case would trade one silent check for
    /// another.
    /// </remarks>
    [Fact]
    public void AContainerWithNothingReachableInsideItIsStillReported() =>
        Assert.Single(
            AuditPass.Accessibility(Window(
                Node("Group", name: null, patterns: ["Invoke"], children:
                [
                    Node("Text", "Title"),
                ]))),
            x => x.Kind == "actionable-control-not-focusable");

    /// <summary>A list row is not expected to be its own Tab stop.</summary>
    [Fact]
    public void AListRowIsNotExpectedToTakeATabStop() =>
        Assert.Empty(AuditPass.Accessibility(Window(
            Node("ListItem", "Autumn slate — lead role, A24, Negotiating", ["SelectionItem"]))));

    private static UiaNode Window(params UiaNode[] children) =>
        Node("Window", "AgencyOS", children: children);

    private static UiaNode Node(
        string controlType,
        string? name = null,
        IReadOnlyList<string>? patterns = null,
        bool focusable = false,
        IReadOnlyList<UiaNode>? children = null) =>
        new(
            AutomationId: null,
            Name: name,
            ControlType: controlType,
            ClassName: controlType,
            IsEnabled: true,
            IsOffscreen: false,
            IsKeyboardFocusable: focusable,
            HasKeyboardFocus: false,
            AcceleratorKey: null,
            AccessKey: null,
            HelpText: null,
            Bounds: "0,0,100,40",
            Patterns: patterns ?? [],
            Value: null,
            Children: children ?? []);
}
