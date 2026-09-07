using System;
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
    public CreatePackageDialog() => InitializeComponent();

    public CreatePackageRequest ToRequest() => new(
        Guid.TryParse(ProjectIdBox.Text.Trim(), out Guid project) ? project : Guid.Empty,
        NameBox.Text.Trim(),
        Guid.TryParse(LeadIdBox.Text.Trim(), out Guid lead) ? lead : Guid.Empty,
        Empty(ThesisBox.Text),
        Empty(StrategyBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled =
            Guid.TryParse(ProjectIdBox.Text.Trim(), out _)
            && Guid.TryParse(LeadIdBox.Text.Trim(), out _)
            && !string.IsNullOrWhiteSpace(NameBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
