using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// Whether a name a surface shows is attributable to the role it actually holds.
/// </summary>
/// <remarks>
/// <para>
/// These assert <em>role = value</em>, not that a value appears. A test proving
/// that both names occur somewhere in a row passes whichever way round they are,
/// which is precisely the failure it is meant to catch, so every case here has a
/// counterpart that exchanges two identities and requires the description to
/// change with them.
/// </para>
/// <para>
/// <strong>Both channels are covered from one place.</strong> The visible caption
/// and the announced row read the same helpers: the converters bound in markup
/// delegate to <see cref="TaskLine"/> and <see cref="TargetLine"/> in a single
/// expression each, and <see cref="RowLabel"/> calls the same two. So asserting
/// the helpers, and separately that the templates bind those converters
/// (<c>TaskSurfaceSemanticsTests</c>), covers what the operator sees and what they
/// hear without a window. Where the two channels could still disagree — different
/// helper, different order, one dropping a role — that is asserted directly.
/// </para>
/// </remarks>
public sealed class SemanticRoleTests
{
    private static readonly Guid Member = Guid.Parse("01a0a27a-c0d5-7a63-830d-b6a1d4f85a88");

    private static readonly Guid Lead = Guid.Parse("01a0a015-c41c-7811-9c22-8cece1228f1b");

    private static readonly DateTimeOffset Due = new(2026, 10, 9, 17, 0, 0, TimeSpan.Zero);

    private const string Client = "Marisol Thorneycroft-Bassey";

    private const string Buyer = "Evander Quillon-Mbeki";

    private const string Worker = "Review member";

    private const string Responsible = "Review Owner";

    private static TaskResponse Task(
        string title = "Send Harrowgate the updated availability window",
        string? subject = Client,
        Guid? assignee = null,
        string? assigneeName = null,
        DateTimeOffset? due = null) =>
        new(Guid.NewGuid(), title, "Open", "High", due,
            subject is null ? null : new PartyReferenceResponse("Person", Guid.NewGuid(), subject),
            null, DateTimeOffset.UtcNow, null, 1, assignee, assigneeName);

    private static OpportunityTargetResponse Target(
        string party = "Harrowgate Lune Pictures",
        string? contact = Buyer,
        string? owner = Responsible,
        bool person = false) =>
        new(Guid.NewGuid(),
            person ? null : Guid.NewGuid(),
            person ? Guid.NewGuid() : null,
            party,
            contact is null ? null : Guid.NewGuid(),
            contact,
            "Interested",
            true,
            owner is null ? null : Lead,
            owner,
            null, null, null, 0, null, 0, null, null, null,
            DateTimeOffset.UtcNow,
            1);

    // ------------------------------------------------------------- task roles

    /// <summary>
    /// A task about one person and assigned to another says which is which.
    /// </summary>
    /// <remarks>
    /// The build-82 Command Center row: subject and assignee are different people,
    /// and position was the only thing distinguishing them in the announcement.
    /// </remarks>
    [Fact]
    public void SubjectAndAssigneeAreEachAttributedToTheirOwnRole()
    {
        TaskResponse row = Task(assignee: Member, assigneeName: Worker, due: Due);

        Assert.Equal($"About {Client}", TaskLine.About(row));
        Assert.Equal($"Assigned to {Worker}", TaskLine.Who(row));

        string announced = RowLabel.For(row);

        Assert.Contains($"About {Client}", announced, StringComparison.Ordinal);
        Assert.Contains($"Assigned to {Worker}", announced, StringComparison.Ordinal);
    }

    /// <summary>
    /// Exchanging the two people changes what the row says about each of them.
    /// </summary>
    /// <remarks>
    /// The assertion build 82 lacked. A row that named both people passed its test
    /// whichever role each held; this one fails if the subject is described as the
    /// assignee or the other way about.
    /// </remarks>
    [Fact]
    public void SwappingSubjectAndAssigneeSwapsWhatIsSaidAboutThem()
    {
        string straight = RowLabel.For(Task(subject: Client, assignee: Member, assigneeName: Worker));
        string swapped = RowLabel.For(Task(subject: Worker, assignee: Member, assigneeName: Client));

        Assert.Contains($"About {Client}", straight, StringComparison.Ordinal);
        Assert.Contains($"Assigned to {Worker}", straight, StringComparison.Ordinal);

        Assert.Contains($"About {Worker}", swapped, StringComparison.Ordinal);
        Assert.Contains($"Assigned to {Client}", swapped, StringComparison.Ordinal);

        Assert.DoesNotContain($"About {Worker}", straight, StringComparison.Ordinal);
        Assert.DoesNotContain($"About {Client}", swapped, StringComparison.Ordinal);
    }

