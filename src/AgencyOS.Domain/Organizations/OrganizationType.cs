namespace AgencyOS.Domain.Organizations;

/// <summary>Broad classification of an organization.</summary>
/// <remarks>
/// Deliberately coarse in M1. Richer classification arrives with the business
/// verticals that need it (M4 onward); adding values later is additive.
/// </remarks>
public enum OrganizationType
{
    /// <summary>The representation agency operating this instance of AgencyOS.</summary>
    Agency = 1,

    Studio = 2,
    Network = 3,
    ProductionCompany = 4,
    Other = 99,
}
