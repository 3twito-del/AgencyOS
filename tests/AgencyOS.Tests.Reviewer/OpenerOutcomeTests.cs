using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That the harness can tell "nothing happened" from every legitimate outcome
/// that also fails to open a dialog.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 could say only that a dialog did not appear, and a dialog does not
/// appear for several unrelated reasons: the opener is disabled, the opener is not
/// there, the product refused with a notice, the product failed with an error — or
/// the product ran the command and said nothing at all. Only the last is
/// <c>AOS-R002-019</c>, and collapsing them into one verdict is what let four
/// broken openers sit behind the same word as a correct refusal.
/// </para>
/// <para>
/// So the verdict is a vocabulary, and this is its positive and negative control.
/// Hand-built trees rather than captures, for the reason
/// <c>DetectorControlTests</c> gives: a captured tree proves what one window
/// happened to contain, and a control has to prove what the rule does.
/// </para>
/// </remarks>
public sealed class OpenerOutcomeTests
{
    /// <summary>A dialog arrived. Nothing else about the page matters.</summary>
    [Fact]
    public void ADialogThatAppearedIsOpened() =>
        Assert.Equal(
            "OPENED",
            OpenerProbe.Classify(
                invoked: true,
                control: "Connect a mailbox",
                before: Page(),
                dialog: Popup(),
                noticesBefore: [],
                noticesAfter: []));

    /// <summary>
    /// A notice that was not there before is the product answering.
    /// </summary>
    [Fact]
    public void ANoticeThatAppearedIsARefusalWithFeedback() =>
        Assert.Equal(
            "REFUSED_WITH_FEEDBACK",
            OpenerProbe.Classify(
                invoked: true,
                control: null,
                before: Page(),
                dialog: null,
                noticesBefore: [],
                noticesAfter: ["No provider is configured — register the application first"]));

    /// <summary>An error the operator can read is a failure, and a visible one.</summary>
    /// <remarks>
    /// Keyed on the title every AgencyOS page gives a failure, because UI
    /// Automation does not expose an <c>InfoBar</c>'s severity and the only
    /// severity signal in the tree is an icon named in the shell's display
    /// language.
    /// </remarks>
    [Fact]
    public void AnErrorThatAppearedIsAVisibleFailure() =>
        Assert.Equal(
            "FAILED_WITH_VISIBLE_ERROR",
            OpenerProbe.Classify(
                invoked: true,
                control: null,
                before: Page(),
                dialog: null,
                noticesBefore: [],
                noticesAfter: ["That did not happen — the server refused it"]));

    /// <summary>
    /// A notice that was already open is not an answer to this invocation.
    /// </summary>
    /// <remarks>
    /// The distinction the whole finding turns on. A page that always shows an
    /// informational bar would otherwise make every opener look like it refused.
    /// </remarks>
    [Fact]
    public void ANoticeThatWasAlreadyThereIsNotAnAnswer() =>
        Assert.Equal(
            "INVOKED_NO_OBSERVABLE_OUTCOME",
            OpenerProbe.Classify(
                invoked: true,
                control: "Connect a mailbox",
                before: Page(),
                dialog: null,
                noticesBefore: ["A mailbox is somebody's correspondence — visibility decides"],
                noticesAfter: ["A mailbox is somebody's correspondence — visibility decides"]));

    /// <summary>The state AOS-R002-019 was: it ran, and said nothing.</summary>
    [Fact]
    public void AnOpenerThatRanAndSaidNothingIsTheDefect() =>
        Assert.Equal(
            "INVOKED_NO_OBSERVABLE_OUTCOME",
            OpenerProbe.Classify(
                invoked: true,
                control: "Connect a mailbox",
                before: Page(),
                dialog: null,
                noticesBefore: [],
                noticesAfter: []));

    /// <summary>A control that is present and off is not a product defect.</summary>
    [Fact]
    public void AControlThatIsPresentAndDisabledIsDisabled() =>
        Assert.Equal(
            "DISABLED",
            OpenerProbe.Classify(
                invoked: false,
                control: "Connect a mailbox",
                before: Page(Button("Connect a mailbox", enabled: false)),
                dialog: null,
                noticesBefore: [],
                noticesAfter: []));