    /// <summary>Neither role is left as a bare name in the announced row.</summary>
    [Fact]
    public void NeitherIdentityIsAnnouncedWithoutItsRole()
    {
        string announced = RowLabel.For(Task(assignee: Member, assigneeName: Worker, due: Due));

        foreach (string name in (string[])[Client, Worker])
        {
            int at = announced.IndexOf(name, StringComparison.Ordinal);

            Assert.True(at > 0, $"{name} was not announced at all: {announced}");

            string before = announced[..at];

            Assert.True(
                before.EndsWith("About ", StringComparison.Ordinal)
                    || before.EndsWith("Assigned to ", StringComparison.Ordinal),
                $"{name} was announced with no role in front of it: {announced}");
        }
    }

    /// <summary>
    /// A genuinely unassigned task keeps its subject distinct from the disclaimer.
    /// </summary>
    [Fact]
    public void AnUnassignedTaskStillSaysWhatItIsAbout()
    {
        string announced = RowLabel.For(Task(subject: "Ottoline Brackenridge-Osei"));

        Assert.Contains("About Ottoline Brackenridge-Osei", announced, StringComparison.Ordinal);
        Assert.Contains("Unassigned", announced, StringComparison.Ordinal);
        Assert.DoesNotContain("Assigned to", announced, StringComparison.Ordinal);
    }

    /// <summary>
    /// An assignment whose name did not resolve is never reported as nobody's.
    /// </summary>
    /// <remarks>
    /// The build-80 invariant, restated against the announced row rather than the
    /// helper, because that is the channel this build changed.
    /// </remarks>
    [Fact]
    public void AnAssignedTaskWithNoResolvedNameIsNotAnnouncedAsUnassigned()
    {
        string announced = RowLabel.For(Task(assignee: Member, assigneeName: null, due: Due));

        Assert.Contains("Assigned, name unavailable", announced, StringComparison.Ordinal);
        Assert.DoesNotContain("Unassigned", announced, StringComparison.Ordinal);
    }

    /// <summary>No due date is said, not invented.</summary>
    [Fact]
    public void AnUndatedTaskAnnouncesNoDate()
    {
        string announced = RowLabel.For(Task(assignee: Member, assigneeName: Worker));

        Assert.Contains("No due date", announced, StringComparison.Ordinal);
        Assert.DoesNotContain("Due 20", announced, StringComparison.Ordinal);
    }

