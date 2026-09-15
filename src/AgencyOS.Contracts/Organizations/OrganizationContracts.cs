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

/// <summary>One person in an organization, as the members list shows them.</summary>
/// <param name="MembershipId">Identifier of the membership, for acting on it.</param>
/// <param name="UserId">The person.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">Role conferred: Observer, Member, Administrator or Owner.</param>
/// <param name="GrantedAt">When they were given it.</param>
/// <param name="IsSelf">Whether this is the caller, so a client can say so.</param>
public sealed record OrganizationMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    DateTimeOffset GrantedAt,
    bool IsSelf);

/// <summary>
/// Brings a person into an organization, registering them if necessary.
/// </summary>
/// <remarks>
/// <see cref="ExternalSubject"/> is the identity-provider subject claim, not an
/// AgencyOS identifier. It is what the provider issues and what the operator's
/// directory shows; nobody is asked to type a GUID.
/// </remarks>
/// <param name="ExternalSubject">The identity-provider subject.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">Role to confer.</param>
public sealed record AddMemberRequest(
    string ExternalSubject,
    string DisplayName,
    string Email,
    string Role);

/// <summary>What adding a member produced.</summary>
/// <param name="MembershipId">The new membership.</param>
/// <param name="UserId">The person, whether newly registered or already known.</param>
/// <param name="UserWasRegistered">Whether this call registered them.</param>
public sealed record AddMemberResponse(Guid MembershipId, Guid UserId, bool UserWasRegistered);

/// <summary>Moves somebody to a different role.</summary>
/// <param name="Role">The role they should hold instead.</param>
public sealed record ChangeMemberRoleRequest(string Role);

/// <summary>What the change produced.</summary>
/// <param name="MembershipId">The new membership carrying the new role.</param>
public sealed record ChangeMemberRoleResponse(Guid MembershipId);
