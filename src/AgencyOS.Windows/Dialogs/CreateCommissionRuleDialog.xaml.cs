using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records the rule that decides what the agency is entitled to.
/// </summary>
/// <remarks>
/// <para>
/// There is no universal commission formula in AgencyOS, and this dialog does not
/// invent one. It offers three shapes an agency actually uses - a percentage of
/// gross, a percentage of one named term, or a fixed sum - and refuses to guess
/// beyond them. The percentage is capped server-side; anything above the cap is a
/// data-entry error far more often than it is a real arrangement (ADR-0023).
/// </para>
/// <para>
/// Both dates matter. A rule with no end governs from its start until somebody
/// ends it, and ending one never rewrites what was already calculated under it.
/// </para>
/// </remarks>
public sealed partial class CreateCommissionRuleDialog : ContentDialog
{
    private readonly IReadOnlyList<TalentSummaryResponse> _clients;

    public CreateCommissionRuleDialog(
        IReadOnlyList<TalentSummaryResponse> clients,
        IReadOnlyList<ContractTermDefinitionResponse> terms)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(terms);

        InitializeComponent();

        _clients = clients;

        foreach (TalentSummaryResponse client in clients)
        {
            ClientBox.Items.Add(new ComboBoxItem
            {
                Content = client.DisplayName,
                Tag = client.PersonId,
            });
        }

        // Only economic terms can carry a commission basis. A rule keyed to a
        // credit or a travel provision would have nothing to take a percentage of.
        foreach (ContractTermDefinitionResponse term in terms.Where(x => x.IsEconomic))
        {
            TermCodeBox.Items.Add(new ComboBoxItem { Content = term.DisplayName, Tag = term.Code });
        }

        UpdateReady();
    }

    /// <summary>The client the rule is for. The page resolves their representation.</summary>
    public Guid SelectedClientPersonId =>
        (ClientBox.SelectedItem as ComboBoxItem)?.Tag is Guid id ? id : Guid.Empty;

    public string SelectedClientName =>
        _clients.FirstOrDefault(x => x.PersonId == SelectedClientPersonId)?.DisplayName
        ?? string.Empty;

    public CreateCommissionRuleRequest ToRequest(Guid representationId)
    {
        string basis = SelectedTag(BasisBox) ?? "GrossCompensation";

        return new CreateCommissionRuleRequest(
            representationId,
            SelectedClientPersonId,
            basis,
            FromPicker.Date is { } from
                ? DateOnly.FromDateTime(from.DateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow),
            basis == "FixedAmount" ? null : Rate,
            basis == "FixedAmount" ? FixedAmount : null,
            basis == "SpecificTerm" ? SelectedTag(TermCodeBox) : null,
            ContractId: null,
            EffectiveTo: ToPicker.Date is { } to ? DateOnly.FromDateTime(to.DateTime) : null,
            Provenance: Empty(ProvenanceBox.Text));
    }

    private decimal? Rate =>
        decimal.TryParse(
            RateBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;

    private MoneyRequest? FixedAmount
    {
        get
        {
            string currency = (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

            return currency.Length == 3
                && decimal.TryParse(
                    FixedAmountBox.Text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal parsed)
                && parsed > 0m
                    ? new MoneyRequest(parsed, currency)
                    : null;
        }
    }

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void OnBasisChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RatePanel is null)
        {
            return;
        }

        string basis = SelectedTag(BasisBox) ?? "GrossCompensation";

        RatePanel.Visibility = basis == "FixedAmount" ? Visibility.Collapsed : Visibility.Visible;
        FixedPanel.Visibility = basis == "FixedAmount" ? Visibility.Visible : Visibility.Collapsed;
        TermCodeBox.Visibility = basis == "SpecificTerm" ? Visibility.Visible : Visibility.Collapsed;

        UpdateReady();
    }

    private void UpdateReady()
    {
        if (RateBox is null)
        {
            return;
        }

        string basis = SelectedTag(BasisBox) ?? "GrossCompensation";

        bool shapeReady = basis switch
        {
            "FixedAmount" => FixedAmount is not null,
            "SpecificTerm" => Rate is > 0m and <= 100m && TermCodeBox.SelectedItem is not null,
            _ => Rate is > 0m and <= 100m,
        };

        IsPrimaryButtonEnabled =
            SelectedClientPersonId != Guid.Empty && FromPicker.Date is not null && shapeReady;
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
