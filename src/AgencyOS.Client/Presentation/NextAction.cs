namespace AgencyOS.Client.Presentation;

/// <summary>One open task, said as the thing to do next.</summary>
/// <param name="Title">What needs doing, in the words somebody wrote.</param>
/// <param name="DueAt">When, if anybody said.</param>
/// <param name="IsOverdue">Whether that date has passed.</param>
/// <param name="OtherOpenCount">How many other open actions there are.</param>
/// <param name="Assignee">
/// Who is accountable, by name, when the surface was told the name. Null means the
/// name is not in hand — which is <em>not</em> the same as nobody being accountable.
/// Read it with <paramref name="IsAssigned"/> and never on its own.
/// </param>
/// <param name="IsAssigned">
/// Whether somebody is accountable at all. This is the authoritative half: it comes
/// from the assignment itself rather than from whether a name was resolved, so a
/// surface can say "Unassigned" only when this is false.
/// </param>
/// <param name="IsTied">
/// Whether another open action is equally eligible — the same due date, or both
/// undated. When it is, the surface says there are several rather than implying
/// this one was chosen for a reason.
/// </param>
public sealed record NextAction(
    string Title,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    int OtherOpenCount,
    bool IsTied,
    string? Assignee = null,
    bool IsAssigned = false);

/// <summary>
/// Picks the action a surface should offer, from tasks that already exist.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 78 could reconstruct the whole history of a
/// case and could not answer "what should I do now". The answer was in the data the
/// entire time — an open, dated, prioritised task — and no surface presented it as
/// the next action. The operator's list of next steps was their own inference from
/// the state, which is exactly the work a handover is supposed to remove.
/// </para>
/// <para>
/// This adds no task system, no case entity and no ranking model. It orders tasks
/// that are already there, by the only rule the domain supports without inventing
/// importance: a date somebody wrote down.
/// </para>
/// <para>
/// <strong>The rule.</strong> Open tasks only. Earliest due date first, which puts
/// overdue before upcoming because overdue dates are earlier. Undated tasks last,
/// because a task nobody dated is not more urgent than one somebody did. Ties are
/// reported rather than broken: two tasks due the same day are two tasks, and
/// picking one would be a judgement this code has no basis for.
/// </para>
/// </remarks>
public static class NextActionFrom
{
    /// <summary>A task as this decision needs to see it.</summary>
    /// <param name="Title">What needs doing.</param>
    /// <param name="State">The task's state; only open ones count.</param>
    /// <param name="DueAt">When it is due, if anybody said.</param>
    /// <param name="Assignee">Their name, when the read that produced this resolved it.</param>
    /// <param name="AssigneeId">
    /// Who is accountable. Carried separately from the name because a projection can
    /// return the assignment without resolving who it belongs to, and a surface that
    /// sees only a missing name cannot tell that from nobody being accountable. It is
    /// never rendered; it decides which sentence is true.
    /// </param>
    public readonly record struct Candidate(
        string? Title,
        string? State,
        DateTimeOffset? DueAt,
        string? Assignee = null,
        Guid? AssigneeId = null);

    /// <summary>The next action, or nothing when there is none.</summary>
    /// <param name="tasks">Every task the surface holds, open or not.</param>
    /// <param name="asOf">Now, for deciding what is overdue.</param>
    public static NextAction? Of(IEnumerable<Candidate>? tasks, DateTimeOffset asOf)
    {
        if (tasks is null)
        {
            return null;
        }

        List<Candidate> open =
        [
            .. tasks
                .Where(x => string.Equals(x.State, "Open", StringComparison.Ordinal))
                .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                // Dated before undated, then earliest first.
                .OrderBy(x => x.DueAt.HasValue ? 0 : 1)
                .ThenBy(x => x.DueAt ?? DateTimeOffset.MaxValue),
        ];

        if (open.Count == 0)
        {
            return null;
        }

        Candidate first = open[0];

        bool tied = open.Count > 1 && open[1].DueAt == first.DueAt;

        return new NextAction(
            first.Title!,
            first.DueAt,
            first.DueAt is { } due && due < asOf,
            open.Count - 1,
            tied,
            string.IsNullOrWhiteSpace(first.Assignee) ? null : first.Assignee,
            first.AssigneeId is not null);
    }
}
