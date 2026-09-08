using System;
using System.Collections.Generic;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Works out what the agency is entitled to against an obligation.
/// </summary>
/// <remarks>
/// Entitlement, not revenue. What this produces is what the agency may charge
/// against the whole obligation; what it has actually earned depends on what has
/// arrived, and the two are kept apart everywhere downstream (ADR-0023).
/// </remarks>
public sealed partial class CalculateCommissionDialog : ContentDialog
{
    public CalculateCommissionDialog(
        MonetaryObligationResponse obligation,
        IReadOnlyList<TalentSummaryResponse> clients)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        ArgumentNullException.ThrowIfNull(clients);

        InitializeComponent();

        HeadlineText.Text = obligation.Description ?? obligation.Category;

        string due = obligation.DueOn is { } dueOn
            ? string.Create(CultureInfo.InvariantCulture, $", due {dueOn:yyyy-MM-dd}.")
            : ", with no due date this build can work out.";

        BasisText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.FormatOrUnknown(obligation.Amount)} from "
                + $"{obligation.PayerDisplayName}{due}");

        foreach (TalentSummaryResponse client in clients)
        {
            ClientBox.Items.Add(new ComboBoxItem
            {
                Content = client.DisplayName,
                Tag = client.PersonId,
            });
        }

        // The governing date defaults to when the money falls due, not to today.
        if (obligation.DueOn is { } governingDefault)
        {
            GoverningPicker.Date =
                new DateTimeOffset(governingDefault.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }
    }

    public Guid SelectedClientPersonId =>
        (ClientBox.SelectedItem as ComboBoxItem)?.Tag is Guid id ? id : Guid.Empty;

    public CalculateCommissionRequest ToRequest(Guid representationId) =>
        new(
            SelectedClientPersonId,
            representationId,
            GoverningPicker.Date is { } governing
                ? DateOnly.FromDateTime(governing.DateTime)
                : null,
            ClientReceivableId: null,
            Notes: string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = SelectedClientPersonId != Guid.Empty;
}
