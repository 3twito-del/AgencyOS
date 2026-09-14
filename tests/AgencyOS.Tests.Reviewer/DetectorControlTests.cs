using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// A positive and a negative control for every corrected detector.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001 reported five accessibility checks and one clipping check across
/// seventeen workspaces. Three of the accessibility checks and the clipping check
/// asked whether a control supported a UI Automation pattern, and the harness
/// trimmed the pattern's name with the wrong suffix, so the answer was always no.
/// Every one of those zeroes was read as a finding of nothing. They were the sound
/// of a check that could not run.
/// </para>
/// <para>
/// So no detector in this harness is trusted until it has been shown to fire on a
/// sample known to be bad and stay silent on one known to be good. These are the
/// samples. They are hand-built trees, not captures, because a captured tree only
/// proves what that window happened to contain — and Audit 001R's whole purpose is
/// to stop treating an absence of output as an answer.
/// </para>
/// </remarks>
public sealed class DetectorControlTests
{
    // ================================================== accessible-name check

    /// <summary>Positive: a button nobody can hear about.</summary>
    [Fact]
    public void AMissingAccessibleNameIsDetected()
    {
        AccessibilityObservation observation = Assert.Single(
            Detectors.Accessibility(Window(Node("Button", patterns: ["Invoke"], focusable: true))),
            x => x.Kind == "actionable-control-without-accessible-name");

        Assert.Contains("Invoke", observation.Detail, StringComparison.Ordinal);
    }

    /// <summary>Negative: the same button, named.</summary>
    [Fact]
    public void ANamedControlIsNotDetected() =>
        Assert.Empty(Detectors.Accessibility(
            Window(Node("Button", "Record an offer", ["Invoke"], focusable: true))));

    // ================================================ keyboard-reachable check

    /// <summary>Positive: invokable by mouse, invisible to the Tab key.</summary>
    [Fact]
    public void AControlOutsideTheTabOrderIsDetected() =>
        Assert.Single(
            Detectors.Accessibility(Window(Node("Button", "Record an offer", ["Invoke"]))),
            x => x.Kind == "actionable-control-not-focusable");

    /// <summary>Negative: a list row is not expected to be its own Tab stop.</summary>
    [Fact]
    public void AListRowIsNotExpectedToTakeATabStop() =>
        Assert.Empty(Detectors.Accessibility(
            Window(Node("ListItem", "Autumn slate, A24, Negotiating", ["SelectionItem"]))));

    // ============================================== duplicate-name check (3rd)

    /// <summary>
    /// Positive: two buttons a screen-reader user cannot tell apart.
    /// </summary>
    /// <remarks>
    /// This is the third detector the suffix defect disabled. It asked
    /// <c>Patterns.Contains("Invoke")</c>, which no control ever satisfied, so it
    /// reported nothing on every surface of Audit 001.
    /// </remarks>
    [Fact]
    public void TwoControlsThatAnnounceAlikeAreDetected()
    {
        AccessibilityObservation observation = Assert.Single(
            Detectors.Accessibility(Window(
                Node("Button", "Open", ["Invoke"], focusable: true),
                Node("Button", "Open", ["Invoke"], focusable: true))),
            x => x.Kind == "duplicate-accessible-name");

        Assert.Equal("Open", observation.Control);
    }

    /// <summary>Negative: two buttons that say what they each do.</summary>
    [Fact]
    public void TwoDistinctlyNamedControlsAreNotDetected() =>
        Assert.Empty(Detectors.Accessibility(Window(
            Node("Button", "Open deal", ["Invoke"], focusable: true),
            Node("Button", "Open contract", ["Invoke"], focusable: true))));

    // ================================================ decorative-focus check

    /// <summary>Positive: a caption that takes a Tab stop and does nothing.</summary>
    [Fact]
    public void AFocusableDecorativeElementIsDetected() =>
        Assert.Single(
            Detectors.Accessibility(Window(Node("Text", "Waiting to send", focusable: true))),
            x => x.Kind == "decorative-element-is-focusable");

    /// <summary>Negative: ordinary caption text.</summary>
    [Fact]
    public void OrdinaryTextIsNotDetected() =>
        Assert.Empty(Detectors.Accessibility(Window(Node("Text", "Waiting to send"))));

    // ===================================================== input-label check

    /// <summary>Positive: a filter nobody can identify.</summary>
    /// <remarks>
    /// The one accessibility check that worked in Audit 001, and the one that
    /// found <c>AOS-R001-004</c>. It is controlled here so that correcting the
    /// other three cannot quietly break it.
    /// </remarks>
    [Fact]
    public void AnUnlabelledInputIsDetected() =>
        Assert.Single(
            Detectors.Accessibility(Window(Node("ComboBox", focusable: true))),
            x => x.Kind == "input-without-label");

    /// <summary>Negative: the same filter with a header.</summary>
    [Fact]
    public void ALabelledInputIsNotDetected() =>
        Assert.Empty(Detectors.Accessibility(Window(Node("ComboBox", "Status", focusable: true))));

