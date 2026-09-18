using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Organizations;
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
    public CreatePackageDialog(
        IReadOnlyList<ProjectSummaryResponse> projects,
        IReadOnlyList<OrganizationMemberResponse> members)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);

        InitializeComponent();

        ProjectBox.ItemsSource = EntityChoice.ForProjects(projects);

        // Preselect whoever is signed in, proved by the directory's own IsSelf
        // rather than guessed; they can still choose somebody else.
        OwnerPicker(members);
    }

    /// <summary>The project the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => ProjectBox.SelectedItem as EntityChoice;

    public CreatePackageRequest ToRequest() => new(
        Chosen()?.Id ?? Guid.Empty,
        NameBox.Text.Trim(),
        Chosen(LeadIdBox),
        Empty(ThesisBox.Text),
        Empty(StrategyBox.Text));

    private void OnRequiredSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Validate();

    // object rather than TextChangedEventArgs: the lead is a picker now, and one
    // handler serves both a text change and a selection change (AOS-R001-006).
    private void OnRequiredChanged(object sender, object e) => Validate();

    private void Validate() =>
        IsPrimaryButtonEnabled =
            Chosen() is not null
            && Chosen(LeadIdBox) != Guid.Empty
            && !string.IsNullOrWhiteSpace(NameBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    /// <summary>
    /// Offers the organization's people, with whoever is signed in preselected.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-006</c>. The directory says which member is the caller through
    /// <c>IsSelf</c>, so the default is proved rather than assumed — and it is only
    /// a default: every other member stays selectable.
    /// </remarks>
    private void OwnerPicker(IReadOnlyList<OrganizationMemberResponse> members)
    {
        IReadOnlyList<EntityChoice> choices = EntityChoice.ForMembers(members);

        LeadIdBox.ItemsSource = choices;

        if (members.FirstOrDefault(x => x.IsSelf) is { } self)
        {
            LeadIdBox.SelectedItem = choices.FirstOrDefault(x => x.Id == self.UserId);
        }
    }

    /// <summary>The member chosen in a picker, or empty while none is.</summary>
    private static Guid Chosen(ComboBox box) =>
        (box.SelectedItem as EntityChoice)?.Id ?? Guid.Empty;

}
