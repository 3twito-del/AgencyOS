using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.PeopleSlice;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What a task row is allowed to say about who owns it, when it is due, and what
/// it concerns.
/// </summary>
/// <remarks>
/// <para>
/// Six surfaces list tasks. None showed an owner or a due date, and the one that
/// showed a person showed the task's <em>subject</em> — the client it is about —
/// unlabelled beneath the title, which a blind operator read as accountability.
/// </para>
/// <para>
/// These are tests about meaning, not layout. Three projections cannot express
/// assignment at all, and the interesting cases are the ones where saying nothing
/// is the only truthful answer.
/// </para>
/// </remarks>
public sealed class TaskLineTests
{
    private static readonly Guid Member = Guid.Parse("01a0a27a-c0d5-7a63-830d-b6a1d4f85a88");

    private static readonly DateTimeOffset Due = new(2026, 10, 8, 17, 0, 0, TimeSpan.Zero);

    private static TaskResponse Task(
        Guid? assignee = null, string? name = null, DateTimeOffset? due = null,
        PartyReferenceResponse? subject = null) =>
        new(Guid.NewGuid(), "Confirm the carve-out", "Open", "High", due, subject,
            null, DateTimeOffset.UtcNow, null, 1, assignee, name);

    // ------------------------------------------------------------------- who

    /// <summary>
    /// A resolved name is the answer, and it arrives attributed.
    /// </summary>
    /// <remarks>
    /// The name alone was the build-82 asymmetry: the subject said "About …" and
    /// the assignee beside it said only a name, so the row proved a name could be
    /// labelled and then left the other one to convention.
    /// </remarks>
    [Fact]
    public void AnAssignedTaskNamesTheMemberAndTheRole()
    {
        string? who = TaskLine.Who(Task(Member, "Review member"));

        Assert.Equal("Assigned to Review member", who);
    }

