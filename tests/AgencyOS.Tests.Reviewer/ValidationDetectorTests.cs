using Xunit;
using AgencyOS.Reviewer.Runtime;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// Controls for the two judgements Audit 002 §8 rests on.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001R was called because three detectors had been reporting nothing for
/// a reason that had nothing to do with the product, and the rule that came out
/// of it is that a detector's silence means nothing until the detector has been
/// shown speaking. Both halves are pinned here: input known to contain a
/// complaint, and input known not to.
/// </para>
/// <para>
/// The control strings are the shapes WinUI actually produces. An
/// <c>InfoBar</c> arrives as a group whose name carries its title and message,
/// which is why the prose reader looks at groups as well as text.
/// </para>
/// </remarks>
public sealed class ValidationDetectorTests
{
    private static UiaNode Node(
        string controlType,
        string? name = null,
        bool offscreen = false,
        string? helpText = null,
        params UiaNode[] children) =>
        new(null, name, controlType, null, true, offscreen, false, false,
            null, null, helpText, "0,0,100,20", [], null, children);

    // ------------------------------------------------------------------ Prose

    [Fact]
    public void Prose_ReadsAnInfoBarsComplaint()
    {
        UiaNode dialog = Node("Window", "New person", false, null,
            Node("Group", "Could not record. A last name is required."),
            Node("Edit", "Last name"));

        IReadOnlyList<string> prose = Detectors.Prose([.. dialog.Flatten()]);

        Assert.Contains("Could not record. A last name is required.", prose);
    }

    [Fact]
    public void Prose_IgnoresWhatCannotBeSeen()
    {
        UiaNode dialog = Node("Window", "New person", false, null,
            Node("Group", "A last name is required.", offscreen: true));

        Assert.DoesNotContain("A last name is required.", Detectors.Prose([.. dialog.Flatten()]));
    }

    [Fact]
    public void Prose_IgnoresControlsThatAreNotProse()
    {
        UiaNode dialog = Node("Window", "New person", false, null,
            Node("Button", "Add"),
            Node("Edit", "Last name"));

        Assert.Empty(Detectors.Prose([.. dialog.Children]));
    }

    [Fact]
    public void Prose_IsComparableBetweenTwoReadings()
    {
        UiaNode before = Node("Window", "New person", false, null,
            Node("Text", "Who is this?"));

        UiaNode after = Node("Window", "New person", false, null,
            Node("Text", "Who is this?"),
            Node("Group", "A last name is required."));

        string[] appeared =
        [
            .. Detectors.Prose([.. after.Flatten()])
                .Except(Detectors.Prose([.. before.Flatten()]), StringComparer.Ordinal),
        ];

        // The standing question is not news; the complaint is.
        Assert.Equal(["A last name is required."], appeared);
    }

    [Fact]
    public void Prose_FindsNothingWhenNothingWasSaid()
    {
        UiaNode dialog = Node("Window", "New person", false, null,
            Node("Text", "Who is this?"));

        string[] appeared =
        [
            .. Detectors.Prose([.. dialog.Flatten()])
                .Except(Detectors.Prose([.. dialog.Flatten()]), StringComparer.Ordinal),
        ];

        Assert.Empty(appeared);
    }

    // -------------------------------------------------------------- DialogRoot

    [Fact]
    public void DialogRoot_IgnoresTheEmptyHostEvenWhenItCarriesTheName()
    {
        // The shape that produced AOS-R002-004. Two popups carry the dialog's
        // name; only one carries the dialog. Picking by name picked the other,
        // and an empty subtree makes every containment check fail — so the pass
        // reported that focus had not entered a dialog whose own text box had
        // the focus.
        UiaNode host = Node("Window", "Add a reason");
        UiaNode real = Node("Window", "Add a reason", false, null,
            Node("Edit", "ReasonBox"),
            Node("Button", "Close"));

        Assert.Same(real, Detectors.DialogRoot([host, real], "Add a reason"));
    }

