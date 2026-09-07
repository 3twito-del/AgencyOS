using System;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Records a company's structural involvement in a project.</summary>
/// <remarks>
/// Deliberately separate from attaching somebody to a role. A studio is not a
/// position anybody fills, and the two workflows stay apart so the data does too
/// (ADR-0019).
/// </remarks>
public sealed partial class AddProjectCompanyDialog : ContentDialog
{
    public AddProjectCompanyDialog()
    {
        InitializeComponent();

        CapacityBox.SelectedIndex = 0;
        StartsOnPicker.Date = DateTimeOffset.UtcNow;
    }

    public AddProjectCompanyRequest ToRequest(int expectedVersion) => new(
        Guid.TryParse(CompanyIdBox.Text.Trim(), out Guid company) ? company : Guid.Empty,
        SelectedTag(CapacityBox) ?? "Other",
        DateOnly.FromDateTime(StartsOnPicker.Date.DateTime),
        expectedVersion,
        EndsOn: null,
        Empty(NotesBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Guid.TryParse(CompanyIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
