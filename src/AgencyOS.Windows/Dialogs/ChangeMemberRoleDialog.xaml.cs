using AgencyOS.Contracts.Organizations;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Moves somebody to a different role.
/// </summary>
/// <remarks>
/// The person is chosen on the page before this opens, so the dialog names them
/// rather than asking who. It says what they hold now and what each role means,
/// because "Administrator" does not mean here what it means elsewhere.
/// </remarks>
public sealed partial class ChangeMemberRoleDialog : ContentDialog
{
    /// <summary>Creates the dialog for one member.</summary>
    /// <param name="displayName">Who is being changed.</param>
    /// <param name="currentRole">What they hold now.</param>
    public ChangeMemberRoleDialog(string displayName, string currentRole)
    {
        InitializeComponent();

        WhoText.Text = displayName;
        CurrentText.Text = $"They are currently {Article(currentRole)} {currentRole.ToLowerInvariant()}.";

        foreach (object item in RoleBox.Items)
        {
            if (item is ComboBoxItem candidate
                && string.Equals(candidate.Tag as string, currentRole, StringComparison.Ordinal))
            {
                // Start on what they hold, so the dialog opens saying the truth
                // rather than proposing a change nobody asked for.
                RoleBox.SelectedItem = candidate;

                break;
            }
        }

        Describe();
    }

    /// <summary>The request this dialog composes.</summary>
    /// <returns>The role they should hold instead.</returns>
    public ChangeMemberRoleRequest ToRequest() => new(SelectedTag() ?? "Member");

    /// <summary>The role chosen, for the caller to compare against the current one.</summary>
    public string SelectedRole => SelectedTag() ?? string.Empty;

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e) => Describe();

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
            "Member" => "The day-to-day role. Does the operational work. Cannot change "
                + "who may do what.",
            "Administrator" => "Oversight. Reads widely, including sensitive material, "
                + "and writes almost nothing operational. Manages people and roles.",
            "Owner" => "Everything a member does and everything an administrator oversees, "
                + "plus retiring the agency and deciding which builds its people may run.",
            _ => string.Empty,
        };
    }

    private static string Article(string role) =>
        role.StartsWith('A') || role.StartsWith('O') ? "an" : "a";

    private string? SelectedTag() => (RoleBox.SelectedItem as ComboBoxItem)?.Tag as string;
}
