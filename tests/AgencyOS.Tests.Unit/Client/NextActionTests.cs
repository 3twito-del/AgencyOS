using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// Choosing the open task a surface should offer as the next thing to do.
/// </summary>
/// <remarks>
/// The blind-handoff retest of build 78 reconstructed a whole case and could not
/// answer "what should I do now". An open, dated, prioritised task existed the whole
/// time and no surface presented it as the next action. This adds no task system:
/// it orders tasks that already exist, by a date somebody wrote down.
/// </remarks>
public sealed class NextActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static NextActionFrom.Candidate Task(
        string title, string state = "Open", DateTimeOffset? due = null) =>
        new(title, state, due);

    /// <summary>Nothing open means nothing offered.</summary>
    [Fact]
    public void NoOpenTaskMeansNoNextAction()
    {
        Assert.Null(NextActionFrom.Of([], Now));
        Assert.Null(NextActionFrom.Of(null, Now));
        Assert.Null(NextActionFrom.Of([Task("Done already", state: "Completed")], Now));
    }

    /// <summary>Completed tasks are never offered.</summary>
    [Fact]
    public void OnlyOpenTasksAreEligible()
    {
        NextAction? next = NextActionFrom.Of(
            [
                Task("Finished", state: "Completed", due: Now.AddDays(-9)),
                Task("Still to do", due: Now.AddDays(4)),
            ],
            Now);

        Assert.Equal("Still to do", next!.Title);
    }

    /// <summary>Overdue comes first, because its date is earliest.</summary>
    [Fact]
    public void OverdueOutranksUpcoming()
    {
        NextAction? next = NextActionFrom.Of(
            [
                Task("Due next week", due: Now.AddDays(7)),
                Task("Should have happened", due: Now.AddDays(-3)),
            ],
            Now);

        Assert.Equal("Should have happened", next!.Title);
        Assert.True(next.IsOverdue);
        Assert.Equal(1, next.OtherOpenCount);
    }

    /// <summary>Among future tasks, the earliest is next.</summary>
    [Fact]
    public void TheEarliestFutureDateIsNext()
    {
        NextAction? next = NextActionFrom.Of(
            [
                Task("Later", due: Now.AddDays(30)),
                Task("Sooner", due: Now.AddDays(2)),
            ],
            Now);

        Assert.Equal("Sooner", next!.Title);
        Assert.False(next.IsOverdue);
    }

    /// <summary>A task nobody dated is not urgent by virtue of having no date.</summary>
    [Fact]
    public void UndatedTasksComeLast()
    {
        NextAction? next = NextActionFrom.Of(
            [
                Task("Someday"),
                Task("By Friday", due: Now.AddDays(5)),
            ],
            Now);

        Assert.Equal("By Friday", next!.Title);
    }

    /// <summary>An undated task is still offered when it is all there is.</summary>
    [Fact]
    public void AnUndatedTaskIsStillAnAction()
    {
        NextAction? next = NextActionFrom.Of([Task("No date on this one")], Now);

        Assert.Equal("No date on this one", next!.Title);
        Assert.Null(next.DueAt);
        Assert.False(next.IsOverdue);
        Assert.Equal(0, next.OtherOpenCount);
    }

    /// <summary>
    /// Two tasks equally due are reported as two, not silently resolved into one.
    /// </summary>
    /// <remarks>
    /// Picking between them would be a judgement about importance, and nothing in
    /// the domain supports one.
    /// </remarks>
    [Fact]
    public void ATieIsDeclaredRatherThanBroken()
    {
        DateTimeOffset same = Now.AddDays(3);

        NextAction? next = NextActionFrom.Of(
            [Task("One thing", due: same), Task("Another thing", due: same)], Now);

        Assert.True(next!.IsTied);
        Assert.Equal(1, next.OtherOpenCount);
    }

    /// <summary>Two undated tasks are equally eligible too.</summary>
    [Fact]
    public void TwoUndatedTasksAreATie()
    {
        Assert.True(NextActionFrom.Of([Task("A"), Task("B")], Now)!.IsTied);
    }

    /// <summary>Different dates are not a tie.</summary>
    [Fact]
    public void DifferentDatesAreNotATie()
    {
        NextAction? next = NextActionFrom.Of(
            [Task("First", due: Now.AddDays(1)), Task("Second", due: Now.AddDays(2))], Now);

        Assert.False(next!.IsTied);
        Assert.Equal(1, next.OtherOpenCount);
    }

    /// <summary>A task with no title is not an instruction.</summary>
    [Fact]
    public void AnEmptyTitleIsNotOffered()
    {
        Assert.Null(NextActionFrom.Of([Task("   ")], Now));
    }

    /// <summary>The count is of other open actions, not of every task.</summary>
    [Fact]
    public void OtherOpenCountExcludesCompletedWork()
    {
        NextAction? next = NextActionFrom.Of(
            [
                Task("Do this", due: Now.AddDays(1)),
                Task("And this", due: Now.AddDays(6)),
                Task("Long done", state: "Completed"),
            ],
            Now);

        Assert.Equal(1, next!.OtherOpenCount);
    }
}
