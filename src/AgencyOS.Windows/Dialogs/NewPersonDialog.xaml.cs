using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Captures the minimum needed to create a person.
/// </summary>
/// <remarks>
/// Only the first name is required, matching the domain. Names that do not split
/// into given and family parts are common in this industry, and demanding a last
/// name would make the system wrong about real people.
/// </remarks>
public sealed partial class NewPersonDialog : ContentDialog
{
    public NewPersonDialog() => InitializeComponent();

    /// <summary>Builds the request from the entered values.</summary>
    public CreatePersonRequest ToRequest() => new(
        FirstNameBox.Text.Trim(),
        Blank(LastNameBox.Text),
        DisplayName: null,
        MiddleName: null,
        PreferredName: null,
        PrimaryCompanyId: null,
        Blank(TitleBox.Text),
        Blank(EmailBox.Text),
        Blank(PhoneBox.Text),
        Blank(NotesBox.Text));

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
