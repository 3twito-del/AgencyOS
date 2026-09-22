using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A row says what each person named on it is to the record.
/// </summary>
/// <remarks>
/// <para>
/// F-04, F-09 and F-10. The caption slot beneath a headline held the counterparty
/// on one surface, the internal member responsible on another and whoever
/// performed an action on a third, and in every case it held a bare name. Position
/// is not a statement: a name in the place a byline goes is read as accountability
/// wherever it appears, and on half of these rows that reading was wrong.
/// </para>
/// <para>
/// These execute the shipped formatter against the shipped response types and
/// assert <strong>which name holds which role</strong>. Every fixture gives its
/// two identities different names, so no test here can pass by finding both names
/// somewhere in the row: swapping them fails.
/// </para>
/// </remarks>
public sealed class IdentityRoleTests
{
    private const string Contact = "Evander Quillon-Mbeki";
    private const string Owner = "Perrin Halloway";
    private const string Party = "Corvid Ash Pictures";
    private const string Payee = "Thessaly Vane";

    // ---------------------------------------------------------------- targets

    private static OpportunityTargetResponse Target(
        string? contact = Contact, string? owner = Owner) =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, Party, Guid.NewGuid(), contact,
            "Contacted", true, Guid.NewGuid(), owner, null, null, null, 0, null, 0,
            null, null, null, DateTimeOffset.UtcNow, 1);

    /// <summary>
    /// A target row names the contact, and says that is who it is.
    /// </summary>
    /// <remarks>
    /// The row carries three identities - the company approached, the individual
    /// dealt with there and the internal member responsible - and named one of
    /// them with nothing saying which.
    /// </remarks>
    [Fact]
    public void ATargetRowSaysWhichPersonIsTheContact()
    {
        string spoken = RowLabel.For(Target());

        Assert.Contains("Contact: " + Contact, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Contact: " + Owner, spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Swapping the two people changes which role each is announced in.
    /// </summary>
    /// <remarks>
    /// The assertion that makes the one above mean something. A row that announced
    /// both names unattributed would satisfy a test that only looked for them.
    /// </remarks>
    [Fact]
    public void SwappingTheContactAndTheOwnerChangesWhatATargetRowSays()
    {
        string right = RowLabel.For(Target(contact: Contact, owner: Owner));
        string swapped = RowLabel.For(Target(contact: Owner, owner: Contact));

        Assert.NotEqual(right, swapped);
        Assert.Contains("Contact: " + Contact, right, StringComparison.Ordinal);
        Assert.Contains("Contact: " + Owner, swapped, StringComparison.Ordinal);
    }

    /// <summary>
    /// A target with no contact says the owner is the owner, not the contact.
    /// </summary>
    /// <remarks>
    /// A person target has no contact - the domain refuses one, because the person
    /// approached is who you are dealing with - so the row falls to the internal
    /// member and must not inherit the role it did not fill.
    /// </remarks>
    [Fact]
    public void ATargetWithNoContactAttributesTheOwnerAsTheOwner()
    {
        string spoken = RowLabel.For(Target(contact: null));

        Assert.Contains("Owner: " + Owner, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Contact:", spoken, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- prospects

    private static ProspectResponse Prospect(string person = Party, string? owner = Owner) =>
        new(Guid.NewGuid(), Guid.NewGuid(), person, "Courting", Guid.NewGuid(), owner,
            null, null, new DateOnly(2026, 4, 2), null, null, DateTimeOffset.UtcNow, 1);

    /// <summary>
    /// A prospect row leads with the person being courted and labels the owner.
    /// </summary>
    /// <remarks>
    /// Two people, one above the other, both bare, in both channels. The prospect
    /// keeps its name unlabelled because it is the row's subject and the only role
    /// it can hold on a list of prospects; the second name is the one that needed
    /// saying.
    /// </remarks>
    [Fact]
    public void AProspectRowAttributesItsOwnerAndLeavesTheProspectBare()
    {
        string spoken = RowLabel.For(Prospect());

        Assert.StartsWith(Party, spoken, StringComparison.Ordinal);
        Assert.Contains("Owner: " + Owner, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner: " + Party, spoken, StringComparison.Ordinal);
    }

    /// <summary>Swapping the prospect and the owner changes what the row says.</summary>
    [Fact]
    public void SwappingTheProspectAndTheOwnerChangesWhatTheRowSays()
    {
        string right = RowLabel.For(Prospect(person: Party, owner: Owner));
        string swapped = RowLabel.For(Prospect(person: Owner, owner: Party));

        Assert.NotEqual(right, swapped);
        Assert.Contains("Owner: " + Owner, right, StringComparison.Ordinal);
        Assert.Contains("Owner: " + Party, swapped, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prospect nobody owns says nothing about ownership.
    /// </summary>
    /// <remarks>
    /// Silence rather than a claim. "Unassigned" is a statement about ownership,
    /// and a row that cannot establish one should not make it.
    /// </remarks>
    [Fact]
    public void AProspectWithNoOwnerMakesNoClaimAboutOwnership()
    {
        string spoken = RowLabel.For(Prospect(owner: null));

        Assert.DoesNotContain("Owner:", spoken, StringComparison.Ordinal);
        Assert.Contains(Party, spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- two payers

    private static PaymentResponse Payment(string payer = Party, string? payee = Payee) =>
        new(Guid.NewGuid(), "Incoming", Guid.NewGuid(), payer, Guid.NewGuid(), payee,
            new MoneyResponse(450000m, "USD"), new MoneyResponse(200000m, "USD"),
            new MoneyResponse(250000m, "USD"), new DateOnly(2026, 7, 10),
            DateTimeOffset.UtcNow, "BankTransfer", "WIRE-88213", null, "Recorded",
            null, null, null, null, null, [], 1);

    /// <summary>
    /// A payment names the payer as the payer, with a payee on the same record.
    /// </summary>
    /// <remarks>
    /// The strongest case in the product for saying the role: the row carries two
    /// parties to the same transaction, shows one of them bare, and the direction
    /// decides which of the two the agency is. A bare name here is a coin toss.
    /// </remarks>
    [Fact]
    public void APaymentRowSaysWhichPartyIsThePayer()
    {
        string spoken = RowLabel.For(Payment());

        Assert.Contains("Payer: " + Party, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Payer: " + Payee, spoken, StringComparison.Ordinal);
    }

    /// <summary>Swapping the payer and the payee changes what the row says.</summary>
    [Fact]
    public void SwappingThePayerAndThePayeeChangesWhatTheRowSays()
    {
        string right = RowLabel.For(Payment(payer: Party, payee: Payee));
        string swapped = RowLabel.For(Payment(payer: Payee, payee: Party));

        Assert.NotEqual(right, swapped);
        Assert.Contains("Payer: " + Party, right, StringComparison.Ordinal);
        Assert.Contains("Payer: " + Payee, swapped, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- money rows

    /// <summary>
    /// A receivable says who owes it, and still says how much.
    /// </summary>
    /// <remarks>
    /// The party is added to a row that wave 7 filled with figures, so this also
    /// pins that the row did not spend its announcement budget on the name and
    /// drop what it was repaired to say.
    /// </remarks>
    [Fact]
    public void AReceivableSaysWhoOwesItAndStillSaysHowMuch()
    {
        ReceivableResponse receivable = new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Halloway / Corvid Ash",
            Guid.NewGuid(), Party, "Client", Guid.NewGuid(), Owner,
            new MoneyResponse(240000m, "USD"), new MoneyResponse(90000m, "USD"),
            new MoneyResponse(0m, "USD"), new MoneyResponse(150000m, "USD"),
            new DateOnly(2026, 8, 1), "Open", false, "CIN-0001", null, null,
            DateTimeOffset.UtcNow, 1);

        string spoken = RowLabel.For(receivable);

        Assert.Contains("Payer: " + Party, spoken, StringComparison.Ordinal);
        Assert.Contains("150,000.00 USD outstanding", spoken, StringComparison.Ordinal);
    }

    /// <summary>An invoice says who is billed, in the debtor's own word.</summary>
    [Fact]
    public void AnInvoiceSaysWhoIsBilled()
    {
        InvoiceResponse invoice = new(
            Guid.NewGuid(), "GH-2026-0041", Guid.NewGuid(), "Halloway / Corvid Ash",
            Guid.NewGuid(), Party, "Issued", new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1), new MoneyResponse(450000m, "USD"),
            new MoneyResponse(120000m, "USD"), false, false, null, null, null, [],
            DateTimeOffset.UtcNow, 1);

        string spoken = RowLabel.For(invoice);

        Assert.Contains("Debtor: " + Party, spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ radar

    private static TalentRadarResponse Radar(string person = Contact, string? owner = Owner) =>
        new(Guid.NewGuid(), Guid.NewGuid(), person, null, Party, "Watching", "High",
            "Three festival leads in a year.", null, "Internal", owner,
            DateTimeOffset.UtcNow, null, null, 4, 1);

    /// <summary>
    /// A radar row leads with the person it is about, as the screen does.
    /// </summary>
    /// <remarks>
    /// It carried no property the headline vocabulary knew, so the announcement
    /// opened with its own type name and then filled the context slot with
    /// <c>CompanyName</c>: the seen row headed by a person and the spoken row by a
    /// company. Both channels now lead with the same identity.
    /// </remarks>
    [Fact]
    public void ARadarRowLeadsWithThePersonItIsAbout()
    {
        string spoken = RowLabel.For(Radar());

        Assert.StartsWith(Contact, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Talent radar", spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// The person a radar row is about is never announced as its owner.
    /// </summary>
    /// <remarks>
    /// The row carries both, and the one the screen shows is the subject.
    /// </remarks>
    [Fact]
    public void ARadarRowDoesNotAnnounceItsSubjectAsItsOwner()
    {
        string spoken = RowLabel.For(Radar());

        Assert.Contains("Owner: " + Owner, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner: " + Contact, spoken, StringComparison.Ordinal);
    }

    /// <summary>A row never announces the same person twice.</summary>
    /// <remarks>
    /// Where the headline is already the person the role would name, repeating it
    /// under a label spends the announcement on nothing.
    /// </remarks>
    [Fact]
    public void ARowDoesNotNameTheSamePersonTwice()
    {
        string spoken = RowLabel.For(Radar(person: Contact, owner: Contact));

        Assert.Equal(1, spoken.Split(Contact, StringSplitOptions.None).Length - 1);
    }

    // ------------------------------------------------------------- vocabulary

    /// <summary>
    /// Whoever performed an action is never called an actor.
    /// </summary>
    /// <remarks>
    /// In an agency for performers that word is a discipline:
    /// <c>AddProjectRoleDialog</c> and the talent filters both offer "Actor"
    /// meaning somebody who acts. A history row reading "Actor: Ravensworth" would
    /// assert a profession the record never claimed - a false role attribution
    /// introduced by the repair for missing role attribution.
    /// </remarks>
    [Fact]
    public void WhoeverActedIsNeverCalledAnActor()
    {
        Assert.Equal("By", PartyLine.Role("ActorDisplayName"));
        Assert.DoesNotContain(PartyLine.Fields, x => PartyLine.Role(x) == "Actor");
    }

    /// <summary>No two roles share a word.</summary>
    /// <remarks>
    /// Two fields labelled alike would put the reader back where they started,
    /// with a role word that does not tell them which role.
    /// </remarks>
    [Fact]
    public void EveryRoleIsSaidInItsOwnWord()
    {
        List<string> words = [.. PartyLine.Fields.Select(x => PartyLine.Role(x)!)];

        Assert.Equal(words.Count, words.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A field the vocabulary does not know is not labelled with a guess.
    /// </summary>
    /// <remarks>
    /// Silence, not the bare name. A caption that quietly fell back to an
    /// unattributed name would look repaired and be exactly the defect.
    /// </remarks>
    [Fact]
    public void AFieldWithNoKnownRoleIsNotLabelled()
    {
        Assert.Null(PartyLine.For(Target(), "DisplayName"));
        Assert.Null(PartyLine.Role("DisplayName"));
        Assert.Null(PartyLine.For(Target(), null));
    }

    /// <summary>
    /// The two channels read one answer out of one vocabulary.
    /// </summary>
    /// <remarks>
    /// What the caption converter renders is the phrase that appears inside the
    /// announcement, character for character. This is the property build 83
    /// established for targets and that the two channels had lost everywhere else.
    /// </remarks>
    [Theory]
    [InlineData(Contact, "ContactDisplayName")]
    [InlineData(null, "OwnerDisplayName")]
    public void WhatIsSeenIsWhatIsAnnounced(string? contact, string field)
    {
        OpportunityTargetResponse row = Target(contact: contact);

        string? seen = PartyLine.For(row, field);

        Assert.NotNull(seen);
        Assert.Contains(seen, RowLabel.For(row), StringComparison.Ordinal);
    }
}
