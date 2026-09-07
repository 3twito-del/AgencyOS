using System;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a drafting version of a contract.
/// </summary>
/// <remarks>
/// <para>
/// The dialog collects a reference to a document, never the document. AgencyOS has
/// no repository until M10 and has not seen the bytes, so there is no upload
/// control and no integrity claim anywhere on the form (ADR-0022).
/// </para>
/// <para>
/// Direction switches which date is asked for, because an inbound draft arrived
/// and an outbound one went out, and recording the wrong one loses the fact that
/// matters when somebody later asks who was waiting on whom.
/// </para>
/// </remarks>
public sealed partial class RecordContractVersionDialog : ContentDialog
{
    public RecordContractVersionDialog(int nextVersionNumber, string? contractTitle = null)
    {
        InitializeComponent();

        Title = contractTitle is null
            ? $"Record version {nextVersionNumber}"
            : $"Record version {nextVersionNumber} of {contractTitle}";
    }

    public RecordContractVersionRequest ToRequest(int expectedVersion) =>
        new(
            LabelBox.Text.Trim(),
            SelectedTag(DirectionBox) ?? "Inbound",
            expectedVersion,
            Date(ReceivedPicker),
            Date(SentPicker),
            Empty(ReferenceBox.Text),
            Empty(SourceSystemBox.Text),
            Empty(FileNameBox.Text),
            MediaType: null,
            Empty(NotesBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(LabelBox.Text);

    private void OnDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReceivedPicker is null || SentPicker is null)
        {
            return;
        }

        bool inbound = SelectedTag(DirectionBox) == "Inbound";

        ReceivedPicker.Visibility = inbound ? Visibility.Visible : Visibility.Collapsed;
        SentPicker.Visibility = inbound ? Visibility.Collapsed : Visibility.Visible;
    }

    private static DateOnly? Date(CalendarDatePicker picker) =>
        picker.Date is { } value && picker.Visibility == Visibility.Visible
            ? DateOnly.FromDateTime(value.DateTime)
            : null;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
