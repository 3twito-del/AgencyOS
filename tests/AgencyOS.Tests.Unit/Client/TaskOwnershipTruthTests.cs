using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// That a surface never reports assigned work as unassigned.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 80 read "Unassigned" off a task that was
/// assigned to a named member. Nothing was wrong with the domain, the column or the
/// API: one read path returned the assignment without resolving the assignee's name,
/// and a renderer that knew only the name could not tell that from nobody being
/// accountable.
/// </para>
/// <para>
/// So ownership is decided by <see cref="NextAction.IsAssigned"/>, which comes from
/// the assignment itself, and never by whether a name happens to be in hand. These
/// tests pin the three states the domain can actually produce. A fourth — an
/// assignee hidden by authorization — is deliberately absent: the name lookup reads
/// every user with no permission filter, so the current model cannot produce it, and
/// a test for it would assert fiction.
/// </para>
/// </remarks>
public sealed class TaskOwnershipTruthTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Member = Guid.Parse("01a0a27a-c0d5-7a63-830d-b6a1d4f85a88");

    private static NextAction Offer(string? name, Guid? id) =>
        NextActionFrom.Of(
            [new NextActionFrom.Candidate("Confirm the option window", "Open", Now.AddDays(3), name, id)],
            Now)!;

    /// <summary>A — nobody is accountable, and the surface may say so.</summary>
    [Fact]
    public void GenuinelyUnassignedWorkIsReportedUnassigned()
    {
        NextAction next = Offer(name: null, id: null);

        Assert.False(next.IsAssigned);
        Assert.Null(next.Assignee);
    }

    /// <summary>B — somebody is accountable and the read resolved who.</summary>
    [Fact]
    public void AssignedWorkWithAResolvedNameReportsTheName()
    {
        NextAction next = Offer("Review member", Member);

        Assert.True(next.IsAssigned);
        Assert.Equal("Review member", next.Assignee);
    }

    /// <summary>
    /// C — somebody is accountable and this read did not resolve who.
    /// </summary>
    /// <remarks>
    /// The defect. The name is absent exactly as in case A, and the two must not be
    /// confused: the assignment is still a fact.
    /// </remarks>
    [Fact]
    public void AssignedWorkWithAnUnresolvedNameIsStillAssigned()
    {
        NextAction next = Offer(name: null, id: Member);

        Assert.True(next.IsAssigned);
        Assert.Null(next.Assignee);
    }

    /// <summary>An empty name is a missing name, not a person called "".</summary>
    [Fact]
    public void BlankNamesDoNotCountAsAName()
    {
        NextAction next = Offer("   ", Member);

        Assert.True(next.IsAssigned);
        Assert.Null(next.Assignee);
    }

    /// <summary>
    /// The states are distinguishable, which is the whole point.
    /// </summary>
    /// <remarks>
    /// A regression that drops the id would make these two equal, and the renderer
    /// would be back to guessing from the name.
    /// </remarks>
    [Fact]
    public void UnassignedAndUnresolvedAreNotTheSameState()
    {
        NextAction unassigned = Offer(name: null, id: null);
        NextAction unresolved = Offer(name: null, id: Member);

        Assert.NotEqual(unassigned.IsAssigned, unresolved.IsAssigned);
    }
}
