using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records that a party signed.
/// </summary>
/// <remarks>
/// <para>
/// The only route to execution, and an assertion rather than a verification. There
/// is no signing surface here and no certificate: a person is telling AgencyOS
/// that a party signed on a date by a method, and that claim is what gets stored
/// (ADR-0022).
/// </para>
/// <para>
/// The default button is Cancel and the last outstanding signature carries a
/// warning, because recording it is the moment the agreement becomes executed.
/// </para>
/// </remarks>
public sealed partial class RecordSignatureDialog : ContentDialog
{
    public RecordSignatureDialog(IReadOnlyList<ContractPartyResponse> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);

        InitializeComponent();

        List<ContractPartyResponse> outstanding =
            [.. parties.Where(x => x.IsRequiredSignatory && !x.HasSigned)];

        PartyBox.ItemsSource = outstanding;

        if (outstanding.Count == 1)
        {
            PartyBox.SelectedIndex = 0;
            LastSignatureBar.IsOpen = true;
        }
    }

    public RecordSignatureRequest ToRequest(int expectedVersion)
    {
        ContractPartyResponse party = (ContractPartyResponse)PartyBox.SelectedItem;

        return new RecordSignatureRequest(
            party.Id,
            DateOnly.FromDateTime(SignedPicker.Date!.Value.DateTime),
            SelectedTag(MethodBox) ?? "Wet",
            expectedVersion,
            Empty(ReferenceBox.Text),
            Empty(NotesBox.Text));
    }

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void UpdateReady() =>
        IsPrimaryButtonEnabled = PartyBox.SelectedItem is not null && SignedPicker.Date is not null;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
