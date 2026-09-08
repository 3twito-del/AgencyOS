using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Asks why, for the intelligence acts that stop something.
/// </summary>
/// <remarks>
/// <para>
/// Retiring a thesis, cancelling a prediction and dismissing a radar entry are the
/// same shape: an act that ends a line of thinking, with a reason that has to outlive
/// whoever had it. None of them deletes anything, and the dialog says so rather than
/// leaving the operator to assume the row is about to disappear.
/// </para>
/// <para>
/// The reason is mandatory. A thesis that quietly stopped being held, or somebody
/// dropped from the radar with no stated basis, is a decision nobody can review — and
/// reviewing old decisions is most of what intelligence is for.
/// </para>
/// </remarks>
public sealed partial class IntelligenceReasonDialog : ContentDialog
{
    /// <param name="title">What is about to happen, in the operator's words.</param>
    /// <param name="headline">Which record it happens to.</param>
    /// <param name="consequence">What the act actually does, stated plainly.</param>
    /// <param name="verb">The primary button's wording.</param>
    public IntelligenceReasonDialog(
        string title,
        string headline,
        string consequence,
        string verb)
    {
        InitializeComponent();

        Title = title;
        HeadlineText.Text = headline;
        ConsequenceBar.Message = consequence;
        PrimaryButtonText = verb;
    }

    /// <summary>The reason, trimmed. Never empty: the button does not enable without one.</summary>
    public string Reason => (ReasonBox.Text ?? string.Empty).Trim();

    private void OnReasonChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(ReasonBox.Text);
}
