using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;
using Xunit;

namespace AgencyOS.Tests.Unit.PeopleSlice;

/// <summary>Person and company record rules.</summary>
public sealed class PersonAndCompanyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrganizationId Tenant = OrganizationId.New();
    private static readonly UserId Actor = UserId.New();

    [Fact]
    public void Person_DerivesADisplayNameFromTheNamesGiven()
    {
        Person person = Person.Create(Tenant, "Sarah", "Okonkwo", Actor, Now);

        Assert.Equal("Sarah Okonkwo", person.DisplayName);
    }

    /// <summary>What someone is actually called wins over what is on their passport.</summary>
    [Fact]
    public void Person_PrefersThePreferredNameWhenDeriving()
    {
        Person person = Person.Create(Tenant, "Katherine", "Vance", Actor, Now, preferredName: "Kate");

        Assert.Equal("Kate Vance", person.DisplayName);
    }

    /// <summary>
    /// A single-token name is a valid result, not an error. Plenty of real people
    /// do not have a family name, and demanding one would make the record wrong.
    /// </summary>
    [Fact]
    public void Person_AcceptsAPersonWithNoLastName()
    {
        Person person = Person.Create(Tenant, "Bjork", lastName: null, Actor, Now);

        Assert.Equal("Bjork", person.DisplayName);
        Assert.Null(person.LastName);
    }

    [Fact]
    public void Person_UsesAnExplicitDisplayNameWhenGiven()
    {
        Person person = Person.Create(Tenant, "Robert", "Zimmerman", Actor, Now, displayName: "Bob Dylan");

        Assert.Equal("Bob Dylan", person.DisplayName);
    }

    [Fact]
    public void Person_RejectsABlankFirstName()
    {
        Assert.Throws<DomainException>(() => Person.Create(Tenant, "   ", "Okonkwo", Actor, Now));
    }

    [Fact]
    public void Person_ArchivingIsNotDeleting()
    {
        Person person = Person.Create(Tenant, "Sarah", "Okonkwo", Actor, Now);

        person.Archive(Now);

        Assert.Equal(PersonStatus.Archived, person.Status);
        Assert.Equal("Sarah Okonkwo", person.DisplayName);
        Assert.Throws<DomainException>(() => person.Archive(Now));
    }

    /// <summary>Editing an archived record would quietly resurrect it.</summary>
    [Fact]
    public void Person_CannotBeEditedWhileArchived()
    {
        Person person = Person.Create(Tenant, "Sarah", "Okonkwo", Actor, Now);
        person.Archive(Now);

        Assert.Throws<DomainException>(() => person.Update("Sarah", "Okonkwo", Now));

        person.Restore(Now);
        person.Update("Sarah", "Adeyemi", Now);

        Assert.Equal("Sarah Adeyemi", person.DisplayName);
    }

    [Fact]
    public void Company_RecordsItsKindAndStaysActiveUntilArchived()
    {
        Company company = Company.Create(Tenant, "Vertex Studios", CompanyType.Studio, Actor, Now);

        Assert.Equal(CompanyType.Studio, company.Type);
        Assert.Equal(CompanyStatus.Active, company.Status);

        company.Archive(Now);
        Assert.Equal(CompanyStatus.Archived, company.Status);
        Assert.Throws<DomainException>(() => company.Update("Vertex", CompanyType.Studio, Now));
    }

    [Fact]
    public void Company_RejectsABlankName()
    {
        Assert.Throws<DomainException>(() => Company.Create(Tenant, " ", CompanyType.Studio, Actor, Now));
    }
}

/// <summary>Interaction rules.</summary>
public sealed class InteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrganizationId Tenant = OrganizationId.New();
    private static readonly UserId Actor = UserId.New();

    /// <summary>
    /// An interaction with nobody in it records nothing, and would be invisible on
    /// every timeline, since timelines are reached through parties.
    /// </summary>
    [Fact]
    public void Record_RequiresAtLeastOneParticipant()
    {
        Assert.Throws<DomainException>(() => Interaction.Record(
            Tenant, InteractionType.Meeting, Now, "Dinner", [], Actor, Now));
    }

    [Fact]
    public void Record_RejectsTheSamePartyTwice()
    {
        RelationshipEndpoint sarah = RelationshipEndpoint.ForPerson(PersonId.New());

        Assert.Throws<DomainException>(() => Interaction.Record(
            Tenant,
            InteractionType.Meeting,
            Now,
            "Dinner",
            [(sarah, "guest"), (sarah, "host")],
            Actor,
            Now));
    }

    [Fact]
    public void Record_KeepsParticipantsAndTheirRoles()
    {
        RelationshipEndpoint sarah = RelationshipEndpoint.ForPerson(PersonId.New());
        RelationshipEndpoint studio = RelationshipEndpoint.ForCompany(CompanyId.New());

        Interaction interaction = Interaction.Record(
            Tenant,
            InteractionType.Meeting,
            Now,
            "Met Sarah at dinner",
            [(sarah, "guest"), (studio, null)],
            Actor,
            Now);

        Assert.Equal(2, interaction.Participants.Count);
        Assert.True(interaction.Involves(sarah));
        Assert.True(interaction.Involves(studio));
        Assert.False(interaction.Involves(RelationshipEndpoint.ForPerson(PersonId.New())));
        Assert.Equal("guest", interaction.Participants.First(p => p.Party == sarah).Role);
    }

    /// <summary>When it happened is not when it was recorded.</summary>
    [Fact]
    public void Record_KeepsOccurrenceAndRecordingSeparate()
    {
        DateTimeOffset lastWeek = Now.AddDays(-7);

        Interaction interaction = Interaction.Record(
            Tenant,
            InteractionType.Call,
            lastWeek,
            "Call with the studio",
            [(RelationshipEndpoint.ForPerson(PersonId.New()), null)],
            Actor,
            Now);

        Assert.Equal(lastWeek, interaction.OccurredAt);
        Assert.Equal(Now, interaction.CreatedAt);
    }

    [Fact]
    public void Record_RejectsABlankSummary()
    {
        Assert.Throws<DomainException>(() => Interaction.Record(
            Tenant,
            InteractionType.Meeting,
            Now,
            "  ",
            [(RelationshipEndpoint.ForPerson(PersonId.New()), null)],
            Actor,
            Now));
    }
}

