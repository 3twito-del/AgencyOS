using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Legal;

/// <summary>
/// What a party is doing in the agreement.
/// </summary>
/// <remarks>
/// Deliberately separate from what kind of record the party is. A company can be
/// the studio on one contract and the licensee on another, and a person can be the
/// artist here and the guarantor there; collapsing role into Person/Company type
/// would make both unsayable (ADR-0022).
/// </remarks>
public enum ContractPartyRole
{
    /// <summary>The performer, writer, director or other individual rendering services.</summary>
    Artist = 1,

    Producer = 2,
    Studio = 3,
    Network = 4,

    /// <summary>The party engaging the services.</summary>
    Employer = 5,

    /// <summary>The entity lending an individual's services.</summary>
    Lender = 6,

    /// <summary>The individual's own loan-out company.</summary>
    LoanOut = 7,

    /// <summary>The party granting rights.</summary>
    Licensor = 8,

    /// <summary>The party receiving rights.</summary>
    Licensee = 9,

    Guarantor = 10,

    /// <summary>The agency itself, where it is genuinely a party.</summary>
    Agency = 11,

    Other = 99,
}

/// <summary>
/// Who a contract party is.
/// </summary>
/// <remarks>
/// <para>
/// An exclusive arc of typed, tenant-qualified identifiers, or - only where the
/// agency genuinely has no record - a name in text with its provenance. Free text
/// is never the identity of somebody AgencyOS already knows, because a contract
/// naming "Northgate Pictures" as a string is a contract nothing can join to the
/// company, the deal or the pipeline.
/// </para>
/// <para>
/// The text case exists because counterparties' lenders, guarantors and counsel
/// routinely appear on paper before anybody creates a record for them, and
/// refusing to record the contract until somebody does would be the system
/// obstructing the work (ADR-0022).
/// </para>
/// </remarks>
/// <param name="PersonId">The person, when the party is one AgencyOS knows.</param>
/// <param name="CompanyId">The company, when the party is one AgencyOS knows.</param>
/// <param name="ExternalName">The party's name, when AgencyOS has no record of them.</param>
/// <param name="Provenance">Where the name came from, for a party recorded as text.</param>
public readonly record struct ContractPartyRef(
    PersonId? PersonId = null,
    CompanyId? CompanyId = null,
    string? ExternalName = null,
    string? Provenance = null)
{
    /// <summary>A party the agency has a person record for.</summary>
    public static ContractPartyRef ForPerson(PersonId id) => new(PersonId: id);

    /// <summary>A party the agency has a company record for.</summary>
    public static ContractPartyRef ForCompany(CompanyId id) => new(CompanyId: id);

    /// <summary>A party named only on the paper, with where the name came from.</summary>
    public static ContractPartyRef ForExternal(string name, string? provenance = null) =>
        new(ExternalName: name, Provenance: provenance);

    /// <summary>Whether exactly one identity is given.</summary>
    public bool IsWellFormed =>
        (PersonId is not null ? 1 : 0)
        + (CompanyId is not null ? 1 : 0)
        + (string.IsNullOrWhiteSpace(ExternalName) ? 0 : 1)
        == 1;

    /// <summary>Whether this party is somebody AgencyOS has a record for.</summary>
    public bool IsResolved => PersonId is not null || CompanyId is not null;
}

/// <summary>How a signature was given.</summary>
/// <remarks>
/// Descriptive only. AgencyOS implements no electronic signature and verifies
/// nothing cryptographically: <see cref="Electronic"/> means somebody reported
/// that the signature was given electronically (ADR-0022).
/// </remarks>
public enum SignatureMethod
{
    /// <summary>Signed on paper.</summary>
    Wet = 1,

    /// <summary>Signed through an electronic service. AgencyOS verifies nothing.</summary>
    Electronic = 2,

    /// <summary>Signed in counterparts.</summary>
    Counterpart = 3,

    Other = 99,
}