    [Fact]
    public void DialogRoot_PrefersTheRichestCandidateWhenNoNameMatches()
    {
        UiaNode thin = Node("Window", "something else", false, null, Node("Button", "Close"));
        UiaNode rich = Node("Window", "something else", false, null,
            Node("Edit", "LabelBox"), Node("ComboBox", "DirectionBox"), Node("Button", "Close"));

        Assert.Same(rich, Detectors.DialogRoot([thin, rich], "Add a reason"));
    }

    [Fact]
    public void DialogRoot_FindsNothingWhenNoCandidateHoldsAnything()
    {
        Assert.Null(Detectors.DialogRoot([Node("Window", "Add a reason")], "Add a reason"));
    }

    // ---------------------------------------------------------------- Refusal

    [Fact]
    public void Refusal_ReadsTheMessageAndNotOnlyItsFrame()
    {
        // The exact string the server answered the probe with, in the node the
        // page put it in. Requiring a group missed this and reported silence.
        Assert.True(Detectors.Refusal(Node("Text", "firstName must be at most 128 characters.")));
    }

    [Fact]
    public void Refusal_ReadsAnInfoBarsOwnTitle()
    {
        Assert.True(Detectors.Refusal(Node("Group", "That did not happen")));
    }

    [Fact]
    public void Refusal_IsNotEverythingOnThePage()
    {
        Assert.False(Detectors.Refusal(Node("Text", "10 project(s); 2 with a role still to fill.")));
        Assert.False(Detectors.Refusal(Node("Text", "Online - last updated 12 hours ago.")));
    }

    [Fact]
    public void Refusal_IgnoresWhatCannotBeSeen()
    {
        Assert.False(Detectors.Refusal(
            Node("Text", "firstName must be at most 128 characters.", offscreen: true)));
    }

    // ------------------------------------------------------------- Validation

    [Fact]
    public void Validation_ADisabledCommitButtonIsAGate()
    {
        Assert.Equal(
            ValidationVerdict.ManualGate,
            Detectors.Validation(
                primaryEnabledWhenEmpty: false,
                refusalObserved: false,
                accessibleAssociation: false,
                stillOpen: true));
    }

    [Fact]
    public void Validation_ADialogThatClosedOnARefusalCannotBeAssociated()
    {
        Assert.Equal(
            ValidationVerdict.Unassociated,
            Detectors.Validation(
                primaryEnabledWhenEmpty: true,
                refusalObserved: true,
                accessibleAssociation: true,
                stillOpen: false));
    }

    [Fact]
    public void Validation_ADialogThatClosedWithoutARefusalSimplyWorked()
    {
        // ChangeProjectStageDialog. The probe's 607 characters went into a
        // reason, the server answered 204, and the dialog closed because the
        // change had been made. Reading that as a misplaced error message was
        // the first version of this rule and it was wrong.
        Assert.Equal(
            ValidationVerdict.Inconclusive,
            Detectors.Validation(
                primaryEnabledWhenEmpty: true,
                refusalObserved: false,
                accessibleAssociation: false,
                stillOpen: false));
    }

    [Fact]
    public void Validation_AMessageWithoutADeclaredAssociationIsOnlyNearby()
    {
        Assert.Equal(
            ValidationVerdict.VisuallyNearOnly,
            Detectors.Validation(
                primaryEnabledWhenEmpty: true,
                refusalObserved: true,
                accessibleAssociation: false,
                stillOpen: true));
    }

    [Fact]
    public void Validation_AMessageWithADeclaredAssociationIsAssociated()
    {
        Assert.Equal(
            ValidationVerdict.Associated,
            Detectors.Validation(
                primaryEnabledWhenEmpty: true,
                refusalObserved: true,
                accessibleAssociation: true,
                stillOpen: true));
    }

    [Fact]
    public void Validation_SilenceIsNotAVerdict()
    {
        // The dialog stayed, said nothing, and accepted nothing. That is not a
        // finding about association; it is a reading that did not settle.
        Assert.Equal(
            ValidationVerdict.Inconclusive,
            Detectors.Validation(
                primaryEnabledWhenEmpty: true,
                refusalObserved: false,
                accessibleAssociation: false,
                stillOpen: true));
    }
}
