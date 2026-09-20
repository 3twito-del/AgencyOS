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
        bar.Message = next.Title + "." + Who(next) + When(next) + Others(next);
    }

    /// <summary>
    /// Who is accountable: named, or said to be unnamed, or said to be nobody.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Unassigned" is printed rather than omitted. The blind-handoff retest of
    /// build 79 could see what had to happen and not who had to do it, and silence
    /// there reads as "somebody has this" when often nobody does.
    /// </para>
    /// <para>
    /// Three states, not two, because the third is what went wrong. The retest of
    /// build 80 read "Unassigned" off a task that was assigned: one read path
    /// returned the assignment without resolving the name, and a renderer that knew
    /// only the name could not tell that from nobody being accountable. It is a
    /// claim about ownership, so it is made from <see cref="NextAction.IsAssigned"/>,
    /// which is authoritative, and never from a missing name.
    /// </para>
    /// </remarks>
    private static string Who(NextAction next) => next switch
    {
        { Assignee: { } who } => $" {who}.",
        { IsAssigned: true } => " Assigned, name unavailable.",
        _ => " Unassigned.",
    };

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