    /// <summary>
    /// A projection that cannot express assignment stays silent in the announcement.
    /// </summary>
    /// <remarks>
    /// Contract tasks carry no assignee field. Silence is the truthful answer and
    /// build 83 does not add one to make the rows look alike.
    /// </remarks>
    [Fact]
    public void ANonAuthoritativeTaskRowAnnouncesNoOwnershipAtAll()
    {
        ContractTaskResponse row =
            new(Guid.NewGuid(), "Get the stop-date clause into the execution copy",
                "Open", "High", Due, null, null);

        string announced = RowLabel.For(row);

        Assert.DoesNotContain("Unassigned", announced, StringComparison.Ordinal);
        Assert.DoesNotContain("Assigned", announced, StringComparison.Ordinal);
        Assert.Contains("Due 2026-10-09", announced, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- long title

    /// <summary>
    /// A long title costs the title, not the answer to who and when.
    /// </summary>
    /// <remarks>
    /// Build 82 appended these last, so they were cut first: the blind operator
    /// found that the longer a task was, the less they were told about it. The row
    /// keeps a bound; what yields inside it changed.
    /// </remarks>
    [Fact]
    public void ALongTitleDoesNotCostTheOperatorWhoAndWhen()
    {
        string title = string.Join(
            " ",
            Enumerable.Repeat("chase the completion bond paperwork through legal", 8));

        string announced = RowLabel.For(
            Task(title: title, assignee: Member, assigneeName: Worker, due: Due));

        Assert.Contains($"Assigned to {Worker}", announced, StringComparison.Ordinal);
        Assert.Contains("Due 2026-10-09", announced, StringComparison.Ordinal);
        Assert.Contains($"About {Client}", announced, StringComparison.Ordinal);
        Assert.Contains("…", announced, StringComparison.Ordinal);
    }

    /// <summary>The bound survives the change.</summary>
    [Fact]
    public void ALongTaskRowIsStillShortEnoughToListenTo()
    {
        string title = new('x', 4000);

        Assert.True(
            RowLabel.For(Task(title: title, assignee: Member, assigneeName: Worker, due: Due))
                .Length <= 160);
    }

    // ----------------------------------------------------------- target roles

    /// <summary>
    /// The counterparty individual is named as one.
    /// </summary>
    /// <remarks>
    /// "Contact:" rather than "Contact", because the second reads as an
    /// instruction to ring him.
    /// </remarks>
    [Fact]
    public void ACompanyTargetNamesItsContactAsAContact()
    {
        Assert.Equal($"Contact: {Buyer}", TargetLine.Contact(Target()));
    }

    /// <summary>
    /// The visible caption and the announced row describe the same person.
    /// </summary>
    /// <remarks>
    /// Before build 83 they described different people: the caption bound
    /// <c>ContactDisplayName</c> and <see cref="RowLabel"/> had no entry for it, so
    /// it announced <c>OwnerDisplayName</c> in the same position. A sighted
    /// operator heard about the casting director and a screen-reader operator about
    /// an internal colleague.
    /// </remarks>
    [Fact]
    public void TheSeenAndTheAnnouncedTargetRowNameTheSamePersonInTheSameRole()
    {
        OpportunityTargetResponse row = Target();

        string seen = TargetLine.Contact(row)!;
        string announced = RowLabel.For(row);

        Assert.Contains(seen, announced, StringComparison.Ordinal);
        Assert.Contains($"Contact: {Buyer}", announced, StringComparison.Ordinal);
        Assert.DoesNotContain(Responsible, announced, StringComparison.Ordinal);
    }

    /// <summary>
    /// Exchanging the contact and the owner changes who is called the contact.
    /// </summary>
    [Fact]
    public void SwappingContactAndOwnerSwapsWhoIsCalledTheContact()
    {
        string straight = RowLabel.For(Target(contact: Buyer, owner: Responsible));
        string swapped = RowLabel.For(Target(contact: Responsible, owner: Buyer));

        Assert.Contains($"Contact: {Buyer}", straight, StringComparison.Ordinal);
        Assert.Contains($"Contact: {Responsible}", swapped, StringComparison.Ordinal);

        Assert.DoesNotContain($"Contact: {Responsible}", straight, StringComparison.Ordinal);
        Assert.DoesNotContain($"Contact: {Buyer}", swapped, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person target has no contact, and no contact role is invented for it.
    /// </summary>
    /// <remarks>
    /// The domain refuses a contact on a person target — the person approached is
    /// who you are dealing with — so the row says nothing rather than borrowing the
    /// role for whoever else it knows about.
    /// </remarks>
    [Fact]
    public void APersonTargetHasNoContactRole()
    {
        OpportunityTargetResponse row = Target(
            party: "Ottoline Brackenridge-Osei", contact: null, owner: Responsible, person: true);

        Assert.Null(TargetLine.Contact(row));

        string announced = RowLabel.For(row);

        Assert.DoesNotContain("Contact:", announced, StringComparison.Ordinal);
        Assert.Contains("Ottoline Brackenridge-Osei", announced, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the row names the responsible member instead, it says that too.
    /// </summary>
    /// <remarks>
    /// A target row announces one person. With no contact it falls to the owner,
    /// and an unattributed second name beside the party approached is the same
    /// ambiguity in a different order.
    /// </remarks>
    [Fact]
    public void AnAnnouncedOwnerIsCalledAnOwner()
    {
        string announced = RowLabel.For(
            Target(party: "Ottoline Brackenridge-Osei", contact: null, person: true));

        Assert.Contains($"Owner: {Responsible}", announced, StringComparison.Ordinal);
    }

    /// <summary>No target row announces a person with no role at all.</summary>
    [Fact]
    public void NoTargetRowAnnouncesABareIdentity()
    {
        foreach (OpportunityTargetResponse row in (OpportunityTargetResponse[])
                 [Target(), Target(contact: null), Target(owner: null)])
        {
            string announced = RowLabel.For(row);

            foreach (string name in (string[])[Buyer, Responsible])
            {
                int at = announced.IndexOf(name, StringComparison.Ordinal);

                if (at < 0)
                {
                    continue;
                }

                Assert.True(
                    announced[..at].EndsWith("Contact: ", StringComparison.Ordinal)
                        || announced[..at].EndsWith("Owner: ", StringComparison.Ordinal),
                    $"{name} was announced with no role in front of it: {announced}");
            }
        }
    }

    /// <summary>Identifiers stay out of both channels.</summary>
    [Fact]
    public void NoTargetIdentifierIsEverSpoken()
    {
        OpportunityTargetResponse row = Target();

        string said = RowLabel.For(row) + " " + TargetLine.Contact(row);

        Assert.DoesNotContain(row.Id.ToString(), said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            row.ContactPersonId!.Value.ToString(), said, StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------- related channel

    /// <summary>
    /// The target picker keeps the contact's role.
    /// </summary>
    /// <remarks>
    /// <c>EntityChoice</c> labels a target with the company, the stage and the
    /// contact. Three values, one of them a person, and nothing said which — the
    /// same defect as the row, in the control that chooses one.
    /// </remarks>
    [Fact]
    public void TheTargetPickerNamesTheContactAsAContact()
    {
        EntityChoice choice = Assert.Single(EntityChoice.ForTargets([Target()]));

        Assert.Contains($"Contact: {Buyer}", choice.Label, StringComparison.Ordinal);
        Assert.Contains("Harrowgate Lune Pictures", choice.Label, StringComparison.Ordinal);
    }

    /// <summary>A target with no contact offers no empty role in the picker.</summary>
    [Fact]
    public void TheTargetPickerSaysNothingWhereThereIsNoContact()
    {
        EntityChoice choice = Assert.Single(EntityChoice.ForTargets([Target(contact: null)]));

        Assert.DoesNotContain("Contact:", choice.Label, StringComparison.Ordinal);
    }
}