/// <summary>
/// A party to a contract, in a stated role.
/// </summary>
/// <remarks>
/// <see cref="IsRequiredSignatory"/> lives here because it is a fact about this
/// party on this instrument, and it is what makes execution derivable rather than
/// asserted.
/// </remarks>
public sealed class ContractParty
{
    private ContractParty()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    public ContractPartyRole Role { get; private set; }

    /// <summary>The person, when the party is one AgencyOS knows.</summary>
    public PersonId? PersonId { get; private set; }

    /// <summary>The company, when the party is one AgencyOS knows.</summary>
    public CompanyId? CompanyId { get; private set; }

    /// <summary>The party's name, when AgencyOS has no record of them.</summary>
    public string? ExternalName { get; private set; }

    /// <summary>Where an external name came from, so it can be resolved later.</summary>
    public string? Provenance { get; private set; }

    /// <summary>Whether the agreement requires this party's signature.</summary>
    public bool IsRequiredSignatory { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Whether this party is somebody AgencyOS has a record for.</summary>
    public bool IsResolved => PersonId is not null || CompanyId is not null;

    /// <summary>Something to show for the party without resolving names.</summary>
    /// <remarks>
    /// The external name when there is one, and the role otherwise. A resolved
    /// party's name lives on the person or company record and is joined at read
    /// time, so it is not copied here where it could go stale.
    /// </remarks>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(ExternalName) ? Role.ToString() : ExternalName;

    /// <summary>The party as a reference.</summary>
    public ContractPartyRef Reference =>
        new(PersonId, CompanyId, ExternalName, Provenance);

    internal static ContractParty Create(
        OrganizationId organizationId,
        ContractId contractId,
        ContractPartyRef party,
        ContractPartyRole role,
        bool isRequiredSignatory,
        string? notes = null)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"Unknown contract party role '{role}'.");
        }

        if (!party.IsWellFormed)
        {
            throw new DomainException(
                "A contract party must be exactly one of a person, a company, or a name "
                + "for somebody AgencyOS has no record of.");
        }

        return new ContractParty
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractId = contractId,
            Role = role,
            PersonId = party.PersonId,
            CompanyId = party.CompanyId,
            ExternalName = Ensure.OptionalMax(party.ExternalName, "name", 300),
            Provenance = Ensure.OptionalMax(party.Provenance, nameof(party.Provenance), 500),
            IsRequiredSignatory = isRequiredSignatory,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
        };
    }
}

/// <summary>
/// A record that a party signed.
/// </summary>
/// <remarks>
/// It means exactly that: AgencyOS records that this party signed on this date.
/// There is no electronic signature here and nothing is verified
/// cryptographically. An integration that could actually verify one belongs to a
/// later milestone, and until then a signature row is a person's assertion
/// (ADR-0022).
/// </remarks>
public sealed class ContractSignature
{
    private ContractSignature()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The party who signed. One signature per party per contract.</summary>
    public Guid ContractPartyId { get; private set; }

    /// <summary>The day they signed, as reported. Freely backdated.</summary>
    public DateOnly SignedOn { get; private set; }

    public SignatureMethod Method { get; private set; }

    /// <summary>When AgencyOS was told.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    /// <summary>An identifier from wherever the signature actually happened.</summary>
    /// <remarks>Opaque. AgencyOS assigns it no meaning and verifies nothing against it.</remarks>
    public string? ExternalReference { get; private set; }

    public string? Notes { get; private set; }

    internal static ContractSignature Create(
        OrganizationId organizationId,
        ContractId contractId,
        Guid contractPartyId,
        DateOnly signedOn,
        SignatureMethod method,
        DateTimeOffset now,
        UserId recordedBy,
        string? externalReference = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(method))
        {
            throw new DomainException($"Unknown signature method '{method}'.");
        }

        if (signedOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainException("A signature cannot have been given in the future.");
        }

        return new ContractSignature
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractPartyId = contractPartyId,
            SignedOn = signedOn,
            Method = method,
            RecordedAt = now,
            RecordedBy = recordedBy,
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 200),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
        };
    }
}
