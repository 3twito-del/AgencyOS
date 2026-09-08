using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Asks why, for the financial acts that undo something.
/// </summary>
/// <remarks>
/// <para>
/// Write-off, cancellation, voiding an invoice, reversing a payment and reversing
/// a journal entry are all the same shape: an act that changes what the books say,
/// with a reason that has to survive on the record. None of them deletes anything,
/// and the dialog says so out loud rather than leaving the operator to assume the
/// row is about to disappear (ADR-0023).
/// </para>
/// <para>
/// The reason is mandatory. A write-off with no stated basis is a number somebody
/// will find in three years with no way to tell whether it was a bad debt, a
/// settlement or a mistake.
/// </para>
/// </remarks>
public sealed partial class FinanceReasonDialog : ContentDialog
{
    /// <param name="title">What is about to happen, in the operator's words.</param>
    /// <param name="headline">Which record it happens to.</param>
    /// <param name="consequence">
    /// What the act does to the books. Written plainly, because the difference
    /// between "we gave up on collecting this" and "this row is gone" is the whole
    /// point.
    /// </param>
    /// <param name="verb">The primary button's wording.</param>
    public FinanceReasonDialog(string title, string headline, string consequence, string verb)
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
