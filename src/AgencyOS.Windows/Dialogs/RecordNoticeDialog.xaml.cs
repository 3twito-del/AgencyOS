using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records that a notice passed between the parties.
/// </summary>
/// <remarks>
/// <para>
/// The verb is Record throughout, exactly as it is for offers and submissions.
/// AgencyOS has no outbound transport, cannot confirm delivery and does not
/// pretend otherwise; what gets stored is a person's assertion that a notice was
/// given or received (ADR-0022).
/// </para>
/// <para>
/// A notice may be recorded against a requirement or on its own. Requiring one
/// would push somebody to invent a requirement to get a real notice on the record.
/// </para>
/// </remarks>
public sealed partial class RecordNoticeDialog : ContentDialog
{
    public RecordNoticeDialog(
        IReadOnlyList<ContractPartyResponse> parties,
        IReadOnlyList<NoticeRequirementResponse> requirements)
    {
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(requirements);

        InitializeComponent();

        SenderBox.ItemsSource = parties;
        RecipientBox.ItemsSource = parties;
        RequirementBox.ItemsSource = requirements;
    }

    public RecordNoticeRequest ToRequest()
    {
        ContractPartyResponse sender = (ContractPartyResponse)SenderBox.SelectedItem;
        ContractPartyResponse recipient = (ContractPartyResponse)RecipientBox.SelectedItem;

        return new RecordNoticeRequest(
            SelectedTag(DirectionBox) ?? "Given",
            sender.Id,
            recipient.Id,
            DateOnly.FromDateTime(OccurredPicker.Date!.Value.DateTime),
            SelectedTag(MethodBox) ?? "Written",
            (RequirementBox.SelectedItem as NoticeRequirementResponse)?.Id,
            Empty(ReferenceBox.Text),
            Empty(SummaryBox.Text));
    }

    private void OnPartyChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void UpdateReady()
    {
        if (SenderBox is null || RecipientBox is null || OccurredPicker is null)
        {
            return;
        }

        // A party does not serve notice on itself, and the server refuses it. The
        // button simply stays off rather than producing a refusal to read.
        bool distinct =
            SenderBox.SelectedItem is ContractPartyResponse from
            && RecipientBox.SelectedItem is ContractPartyResponse to
            && from.Id != to.Id;

        IsPrimaryButtonEnabled = distinct && OccurredPicker.Date is not null;
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