    // ====================================================== row-speech check

    /// <summary>
    /// Positive: a row reciting its own record.
    /// </summary>
    /// <remarks>
    /// The detector Audit 001 did not have at all. <c>AOS-R001-003</c> was found by
    /// reading captured trees by hand, which is why it could not be re-run and
    /// could not visibly regress. The sample is the real pre-repair name, shortened.
    /// </remarks>
    [Fact]
    public void ARowRecitingItsRecordIsDetected()
    {
        RowSpeechObservation observation = Assert.Single(
            Detectors.RowSpeech(Window(Node(
                "ListItem",
                "DealSummaryResponse { Id = 01a0a015-d0bc-709c-8291-d7f12136a6d6, "
                    + "Name = Autumn slate, Kind = TalentEmployment }"))),
            x => x.Kind == "row-announces-its-record");

        Assert.Equal(1, observation.Identifiers);
    }

    /// <summary>Positive: a row that says an identifier out loud.</summary>
    [Fact]
    public void ARowSayingAnIdentifierIsDetected() =>
        Assert.Single(
            Detectors.RowSpeech(Window(Node(
                "ListItem", "Autumn slate 01a0a015-d0bc-709c-8291-d7f12136a6d6"))),
            x => x.Kind == "row-announces-an-identifier");

    /// <summary>Positive: a row nobody can wait through.</summary>
    [Fact]
    public void ARowThatSaysTooMuchIsDetected() =>
        Assert.Single(
            Detectors.RowSpeech(Window(Node("ListItem", new string('a', 240)))),
            x => x.Kind == "row-announces-too-much");

    /// <summary>
    /// Positive: a row spelling a value the way the code does.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-012</c>. The filter above the list offers "Talent employment";
    /// the row read <c>TalentEmployment</c>.
    /// </remarks>
    [Fact]
    public void ARowSpellingADomainTokenIsDetected() =>
        Assert.Single(
            Detectors.RowSpeech(Window(Node("ListItem", "Autumn slate, TalentEmployment"))),
            x => x.Kind == "row-announces-a-domain-token");

    /// <summary>Negative: the row as it reads after Repair Wave 002.</summary>
    [Fact]
    public void ARowThatSaysWhatItShowsIsNotDetected() =>
        Assert.Empty(Detectors.RowSpeech(Window(Node(
            "ListItem", "Autumn slate — lead role, A24, Negotiating, Talent employment"))));

    /// <summary>Negative: a row is not a problem for having a short name.</summary>
    [Theory]
    [InlineData("Priya Raghunathan, Pinewood Streaming Group, Active")]
    [InlineData("brief-writer")]
    [InlineData("Sent materials — recorded, not sent, Email")]
    public void AnOrdinaryRowIsNotDetected(string spoken) =>
        Assert.Empty(Detectors.RowSpeech(Window(Node("ListItem", spoken))));

    /// <summary>A control that is not a row is not judged as one.</summary>
    [Fact]
    public void OnlyRowsAreJudgedAsRows() =>
        Assert.Empty(Detectors.RowSpeech(Window(Node("Text", "TalentEmployment"))));

    // ======================================================= clipping checks

    private static readonly Rectangle Viewport = new(41, 41, 900, 701);

    /// <summary>
    /// Positive: a control laid out past the window edge that will not come back.
    /// </summary>
    /// <remarks>
    /// The shape of <c>AOS-R001-007</c>: the detail pane at 900x700, with a
    /// rectangle that is simply not on the window and nothing that scrolls it
    /// there.
    /// </remarks>
    [Fact]
    public void AControlPastTheWindowEdgeIsDetected() =>
        Assert.Equal(
            ReachabilityVerdict.ClippedUnreachable,
            Detectors.Reachability(
                Rectangle.Parse("1000,300,240,60"), false, Viewport, scrollable: false, arrived: null));

    /// <summary>
    /// Positive: a control beyond a scrollable region that stays beyond it.
    /// </summary>
    /// <remarks>
    /// Scrolling was offered and the control still did not arrive on the window.
    /// That is unreachable, not merely scrolled away.
    /// </remarks>
    [Fact]
    public void AControlBeyondAScrollRegionThatWillNotComeBackIsDetected() =>
        Assert.Equal(
            ReachabilityVerdict.ClippedUnreachable,
            Detectors.Reachability(
                null, true, Viewport, scrollable: true, arrived: Rectangle.Parse("1400,300,240,60")));

    /// <summary>
    /// Negative: off screen, and reachable by scrolling.
    /// </summary>
    /// <remarks>
    /// The distinction the whole layout pass turns on. A naive detector that
    /// counted everything off screen would report this as a defect, and a page of
    /// false defects is no better than a check that cannot fire.
    /// </remarks>
    [Fact]
    public void AControlThatScrollsIntoViewIsNotDetected() =>
        Assert.Equal(
            ReachabilityVerdict.ScrollReachable,
            Detectors.Reachability(
                null, true, Viewport, scrollable: true, arrived: Rectangle.Parse("500,300,240,60")));

