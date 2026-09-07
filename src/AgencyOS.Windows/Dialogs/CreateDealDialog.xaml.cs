using System;
using AgencyOS.Contracts.Deals;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Opens a negotiation against a market conversation.</summary>
/// <remarks>
/// The opportunity and target are both required, so the lineage from client or
/// project through pursuit and target to agreed terms is never broken. The server
/// refuses a target that belongs to a different pursuit, and the database refuses
/// it too (ADR-0021).
/// </remarks>
public sealed partial class CreateDealDialog : ContentDialog
{
    public CreateDealDialog() => InitializeComponent();

    public CreateDealRequest ToRequest() =>
        new(
            Guid.Parse(OpportunityIdBox.Text.Trim()),
            Guid.Parse(TargetIdBox.Text.Trim()),
            NameBox.Text.Trim(),
            SelectedTag(KindBox) ?? "Other",
            Guid.Parse(OwnerIdBox.Text.Trim()),
            OpenedOn: null,
            Empty(ReferenceBox.Text),
            Empty(SummaryBox.Text),
            Empty(StrategyBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(NameBox.Text)
            && Guid.TryParse(OpportunityIdBox.Text.Trim(), out _)
            && Guid.TryParse(TargetIdBox.Text.Trim(), out _)
            && Guid.TryParse(OwnerIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