    /// <summary>
    /// The role word is the task vocabulary, not the deal one.
    /// </summary>
    /// <remarks>
    /// "Owner" is taken in AgencyOS: it is the member responsible for a deal,
    /// target or opportunity. Reusing it for the member doing one task would put
    /// two different domain roles behind one word, which is the confusion the rule
    /// exists to prevent rather than a tidier label.
    /// </remarks>
    [Fact]
    public void TheAssigneeIsNotCalledAnOwner()
    {
        string? who = TaskLine.Who(Task(Member, "Review member"));

        Assert.DoesNotContain("Owner", who!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Assigned to ", who, StringComparison.Ordinal);
    }

    /// <summary>An identifier that is present and null is authoritative.</summary>
    [Fact]
    public void GenuinelyUnassignedWorkSaysSo() =>
        Assert.Equal("Unassigned", TaskLine.Who(Task()));

    /// <summary>
    /// Assigned with no name is not unassigned.
    /// </summary>
    /// <remarks>
    /// The build-80 defect, restated for list rows: a missing name is not the same
    /// fact as a missing assignment, and this must never collapse them.
    /// </remarks>
    [Fact]
    public void AssignedWorkWithNoNameIsNeverCalledUnassigned()
    {
        string? who = TaskLine.Who(Task(Member));

        Assert.NotNull(who);
        Assert.DoesNotContain("Unassigned", who, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Assigned", who, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A blank name is a missing name, not a person called "".</summary>
    [Fact]
    public void ABlankNameDoesNotCountAsANameOrAsNobody()
    {
        Assert.Equal("Assigned, name unavailable", TaskLine.Who(Task(Member, "   ")));
    }

    /// <summary>
    /// A projection that cannot express assignment says nothing at all.
    /// </summary>
    /// <remarks>
    /// Contract, opportunity and finance tasks carry no assignment field. Under the
    /// coherence rule they are not authoritative for ownership, so "Unassigned"
    /// would be a claim they cannot support. Silence is the only truthful answer,
    /// and it is what the rule asks for.
    /// </remarks>
    [Fact]
    public void AProjectionWithNoAssignmentFieldStaysSilent()
    {
        ContractTaskResponse contractTask =
            new(Guid.NewGuid(), "Serve the option notice", "Open", "High", Due, null, null);

        Assert.Null(TaskLine.Who(contractTask));
    }

    /// <summary>
    /// A projection with a name but no identifier may name, and may not deny.
    /// </summary>
    /// <remarks>
    /// Research tasks carry <c>AssignedToDisplayName</c> and no identifier, so they
    /// can report who when they know, and cannot distinguish nobody from
    /// not-resolved when they do not.
    /// </remarks>
    [Fact]
    public void ANameWithoutAnIdentifierCanNameButNotDeny()
    {
        Assert.Equal("Assigned to Review member", TaskLine.Who(new ResearchLike("Review member")));
        Assert.Null(TaskLine.Who(new ResearchLike(null)));
    }

    /// <summary>The deal projection answers on the same terms.</summary>
    [Fact]
    public void DealTasksUseTheSameRules()
    {
        DealTaskResponse assigned =
            new(Guid.NewGuid(), "Chase the redline", "Open", "High", Due, null, Member, "Review member");

        DealTaskResponse nobody =
            new(Guid.NewGuid(), "Chase the redline", "Open", "High", Due, null);

        Assert.Equal("Assigned to Review member", TaskLine.Who(assigned));
        Assert.Equal("Unassigned", TaskLine.Who(nobody));
    }

    // ------------------------------------------------------------------ when

    /// <summary>A due date is written the one way this product writes dates.</summary>
    [Fact]
    public void ADueDateIsWrittenUnambiguously()
    {
        Assert.Equal("Due 2026-10-08", TaskLine.When(Task(Member, "Review member", Due)));
    }

    /// <summary>No date is not an invented date.</summary>
    [Fact]
    public void AnUndatedTaskSaysNobodySetOne()
    {
        string when = TaskLine.When(Task(Member, "Review member"));

        Assert.Equal("No due date", when);
        Assert.DoesNotContain("20", when, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- subject

    /// <summary>The subject is labelled, so it cannot read as the owner.</summary>
    [Fact]
    public void TheSubjectSaysItIsTheSubject()
    {
        PartyReferenceResponse client = new("Person", Guid.NewGuid(), "Ottoline Brackenridge-Osei");

        string? about = TaskLine.About(Task(Member, "Review member", Due, client));

        Assert.NotNull(about);
        Assert.StartsWith("About ", about, StringComparison.Ordinal);
        Assert.Contains("Ottoline Brackenridge-Osei", about, StringComparison.Ordinal);
    }

    /// <summary>
    /// Subject and owner are different people and stay distinguishable.
    /// </summary>
    /// <remarks>
    /// The Command Center failure in one assertion: a task about one person,
    /// assigned to another, must not let the first be mistaken for the second.
    /// </remarks>
    [Fact]
    public void ATaskAboutOnePersonAssignedToAnotherKeepsThemApart()
    {
        PartyReferenceResponse client = new("Person", Guid.NewGuid(), "Ottoline Brackenridge-Osei");

        TaskResponse row = Task(Member, "Review member", Due, client);

        string? about = TaskLine.About(row);
        string? who = TaskLine.Who(row);

        Assert.NotEqual(about, who);
        Assert.DoesNotContain("Ottoline", who!, StringComparison.Ordinal);
        Assert.DoesNotContain("Review member", about!, StringComparison.Ordinal);
    }

    /// <summary>A task with no subject offers no subject line.</summary>
    [Fact]
    public void NoSubjectMeansNoSubjectLine() => Assert.Null(TaskLine.About(Task(Member, "Review member")));

    /// <summary>No identifier is ever rendered.</summary>
    [Fact]
    public void NoIdentifierIsEverSpoken()
    {
        PartyReferenceResponse client = new("Person", Guid.NewGuid(), "Ottoline Brackenridge-Osei");
        TaskResponse row = Task(Member, "Review member", Due, client);

        string said = $"{TaskLine.Who(row)} {TaskLine.When(row)} {TaskLine.About(row)}";

        Assert.DoesNotContain(Member.ToString(), said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(client.Id.ToString(), said, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A row with a name field but no identifier, as research tasks are.</summary>
    private sealed record ResearchLike(string? AssignedToDisplayName)
    {
        public DateTimeOffset? DueAt => null;
    }
}