/// <summary>Task state machine.</summary>
public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrganizationId Tenant = OrganizationId.New();
    private static readonly UserId Actor = UserId.New();

    [Fact]
    public void Create_StartsOpenWithNoCompletionStamp()
    {
        TaskItem task = TaskItem.Create(Tenant, "Send Sarah the screenplay", Actor, Now);

        Assert.Equal(TaskState.Open, task.State);
        Assert.Null(task.CompletedAt);
        Assert.Null(task.CompletedBy);
    }

    [Fact]
    public void Complete_RecordsWhoAndWhen()
    {
        TaskItem task = TaskItem.Create(Tenant, "Send the screenplay", Actor, Now);
        UserId completer = UserId.New();

        task.Complete(completer, Now.AddDays(1));

        Assert.Equal(TaskState.Completed, task.State);
        Assert.Equal(Now.AddDays(1), task.CompletedAt);
        Assert.Equal(completer, task.CompletedBy);
    }

    /// <summary>
    /// Reopening clears the completion stamp. Leaving it set would produce an open
    /// task that still claims a completion instant - exactly the inconsistency the
    /// state machine exists to prevent.
    /// </summary>
    [Fact]
    public void Reopen_ClearsTheCompletionStamp()
    {
        TaskItem task = TaskItem.Create(Tenant, "Send the screenplay", Actor, Now);
        task.Complete(Actor, Now.AddDays(1));

        task.Reopen(Now.AddDays(2));

        Assert.Equal(TaskState.Open, task.State);
        Assert.Null(task.CompletedAt);
        Assert.Null(task.CompletedBy);
    }

    [Fact]
    public void Transitions_RejectRepeats()
    {
        TaskItem task = TaskItem.Create(Tenant, "Send the screenplay", Actor, Now);

        Assert.Throws<DomainException>(() => task.Reopen(Now));

        task.Complete(Actor, Now);
        Assert.Throws<DomainException>(() => task.Complete(Actor, Now));
    }

    [Fact]
    public void IsOverdue_OnlyAppliesToOpenTasksPastTheirDueDate()
    {
        TaskItem overdue = TaskItem.Create(Tenant, "Overdue", Actor, Now, dueAt: Now.AddDays(-1));
        TaskItem upcoming = TaskItem.Create(Tenant, "Upcoming", Actor, Now, dueAt: Now.AddDays(1));
        TaskItem undated = TaskItem.Create(Tenant, "Undated", Actor, Now);

        Assert.True(overdue.IsOverdueAt(Now));
        Assert.False(upcoming.IsOverdueAt(Now));
        Assert.False(undated.IsOverdueAt(Now));

        overdue.Complete(Actor, Now);
        Assert.False(overdue.IsOverdueAt(Now));
    }

    [Fact]
    public void Subject_ReportsThePartyTheTaskConcerns()
    {
        PersonId personId = PersonId.New();

        TaskItem aboutPerson = TaskItem.Create(
            Tenant, "Call Sarah", Actor, Now, subject: RelationshipEndpoint.ForPerson(personId));

        TaskItem aboutNobody = TaskItem.Create(Tenant, "Tidy the CRM", Actor, Now);

        Assert.Equal(personId, aboutPerson.Subject?.AsPerson);
        Assert.Equal(personId, aboutPerson.RelatedPersonId);
        Assert.Null(aboutPerson.RelatedCompanyId);
        Assert.Null(aboutNobody.Subject);
    }

    [Fact]
    public void Create_RejectsABlankTitle()
    {
        Assert.Throws<DomainException>(() => TaskItem.Create(Tenant, "   ", Actor, Now));
    }
}
