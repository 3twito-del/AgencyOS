using System.Net;
using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Refusals;

/// <summary>
/// That a refused entry can be corrected where it was made.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-010</c>, decided by the owner: a **recoverable** refusal keeps the
/// dialog, its values and its context, so the operator corrects and retries rather
/// than retyping from nothing. <c>AOS-R002-011</c>: where the refusal is about one
/// field, the message is associated with that field.
/// </para>
/// <para>
/// These execute the decision itself rather than reading source. What the decision
/// cannot cover from here — that WinUI really sets <c>DescribedBy</c>, and that
/// focus really lands on the field — is measured against the running client, in
/// this wave's runtime evidence.
/// </para>
/// </remarks>
public sealed class DialogRefusalTests
{
    private static readonly string[] PersonFields =
        ["firstName", "lastName", "title", "email", "phone", "notes"];

    // ------------------------------------------------------------ NO_ERROR

    /// <summary>Nothing is decided when nothing was refused.</summary>
    /// <remarks>
    /// The control case. A rule that fires on the happy path would close dialogs
    /// that succeeded, and no other test here would notice.
    /// </remarks>
    [Fact]
    public void NoRefusalNamesNoFieldAndKeepsNothingOpen()
    {
        Assert.Null(DialogRefusal.FieldNamed(null, PersonFields));
        Assert.Null(DialogRefusal.FieldNamed(string.Empty, PersonFields));
        Assert.Null(DialogRefusal.FieldNamed("   ", PersonFields));
    }

    // -------------------------------------------------- RECOVERABLE_REFUSAL

    /// <summary>What the operator can fix from where they are.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ARecoverableRefusalKeepsTheDialog(HttpStatusCode status)
    {
        RefusalPresentation shown = DialogRefusal.Present(
            status, "firstName must be at most 128 characters.", "Invalid request", PersonFields);

        Assert.True(shown.KeepsDialogOpen);
    }

    /// <summary>What no amount of editing would fix.</summary>
    /// <remarks>
    /// The boundary the decision draws. A session that is gone, a parent that is
    /// gone, a build the server will not talk to, or a server that failed: holding
    /// the dialog open over one of those would be pretending the operator could do
    /// something about it.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UpgradeRequired)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ATerminalFailureDoesNotKeepTheDialog(HttpStatusCode status)
    {
        RefusalPresentation shown = DialogRefusal.Present(
            status, "The session is no longer valid.", "Unauthorized", PersonFields);

        Assert.False(shown.KeepsDialogOpen);
        Assert.Null(shown.Field);
    }

    // ------------------------------------------------- FIELD_ERROR_VISIBLE

    /// <summary>The operator is shown the server's reason, not the problem's title.</summary>
    [Fact]
    public void TheReasonIsShownRatherThanTheTitle()
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest,
            "firstName must be at most 128 characters.",
            "Invalid request",
            PersonFields);

        Assert.Equal("firstName must be at most 128 characters.", shown.Message);
    }

    /// <summary>A refusal with no detail still says something.</summary>
    [Fact]
    public void ARefusalWithoutADetailFallsBackToItsMessage()
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest, null, "Invalid request", PersonFields);

        Assert.Equal("Invalid request", shown.Message);
        Assert.True(shown.KeepsDialogOpen);
    }

    // ---------------------------------------------- FIELD_ERROR_ASSOCIATED

    /// <summary>The refusal is pinned to the field it is about.</summary>
    [Theory]
    [InlineData("firstName must be at most 128 characters.", "firstName")]
    [InlineData("lastName must be at most 128 characters.", "lastName")]
    [InlineData("email must be at most 320 characters.", "email")]
    [InlineData("notes must not be blank.", "notes")]
    public void ARefusalAboutOneFieldNamesThatField(string detail, string expected)
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest, detail, "Invalid request", PersonFields);

        Assert.Equal(expected, shown.Field);
    }

    /// <summary>A body that could not be read names its field too.</summary>
    /// <remarks>
    /// <c>AOS-R002-008</c>'s shape: the field is quoted rather than leading.
    /// </remarks>
    [Fact]
    public void AFieldThatCouldNotBeReadIsAlsoNamed()
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest,
            "'email' could not be read. Expected an identifier.",
            "Invalid request",
            PersonFields);

        Assert.Equal("email", shown.Field);
    }

    /// <summary>
    /// A refusal about the submission is not pinned to a field.
    /// </summary>
    /// <remarks>
    /// The instruction that matters most here: do not mechanically attach one error
    /// to every field. Telling a screen-reader user that "this target has not been
    /// approved" describes the first name box would be a lie told accessibly.
    /// </remarks>
    [Theory]
    [InlineData("This target has not been approved yet. Approve it before recording a pitch.")]
    [InlineData("Permission 'people.write' is required.")]
    [InlineData("This project has changed since you last saw it (you had version 6, it is now 7).")]
    public void ARefusalAboutTheWholeSubmissionIsNotPinnedToAField(string detail)
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest, detail, "Invalid request", PersonFields);

        Assert.True(shown.KeepsDialogOpen);
        Assert.Null(shown.Field);
    }

    /// <summary>A field this dialog does not have is not invented.</summary>
    [Fact]
    public void AFieldTheDialogDoesNotHaveIsNotNamed()
    {
        RefusalPresentation shown = DialogRefusal.Present(
            HttpStatusCode.BadRequest,
            "middleName must be at most 128 characters.",
            "Invalid request",
            PersonFields);

        Assert.Null(shown.Field);
    }

    /// <summary>A dialog with no fields associates nothing.</summary>
    [Fact]
    public void ADialogWithNoFieldsAssociatesNothing() =>
        Assert.Null(DialogRefusal.FieldNamed("firstName must not be blank.", []));

    /// <summary>The match is on the whole name, not a prefix of one.</summary>
    /// <remarks>
    /// Without this, a refusal about <c>name</c> would attach itself to
    /// <c>legalName</c> or <c>firstName</c> in a dialog that has both.
    /// </remarks>
    [Fact]
    public void AFieldIsMatchedWholeRatherThanByPrefix()
    {
        string[] company = ["name", "legalName", "website"];

        Assert.Equal("name", DialogRefusal.FieldNamed("name must be at most 256 characters.", company));
        Assert.Equal(
            "legalName",
            DialogRefusal.FieldNamed("legalName must be at most 256 characters.", company));
    }
}
