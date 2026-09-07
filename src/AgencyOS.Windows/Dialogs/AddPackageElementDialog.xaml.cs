using System;
using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Adds one item to a package.</summary>
/// <remarks>
/// The dialog names the distinction it is easiest to lose while typing: an
/// attached party is a fact about the project, everything else is the agency's own
/// working list (ADR-0019).
/// </remarks>
public sealed partial class AddPackageElementDialog : ContentDialog
{
    public AddPackageElementDialog()
    {
        InitializeComponent();

        KindBox.SelectedIndex = 1;
    }

    public AddPackageElementRequest ToRequest(int expectedVersion) => new(
        SelectedTag(KindBox) ?? "ProposedPerson",
        Guid.TryParse(TargetIdBox.Text.Trim(), out Guid target) ? target : Guid.Empty,
        expectedVersion,
        Empty(NoteBox.Text));

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => Validate();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate() => IsPrimaryButtonEnabled = Guid.TryParse(TargetIdBox.Text.Trim(), out _);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