    /// <summary>Negative: a control plainly on the window.</summary>
    [Fact]
    public void AControlOnTheWindowIsNotDetected() =>
        Assert.Equal(
            ReachabilityVerdict.Reachable,
            Detectors.Reachability(
                Rectangle.Parse("500,300,240,60"), false, Viewport, scrollable: false, arrived: null));

    /// <summary>
    /// A control with a rectangle on the window that the tree calls hidden is not
    /// declared reachable.
    /// </summary>
    /// <remarks>
    /// A collapsed panel keeps its last rectangle. Trusting geometry alone would
    /// report a hidden control as visible.
    /// </remarks>
    [Fact]
    public void AHiddenControlIsNotCalledReachableOnGeometryAlone() =>
        Assert.NotEqual(
            ReachabilityVerdict.Reachable,
            Detectors.Reachability(
                Rectangle.Parse("500,300,240,60"), true, Viewport, scrollable: false, arrived: null));

    /// <summary>What cannot be established is said to be inconclusive.</summary>
    /// <remarks>
    /// A control that offers to scroll and never reports a rectangle has not been
    /// shown to be reachable and has not been shown to be lost. Recording that as
    /// either would be inventing an answer.
    /// </remarks>
    [Fact]
    public void AnUnanswerableCaseIsInconclusive() =>
        Assert.Equal(
            ReachabilityVerdict.Inconclusive,
            Detectors.Reachability(null, true, Viewport, scrollable: true, arrived: null));

    // ============================================================== scoping

    /// <summary>The window frame is not the product's markup.</summary>
    [Fact]
    public void TheWindowsCaptionButtonsAreNotReviewed() =>
        Assert.Empty(Detectors.Accessibility(Window(
            Node("TitleBar", "AgencyOS", children:
            [
                Node("Button", "Minimize", ["Invoke"]),
                Node("Button", "Maximize", ["Invoke"]),
                Node("Button", "Close", ["Invoke"]),
            ]))));

    /// <summary>A wrapper whose child does the work is not a missing control.</summary>
    /// <remarks>
    /// WinUI builds an AutoSuggestBox as an unnamed Group holding a named,
    /// focusable Edit. A screen reader lands on the Edit.
    /// </remarks>
    [Fact]
    public void AContainerWhoseChildCarriesTheControlIsNotDetected() =>
        Assert.Empty(Detectors.Accessibility(Window(
            Node("Group", patterns: ["Invoke"], children:
            [
                Node("Edit", "Title", ["Value"], focusable: true),
            ]))));

    /// <summary>
    /// Negative control for the exclusion above.
    /// </summary>
    /// <remarks>
    /// An exclusion that swallows the real case trades one silent check for
    /// another. A container with nothing reachable inside it is still reported.
    /// </remarks>
    [Fact]
    public void AContainerWithNothingReachableInsideItIsStillDetected() =>
        Assert.Single(
            Detectors.Accessibility(Window(
                Node("Group", patterns: ["Invoke"], children: [Node("Text", "Title")]))),
            x => x.Kind == "actionable-control-not-focusable");

    // =========================================================== the defect

    /// <summary>
    /// The trimming that disabled three detectors and one clipping check.
    /// </summary>
    /// <remarks>
    /// The exact strings UI Automation reports. Audit 001 removed
    /// <c>"Pattern.Pattern"</c>, which never appears in any of them.
    /// </remarks>
    [Theory]
    [InlineData("InvokePatternIdentifiers.Pattern", "Invoke")]
    [InlineData("TogglePatternIdentifiers.Pattern", "Toggle")]
    [InlineData("ValuePatternIdentifiers.Pattern", "Value")]
    [InlineData("SelectionItemPatternIdentifiers.Pattern", "SelectionItem")]
    [InlineData("ExpandCollapsePatternIdentifiers.Pattern", "ExpandCollapse")]
    [InlineData("ScrollItemPatternIdentifiers.Pattern", "ScrollItem")]
    public void APatternIsNamedTheWayTheDetectorsAskForIt(string reported, string expected) =>
        Assert.Equal(expected, UiaTree.ShortName(reported));

    /// <summary>
    /// A tree built the way Audit 001's harness would have recorded it.
    /// </summary>
    /// <remarks>
    /// This is the regression test that matters most: with the untrimmed names,
    /// the detectors that depend on patterns go silent, exactly as they did for a
    /// whole audit. If the trimming breaks again, this fails.
    /// </remarks>
    [Fact]
    public void TheOriginalDefectWouldHaveSilencedTheseDetectors()
    {
        UiaNode broken = Window(Node(
            "Button", patterns: ["InvokePatternIdentifiers.Pattern"], focusable: false));

        Assert.Empty(Detectors.Accessibility(broken));

        UiaNode corrected = Window(Node("Button", patterns: ["Invoke"], focusable: false));

        Assert.Equal(2, Detectors.Accessibility(corrected).Count);
    }

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