    /// <summary>A control that is not in the tree is a different answer again.</summary>
    [Fact]
    public void AControlThatIsNotThereIsAMissingOpener() =>
        Assert.Equal(
            "MISSING_OPENER",
            OpenerProbe.Classify(
                invoked: false,
                control: "Connect a mailbox",
                before: Page(),
                dialog: null,
                noticesBefore: [],
                noticesAfter: []));

    /// <summary>A palette command that never ran is not evidence about the product.</summary>
    [Fact]
    public void APaletteCommandThatNeverRanIsAMissingOpener() =>
        Assert.Equal(
            "MISSING_OPENER",
            OpenerProbe.Classify(
                invoked: false,
                control: null,
                before: Page(),
                dialog: null,
                noticesBefore: [],
                noticesAfter: []));

    /// <summary>
    /// A notice is read the way the running tree actually presents one.
    /// </summary>
    /// <remarks>
    /// The shape is measured, not assumed: an open <c>InfoBar</c> arrives as a
    /// <c>StatusBar</c> whose class name is fully qualified and which has no
    /// accessible name of its own, with the title and message as child text
    /// elements. Reading <c>Name</c> off a control whose class name was matched
    /// short found nothing at all, on any page, which would have reported every
    /// legitimate refusal as the silence this probe exists to catch.
    /// </remarks>
    [Fact]
    public void ANoticeIsReadAsTheTreePresentsIt()
    {
        string notice = Assert.Single(OpenerProbe.Notices(Page(InfoBar(
            "No provider is configured",
            "An administrator has to register the application with the mail provider."))));

        Assert.Equal(
            "No provider is configured — An administrator has to register "
                + "the application with the mail provider.",
            notice);
    }

    /// <summary>The short class name is accepted too, for a tree that reports one.</summary>
    [Fact]
    public void AShortClassNameIsAlsoANotice() =>
        Assert.Single(OpenerProbe.Notices(
            Page(Node("StatusBar", className: "InfoBar", children:
                [Node("Text", "No provider is configured", automationId: "Title")]))));

    /// <summary>A notice with no message is still a notice.</summary>
    [Fact]
    public void ANoticeWithNoMessageIsStillRead() =>
        Assert.Equal(
            "No provider is configured",
            Assert.Single(OpenerProbe.Notices(Page(InfoBar("No provider is configured", null)))));

    /// <summary>A closed notice is not in the tree, and is not counted.</summary>
    [Fact]
    public void AnOffscreenNoticeIsNotCounted() =>
        Assert.Empty(OpenerProbe.Notices(
            Page(InfoBar("No provider is configured", "…", offscreen: true))));

    /// <summary>Nothing that is not a notice is counted as one.</summary>
    [Fact]
    public void AnOrdinaryStatusBarIsNotANotice() =>
        Assert.Empty(OpenerProbe.Notices(
            Page(Node("StatusBar", "Ready", className: "Microsoft.UI.Xaml.Controls.StatusBar"))));

    // ------------------------------------------------------------ hand-built

    private static UiaNode Page(params UiaNode[] children) =>
        Node("Window", "AgencyOS", children: children);

    private static UiaNode Popup() =>
        Node("Window", "Connect a mailbox", className: "Popup");

    private static UiaNode Button(string name, bool enabled) =>
        Node("Button", name, enabled: enabled);

    /// <summary>An open InfoBar, in the shape the running tree reports one.</summary>
    private static UiaNode InfoBar(string title, string? message, bool offscreen = false) =>
        Node(
            "StatusBar",
            className: "Microsoft.UI.Xaml.Controls.InfoBar",
            offscreen: offscreen,
            children:
            [
                Node("Image", "סמל", automationId: "StandardIcon"),
                Node("Text", title, automationId: "Title"),
                .. message is null ? [] : new[] { Node("Text", message, automationId: "Message") },
            ]);

    private static UiaNode Node(
        string controlType,
        string? name = null,
        string? className = null,
        bool enabled = true,
        bool offscreen = false,
        string? automationId = null,
        IReadOnlyList<UiaNode>? children = null) =>
        new(
            AutomationId: automationId,
            Name: name,
            ControlType: controlType,
            ClassName: className ?? controlType,
            IsEnabled: enabled,
            IsOffscreen: offscreen,
            IsKeyboardFocusable: false,
            HasKeyboardFocus: false,
            AcceleratorKey: null,
            AccessKey: null,
            HelpText: null,
            Bounds: "0,0,100,40",
            Patterns: [],
            Value: null,
            Children: children ?? []);
}
