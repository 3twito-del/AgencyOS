using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
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
    /// <param name="companies">
    /// The companies this organization holds. The operator chooses one by name;
    /// the identifier the command carries is the one attached to that choice and
    /// is never typed (<c>AOS-R001-006</c>).
    /// </param>
    public AddProjectCompanyDialog(IReadOnlyList<CompanySummaryResponse> companies)
    {
        ArgumentNullException.ThrowIfNull(companies);

        InitializeComponent();

        CompanyBox.ItemsSource = EntityChoice.ForCompanies(companies);

        CapacityBox.SelectedIndex = 0;
        StartsOnPicker.Date = DateTimeOffset.UtcNow;
    }

    /// <summary>The company the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => CompanyBox.SelectedItem as EntityChoice;

    public AddProjectCompanyRequest ToRequest(int expectedVersion) => new(
        Chosen()?.Id ?? Guid.Empty,
        SelectedTag(CapacityBox) ?? "Other",
        DateOnly.FromDateTime(StartsOnPicker.Date.DateTime),
        expectedVersion,
        EndsOn: null,
        Empty(NotesBox.Text));

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Chosen() is not null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
