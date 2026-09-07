using System;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a credit.</summary>
/// <remarks>
/// A title and a contribution type are the minimum that makes a credit meaningful;
/// everything else is optional, because agencies routinely record a credit before
/// they know the year or the exact billing.
/// </remarks>
public sealed partial class AddCreditDialog : ContentDialog
{
    public AddCreditDialog()
    {
        InitializeComponent();

        TypeBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 0;
    }

    public AddCreditRequest ToRequest(Guid personId) => new(
        personId,
        TitleBox.Text.Trim(),
        SelectedTag(TypeBox) ?? "Other",
        Empty(RoleBox.Text),
        SelectedTag(StatusBox),
        double.IsNaN(YearBox.Value) ? null : (int)YearBox.Value,
        CompanyId: null,
        Empty(SourceBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
