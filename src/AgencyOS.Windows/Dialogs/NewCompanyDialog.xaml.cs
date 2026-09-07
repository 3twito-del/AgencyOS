using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures the minimum needed to create an external company.</summary>
public sealed partial class NewCompanyDialog : ContentDialog
{
    public NewCompanyDialog() => InitializeComponent();

    /// <summary>Builds the request from the entered values.</summary>
    public CreateCompanyRequest ToRequest() => new(
        NameBox.Text.Trim(),
        TypeBox.SelectedItem as string ?? "Other",
        Blank(LegalNameBox.Text),
        Blank(WebsiteBox.Text),
        Blank(NotesBox.Text));

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
