using AgencyOS.Contracts.Organizations;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Brings somebody into the agency.
/// </summary>
/// <remarks>
/// <para>
/// Three facts and a role. The sign-in name is the identity-provider subject —
/// the one value only the directory knows — and it is asked for by that name
/// rather than as an identifier. Nothing here asks for a GUID.
/// </para>
/// <para>
/// The primary button stays disabled until every required field has something in
/// it, so the dialog cannot submit a request the server will refuse for a reason
/// the user could have seen first.
/// </para>
/// </remarks>
public sealed partial class AddMemberDialog : ContentDialog
{
    /// <summary>Creates the dialog.</summary>
    public AddMemberDialog()
    {
        InitializeComponent();

        Describe();
    }

    /// <summary>The request this dialog composes.</summary>
    /// <returns>Who to add, and as what.</returns>
    public AddMemberRequest ToRequest() => new(
        SubjectBox.Text.Trim(),
        NameBox.Text.Trim(),
        EmailBox.Text.Trim(),
        SelectedTag() ?? "Member");

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdatePrimary();

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e) => Describe();

    private void UpdatePrimary() =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(NameBox.Text)
            && !string.IsNullOrWhiteSpace(EmailBox.Text)
            && !string.IsNullOrWhiteSpace(SubjectBox.Text);

    /// <summary>
    /// Says what the chosen role means, in the dialog, before it is granted.
    /// </summary>
    /// <remarks>
    /// A role name is not self-explanatory, and the difference between an
    /// administrator and a member here is not the one most products use: an
    /// administrator oversees and manages people, and writes almost nothing.
    /// </remarks>
    private void Describe()
    {
        if (RoleText is null)
        {
            return;
        }

        RoleText.Text = SelectedTag() switch
        {
            "Observer" => "Reads the working record set. No money, no private notes, "
                + "no strategy, no contract terms. Changes nothing.",
            "Member" => "The day-to-day role. Does the operational work across people, "
                + "projects, deals, contracts, finance and communications. Cannot change "
                + "who may do what.",
            "Administrator" => "Oversight. Reads widely, including sensitive material, "
                + "and writes almost nothing operational. Manages people and roles, and "
                + "may post a journal entry by hand.",
            "Owner" => "Everything a member does and everything an administrator oversees, "
                + "plus retiring the agency and deciding which builds its people may run.",
            _ => string.Empty,
        };
    }

    private string? SelectedTag() => (RoleBox.SelectedItem as ComboBoxItem)?.Tag as string;
}
