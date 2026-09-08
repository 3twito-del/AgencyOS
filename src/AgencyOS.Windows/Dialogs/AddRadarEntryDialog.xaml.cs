using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Puts somebody on the talent radar.
/// </summary>
/// <remarks>
/// <para>
/// The rationale is required. A radar entry with no stated reason is a name on a
/// list, and the person who finds it in six months has no way to tell whether the
/// interest was serious.
/// </para>
/// <para>
/// The radar points at an existing person record rather than taking a typed name.
/// Two spellings of the same person would be two entries, two research threads and
/// eventually two prospects (§59).
/// </para>
/// </remarks>
public sealed partial class AddRadarEntryDialog : ContentDialog
{
    public AddRadarEntryDialog(IReadOnlyList<PersonSummaryResponse> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        InitializeComponent();

        PersonBox.ItemsSource = people.ToList();
    }

    public CreateRadarEntryRequest ToRequest() =>
        new(
            Chosen()?.Id ?? Guid.Empty,
            (RationaleBox.Text ?? string.Empty).Trim(),
            Selection(SensitivityBox) ?? "Internal",
            OwnerUserId: null,
            string.IsNullOrWhiteSpace(DisciplinesBox.Text) ? null : DisciplinesBox.Text.Trim(),
            Selection(PriorityBox) ?? "Unassigned");

    /// <summary>The person the operator chose, or null while none is chosen.</summary>
    public PersonSummaryResponse? Chosen() => PersonBox.SelectedItem as PersonSummaryResponse;

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdatePrimary();

    private void OnRequiredSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdatePrimary();

    private void UpdatePrimary() =>
        IsPrimaryButtonEnabled =
            Chosen() is not null && !string.IsNullOrWhiteSpace(RationaleBox.Text);

    private static string? Selection(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
