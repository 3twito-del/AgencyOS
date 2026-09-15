using System;
using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a piece of evidence.
/// </summary>
/// <remarks>
/// <para>
/// The dialog's job is to stop a source being recorded as more than it is. A URL is
/// a reference and the screen says so; a manual observation is what somebody
/// remembers and has no publication date at all; and nothing on this dialog asks how
/// true the source is, because reliability is a separate judgment recorded with the
/// name of whoever made it (ADR-0030).
/// </para>
/// <para>
/// Classification is chosen, never inferred. Whether somebody spoke in confidence is
/// something they said, and a rule that guessed it from the wording would be wrong
/// in exactly the cases that matter (§26).
/// </para>
/// </remarks>
public sealed partial class RecordSourceDialog : ContentDialog
{
    public RecordSourceDialog()
    {
        InitializeComponent();

        ApplyKind();
    }

    /// <summary>What the operator filled in, as the API expects it.</summary>
    public RecordSourceRequest ToRequest() =>
        new(
            Kind,
            Text(TitleBox),
            Selection(SensitivityBox) ?? "Internal",
            DocumentVersionId: null,
            MessageId: null,
            Kind == "ExternalUrl" ? Text(UrlBox) : null,
            Optional(PublisherBox),
            Optional(AuthorBox),
            Optional(ReferenceBox),

            // A published date only where publication is a thing that happened. An
            // observation has no publisher and no publication, and offering the
            // field would invite somebody to enter the date they heard it.
            Kind is "ManualObservation" ? null : PublishedPicker.Date,
            ObservedPicker.Date,
            Optional(NotesBox));

    private string Kind => Selection(KindBox) ?? "ManualObservation";

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyKind();
        UpdatePrimary();
    }

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdatePrimary();

    private void ApplyKind()
    {
        // KindBox declares its default selection in the markup, so the parser
        // raises SelectionChanged while InitializeComponent is still running -
        // and everything below it in the document, UrlBox and CustodyBar and
        // PublishedPicker among them, does not exist yet. Reaching one then
        // threw, and the fire-and-forget opener discarded the throw, so the
        // operator ran the command and saw nothing at all (AOS-R002-019). The
        // constructor calls this again once the dialog is whole.
        if (UrlBox is null || CustodyBar is null || PublishedPicker is null)
        {
            return;
        }

        bool isUrl = Kind == "ExternalUrl";

        UrlBox.Visibility = isUrl
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        PublishedPicker.IsEnabled = Kind is not "ManualObservation";

        CustodyBar.Message = Kind switch
        {
            "DocumentVersion" =>
                "AgencyOS holds these bytes. The document version is the evidence itself.",
            "Message" =>
                "AgencyOS holds this message. The synchronized copy is the evidence itself.",
            "ExternalUrl" =>
                "A reference only. AgencyOS stores the address and what you record about "
                    + "it; the page is not archived and cannot be produced later.",
            "ManualObservation" =>
                "What you observed, in your words, with your name on it. There is no "
                    + "artifact behind this one.",
            _ => "A reference. AgencyOS holds what you record here and nothing else.",
        };
    }

    private void UpdatePrimary()
    {
        // Reached from OnKindChanged, and so from the same load-time selection
        // the guard above exists for. TitleBox and UrlBox are both declared
        // after KindBox (AOS-R002-019).
        if (TitleBox is null || UrlBox is null)
        {
            return;
        }

        bool hasTitle = !string.IsNullOrWhiteSpace(TitleBox.Text);

        bool hasUrl = Kind != "ExternalUrl"
            || Uri.TryCreate(Text(UrlBox), UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https";

        IsPrimaryButtonEnabled = hasTitle && hasUrl;
    }

    private static string Text(TextBox box) => (box.Text ?? string.Empty).Trim();

    private static string? Optional(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private static string? Selection(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
