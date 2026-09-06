namespace AgencyOS.Contracts.Organizations;

/// <param name="Name">Display name of the organization.</param>
/// <param name="LegalName">Registered legal name, when it differs.</param>
/// <param name="Type">Classification: Agency, Studio, Network, ProductionCompany or Other.</param>
public sealed record CreateOrganizationRequest(string Name, string? LegalName, string Type);

/// <param name="Id">Identifier of the organization.</param>
/// <param name="Name">Display name.</param>
/// <param name="LegalName">Registered legal name.</param>
/// <param name="Type">Classification.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string Type,
    string Status,
    DateTimeOffset CreatedAt);

/// <param name="UserId">User receiving the membership.</param>
/// <param name="Role">Role conferred: Observer, Member, Administrator or Owner.</param>
public sealed record GrantMembershipRequest(Guid UserId, string Role);

/// <param name="Id">Identifier of the membership.</param>
/// <param name="OrganizationId">Organization the membership is held in.</param>
/// <param name="UserId">User holding the membership.</param>
/// <param name="Role">Role conferred.</param>
/// <param name="Status">Lifecycle status.</param>
public sealed record MembershipResponse(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    string Role,
    string Status);
