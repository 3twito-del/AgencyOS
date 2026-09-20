using AgencyOS.Client.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Presentation;

/// <summary>
/// Renders the open task a surface should offer as the thing to do next.
/// </summary>
/// <remarks>
/// <para>
/// Which task that is belongs to <see cref="NextActionFrom"/>, which is testable
/// and knows nothing about controls. This only writes it onto an
/// <see cref="InfoBar"/>, and exists so the two surfaces that offer a next action
/// say it the same way — a negotiation offers the tasks linked to it, and a client
/// offers the tasks about them.
/// </para>
/// <para>
/// The blind-handoff retest of build 78 could reconstruct an entire case and not
/// answer "what should I do now": the task was open, dated and prioritised the whole
/// time, and no surface presented it as the next action.
/// </para>
/// </remarks>
internal static class NextActionBanner
{
    /// <summary>Shows the action, or closes the bar when there is none.</summary>
    internal static void Apply(InfoBar bar, NextAction? next)
    {
        ArgumentNullException.ThrowIfNull(bar);

        bar.IsOpen = next is not null;

        if (next is null)
        {
            return;
        }

        bar.Title = next.IsOverdue ? "Next action, overdue" : "Next action";
        bar.Severity = next.IsOverdue ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        bar.Message = next.Title + "." + When(next) + Others(next);
    }

    /// <summary>
    /// When it is due, or that nobody said.
    /// </summary>
    /// <remarks>
    /// An undated task says so rather than showing nothing, because a blank space
    /// reads as a rendering fault and an operator cannot tell the two apart.
    /// </remarks>
    private static string When(NextAction next) =>
        next.DueAt is { } due
            ? $" Due {due.ToLocalTime():d MMMM yyyy}."
            : " No date was set.";

    /// <summary>
    /// How many other actions are open, and whether one is equally due.
    /// </summary>
    /// <remarks>
    /// A tie is declared rather than broken. Two tasks due the same day are two
    /// tasks, and choosing between them would be a judgement about importance that
    /// nothing in the domain supports.
    /// </remarks>
    private static string Others(NextAction next) => next.OtherOpenCount switch
    {
        0 => string.Empty,
        1 when next.IsTied => " One other open action is equally due.",
        1 => " One other action is open.",
        _ when next.IsTied =>
            $" {next.OtherOpenCount} other actions are open, and at least one is equally due.",
        _ => $" {next.OtherOpenCount} other actions are open.",
    };
}
