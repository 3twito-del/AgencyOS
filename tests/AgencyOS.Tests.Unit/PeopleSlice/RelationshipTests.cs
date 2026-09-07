using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using Xunit;

namespace AgencyOS.Tests.Unit.PeopleSlice;

/// <summary>
/// Which pairings each relationship type accepts, and the invariants a
/// relationship must satisfy however it is created.
/// </summary>
/// <remarks>
/// These are the semantic rules that live in the domain rather than in a check
/// constraint: the database cannot know that employing yourself is meaningless
/// (ADR-0011).
/// </remarks>
public sealed class RelationshipTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly OrganizationId Tenant = OrganizationId.New();
    private static readonly UserId Actor = UserId.New();

    [Theory]
    [InlineData(RelationshipType.Employment, PartyKind.Person, PartyKind.Company, true)]
    [InlineData(RelationshipType.Employment, PartyKind.Person, PartyKind.Person, false)]
    [InlineData(RelationshipType.Employment, PartyKind.Company, PartyKind.Company, false)]
    [InlineData(RelationshipType.Affiliation, PartyKind.Person, PartyKind.Company, true)]
    [InlineData(RelationshipType.Colleague, PartyKind.Person, PartyKind.Person, true)]
    [InlineData(RelationshipType.Colleague, PartyKind.Person, PartyKind.Company, false)]
    [InlineData(RelationshipType.Introduction, PartyKind.Person, PartyKind.Person, true)]
    [InlineData(RelationshipType.Collaboration, PartyKind.Person, PartyKind.Person, true)]
    [InlineData(RelationshipType.Advisor, PartyKind.Person, PartyKind.Company, true)]
    [InlineData(RelationshipType.Advisor, PartyKind.Company, PartyKind.Company, true)]
    [InlineData(RelationshipType.Counsel, PartyKind.Company, PartyKind.Company, true)]
    [InlineData(RelationshipType.Other, PartyKind.Company, PartyKind.Person, true)]
    public void TypeRules_ConstrainEndpointPairings(
        RelationshipType type,
        PartyKind from,
        PartyKind to,
        bool expected)
    {
        Assert.Equal(expected, RelationshipTypeRules.AllowsPairing(type, from, to));
    }

    [Theory]
    [InlineData(RelationshipType.Colleague, RelationshipDirection.Mutual)]
    [InlineData(RelationshipType.Collaboration, RelationshipDirection.Mutual)]
    [InlineData(RelationshipType.Employment, RelationshipDirection.Directed)]
    [InlineData(RelationshipType.Introduction, RelationshipDirection.Directed)]
    public void TypeRules_HaveANaturalDirection(RelationshipType type, RelationshipDirection expected)
    {
        Assert.Equal(expected, RelationshipTypeRules.DefaultDirection(type));
    }

    /// <summary>No M2 type permits a party to relate to itself.</summary>
    [Theory]
    [InlineData(RelationshipType.Employment)]
    [InlineData(RelationshipType.Colleague)]
    [InlineData(RelationshipType.Advisor)]
    [InlineData(RelationshipType.Other)]
    public void TypeRules_RefuseSelfReference(RelationshipType type)
    {
        Assert.False(RelationshipTypeRules.PermitsSelfReference(type));
    }

    [Fact]
    public void Create_RejectsAPairingTheTypeDoesNotAccept()
    {
        RelationshipEndpoint person = RelationshipEndpoint.ForPerson(PersonId.New());
        RelationshipEndpoint other = RelationshipEndpoint.ForPerson(PersonId.New());

        DomainException error = Assert.Throws<DomainException>(() => ProfessionalRelationship.Create(
            Tenant, person, other, RelationshipType.Employment, Actor, Now));

        // The message names what the type does accept, so the caller can fix it.
        Assert.Contains("person to company", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsASelfRelationship()
    {
        RelationshipEndpoint person = RelationshipEndpoint.ForPerson(PersonId.New());

        Assert.Throws<DomainException>(() => ProfessionalRelationship.Create(
            Tenant, person, person, RelationshipType.Colleague, Actor, Now));
    }

    [Fact]
    public void Create_RejectsAnEndDateBeforeTheStartDate()
    {
        Assert.Throws<DomainException>(() => ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(PersonId.New()),
            RelationshipEndpoint.ForCompany(CompanyId.New()),
            RelationshipType.Employment,
            Actor,
            Now,
            startedAt: Now,
            endedAt: Now.AddDays(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Create_RejectsStrengthOutsideTheScale(int strength)
    {
        Assert.Throws<DomainException>(() => Employment(strength: strength));
    }

    [Fact]
    public void Create_UsesTheTypesNaturalDirectionWhenNoneIsGiven()
    {
        ProfessionalRelationship colleagues = ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(PersonId.New()),
            RelationshipEndpoint.ForPerson(PersonId.New()),
            RelationshipType.Colleague,
            Actor,
            Now);

        Assert.Equal(RelationshipDirection.Mutual, colleagues.Direction);
    }

    [Fact]
    public void Create_ExposesEndpointsAsPartyValues()
    {
        PersonId personId = PersonId.New();
        CompanyId companyId = CompanyId.New();

        ProfessionalRelationship relationship = ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(personId),
            RelationshipEndpoint.ForCompany(companyId),
            RelationshipType.Employment,
            Actor,
            Now);

        // The arc is four columns underneath; the domain talks in parties.
        Assert.Equal(personId, relationship.From.AsPerson);
        Assert.Null(relationship.From.AsCompany);
        Assert.Equal(companyId, relationship.To.AsCompany);
        Assert.Equal(personId, relationship.FromPersonId);
        Assert.Equal(companyId, relationship.ToCompanyId);
        Assert.Null(relationship.FromCompanyId);
        Assert.Null(relationship.ToPersonId);
    }

    [Fact]
    public void End_RetainsTheRelationshipAsHistory()
    {
        ProfessionalRelationship relationship = Employment();
        UserId ender = UserId.New();

        relationship.End(ender, Now.AddYears(1), Now.AddYears(1));

        Assert.Equal(RelationshipStatus.Ended, relationship.Status);
        Assert.Equal(Now.AddYears(1), relationship.EndedAt);
        Assert.Equal(ender, relationship.EndedBy);

        // Still a relationship, still describable. Nothing was deleted.
        Assert.Equal(RelationshipType.Employment, relationship.Type);
    }

    [Fact]
    public void End_RejectsAnEndBeforeTheStart()
    {
        ProfessionalRelationship relationship = ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(PersonId.New()),
            RelationshipEndpoint.ForCompany(CompanyId.New()),
            RelationshipType.Employment,
            Actor,
            Now,
            startedAt: Now);

        Assert.Throws<DomainException>(() => relationship.End(Actor, Now.AddDays(-1), Now));
    }

    [Fact]
    public void End_CannotHappenTwice()
    {
        ProfessionalRelationship relationship = Employment();
        relationship.End(Actor, Now, Now);

        Assert.Throws<DomainException>(() => relationship.End(Actor, Now, Now));
    }

    [Fact]
    public void Involves_IdentifiesEitherEndpoint()
    {
        PersonId personId = PersonId.New();
        CompanyId companyId = CompanyId.New();

        ProfessionalRelationship relationship = ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(personId),
            RelationshipEndpoint.ForCompany(companyId),
            RelationshipType.Employment,
            Actor,
            Now);

        Assert.True(relationship.Involves(RelationshipEndpoint.ForPerson(personId)));
        Assert.True(relationship.Involves(RelationshipEndpoint.ForCompany(companyId)));
        Assert.False(relationship.Involves(RelationshipEndpoint.ForPerson(PersonId.New())));
    }

    private static ProfessionalRelationship Employment(int? strength = null) =>
        ProfessionalRelationship.Create(
            Tenant,
            RelationshipEndpoint.ForPerson(PersonId.New()),
            RelationshipEndpoint.ForCompany(CompanyId.New()),
            RelationshipType.Employment,
            Actor,
            Now,
            strength: strength);
}
