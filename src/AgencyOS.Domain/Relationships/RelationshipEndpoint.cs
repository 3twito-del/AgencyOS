using AgencyOS.Domain.Companies;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Relationships;

/// <summary>The kinds of party a relationship or interaction can involve.</summary>
/// <remarks>
/// Two kinds in M2. <c>docs/01_PRODUCT_SPEC.md</c> anticipates more (Team,
/// Office); adding one is an additive migration, see ADR-0011.
/// </remarks>
public enum PartyKind
{
    Person = 1,
    Company = 2,
}

/// <summary>
/// One end of a relationship: a party of a known kind.
/// </summary>
/// <remarks>
/// The domain's uniform way of naming an endpoint. Persistence stores it as an
/// exclusive arc of real foreign keys rather than as an opaque pair, so the
/// database can still tell whether the endpoint exists (ADR-0011).
/// </remarks>
/// <param name="Kind">Which kind of party this endpoint refers to.</param>
/// <param name="Id">Identifier of the party.</param>
public readonly record struct RelationshipEndpoint(PartyKind Kind, Guid Id)
{
    public static RelationshipEndpoint ForPerson(PersonId id) => new(PartyKind.Person, id.Value);

    public static RelationshipEndpoint ForCompany(CompanyId id) => new(PartyKind.Company, id.Value);

    public bool IsPerson => Kind == PartyKind.Person;

    public bool IsCompany => Kind == PartyKind.Company;

    /// <summary>Gets this endpoint as a person identifier, when it is one.</summary>
    public PersonId? AsPerson => IsPerson ? new PersonId(Id) : null;

    /// <summary>Gets this endpoint as a company identifier, when it is one.</summary>
    public CompanyId? AsCompany => IsCompany ? new CompanyId(Id) : null;

    public override string ToString() => $"{Kind}:{Id}";
}
