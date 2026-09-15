using System;
using System.Collections.Generic;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a new package.</summary>
/// <remarks>
/// The strategy field says on the surface who can read it back, because somebody
/// typing a candid assessment deserves to know before they type it rather than
/// after.
/// </remarks>
public sealed partial class CreatePackageDialog : ContentDialog
{
    /// <param name="projects">
    /// The projects this organization holds. A package is built from one of them,
    /// chosen by title rather than typed as an identifier (<c>AOS-R001-006</c>).
    /// </param>
    public CreatePackageDialog(IReadOnlyList<ProjectSummaryResponse> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        InitializeComponent();

        ProjectBox.ItemsSource = EntityChoice.ForProjects(projects);
    }

    /// <summary>The project the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => ProjectBox.SelectedItem as EntityChoice;

    public CreatePackageRequest ToRequest() => new(
        Chosen()?.Id ?? Guid.Empty,
        NameBox.Text.Trim(),
        Guid.TryParse(LeadIdBox.Text.Trim(), out Guid lead) ? lead : Guid.Empty,
        Empty(ThesisBox.Text),
        Empty(StrategyBox.Text));

    private void OnRequiredSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Validate();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate() =>
        IsPrimaryButtonEnabled =
            Chosen() is not null
            && Guid.TryParse(LeadIdBox.Text.Trim(), out _)
            && !string.IsNullOrWhiteSpace(NameBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
