using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Ai;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Asks a person to decide one exact action a model proposed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The default button is Reject.</strong> Not neutral, and not Approve. A
/// dialog whose default commits a canonical write turns a keypress into a business
/// act, and the whole point of the approval is that somebody chose.
/// </para>
/// <para>
/// Everything shown comes from the server's account of the tool request: the
/// summary AgencyOS wrote from the validated arguments, and the arguments
/// themselves. The model's own words about what it is asking for appear nowhere,
/// because a model that could word its own approval prompt could describe one
/// action and request another.
/// </para>
/// <para>
/// There is deliberately no "approve everything from this run" and no "don't ask
/// again". A standing approval is a permission grant wearing a button.
/// </para>
/// </remarks>
public sealed partial class ApproveAiActionDialog : ContentDialog
{
    private readonly AiApprovalResponse _approval;

    public ApproveAiActionDialog(AiApprovalResponse approval, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);

        InitializeComponent();

        _approval = approval;

        SummaryText.Text = approval.Summary;
        ArgumentsText.Text = approval.Arguments;

        EffectBar.Message = string.Create(
            CultureInfo.CurrentCulture,
            $"{approval.ToolName} was requested by a {approval.AgentKind} run. It has not run.");

        ExpiryText.Text = AiFormatting.Remaining(approval, now);

        // An approval that has lapsed cannot be granted, and offering the button
        // would mean telling somebody they approved something and then that they
        // did not.
        IsPrimaryButtonEnabled = approval is { Decision: "Pending", HasExpired: false };
    }

    /// <summary>The approval this dialog is about.</summary>
    public AiApprovalResponse Approval => _approval;

    /// <summary>Why, in the deciding person's words. Empty when they did not say.</summary>
    public string? Reason
    {
        get
        {
            string reason = (ReasonBox.Text ?? string.Empty).Trim();
            return reason.Length == 0 ? null : reason;
        }
    }
}
