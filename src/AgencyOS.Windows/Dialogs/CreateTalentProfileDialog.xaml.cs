using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Creates the talent profile of a client just signed, as its own step.
/// </summary>
/// <remarks>
/// Signing a client creates a representation; it does not create a talent profile,
/// and the Talent roster and talent pursuits are built from profiles (owner decision
/// C). This is the deliberate follow-up: the person is fixed, the career stage is
/// said out loud, and nothing is created until the operator confirms. Unknown is the
/// default because it is the domain's honest answer before anybody has assessed them.
/// </remarks>
public sealed partial class CreateTalentProfileDialog : ContentDialog
{
    public CreateTalentProfileDialog(string displayName)
    {
        InitializeComponent();

        SubjectText.Text = $"Create a talent profile for {displayName}.";
    }

    /// <summary>The career stage chosen; Unknown unless the operator says otherwise.</summary>
    public string CareerStage =>
        (CareerStageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Unknown";
}
