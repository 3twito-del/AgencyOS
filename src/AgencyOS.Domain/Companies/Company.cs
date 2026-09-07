using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Companies;

/// <summary>Opaque, immutable identifier for a <see cref="Company"/>.</summary>
public readonly record struct CompanyId(Guid Value)
{
    public static CompanyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Lifecycle state of a company record.</summary>
public enum CompanyStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>Kind of external body.</summary>
/// <remarks>
/// Describes companies the agency deals with. Distinct from
/// <c>OrganizationType</c>, which describes the tenant itself (ADR-0010).
/// </remarks>
public enum CompanyType
{
    Studio = 1,
    Network = 2,
    Streamer = 3,
    ProductionCompany = 4,
    ManagementCompany = 5,
    TalentAgency = 6,
    LawFirm = 7,
    Publisher = 8,
    Brand = 9,
    Other = 99,
}

/// <summary>
/// An external body the agency holds records about.
/// </summary>
/// <remarks>
/// A studio, network, management company, law firm or similar. It is a subject of
/// records and a relationship endpoint. It is never a security boundary and grants
/// no authority - that is what the tenant <c>Organization</c> is for (ADR-0010).
/// </remarks>
public sealed class Company
{
    private Company()
    {
    }

    public CompanyId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? LegalName { get; private set; }

    public CompanyType Type { get; private set; }

    public CompanyStatus Status { get; private set; }

    public string? Website { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public static Company Create(
        OrganizationId organizationId,
        string name,
        CompanyType type,
        UserId createdBy,
        DateTimeOffset now,
        string? legalName = null,
        string? website = null,
        string? notes = null)
    {
        return new Company
        {
            Id = CompanyId.New(),
            OrganizationId = organizationId,
            Name = Ensure.NotBlankMax(name, nameof(name), 256),
            LegalName = Ensure.OptionalMax(legalName, nameof(legalName), 256),
            Type = type,
            Status = CompanyStatus.Active,
            Website = Ensure.OptionalMax(website, nameof(website), 512),
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
        };
    }

    public void Update(
        string name,
        CompanyType type,
        DateTimeOffset now,
        string? legalName = null,
        string? website = null,
        string? notes = null)
    {
        if (Status != CompanyStatus.Active)
        {
            throw new DomainException("An archived company cannot be edited. Restore it first.");
        }

        Name = Ensure.NotBlankMax(name, nameof(name), 256);
        LegalName = Ensure.OptionalMax(legalName, nameof(legalName), 256);
        Type = type;
        Website = Ensure.OptionalMax(website, nameof(website), 512);
        Notes = notes;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (Status == CompanyStatus.Archived)
        {
            throw new DomainException("Company is already archived.");
        }

        Status = CompanyStatus.Archived;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (Status == CompanyStatus.Active)
        {
            throw new DomainException("Company is already active.");
        }

        Status = CompanyStatus.Active;
        UpdatedAt = now;
    }
}
