using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Verifies that authorization is decided by the server, from current state.
/// </summary>
/// <remarks>
/// <c>docs/07_SECURITY_AND_AUDIT.md</c>: never trust the Windows client to
/// enforce permissions. Every test here presents a perfectly well-formed,
/// fully compatible client; the only thing under test is whether the server
/// agrees the caller may act.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AuthorizationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public AuthorizationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnonymousCaller_CannotCreateAnOrganization()
    {
        using HttpClient client = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Anonymous Attempt", null, "Agency"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSubject_IsNotSilentlyProvisioned()
    {
        using HttpClient client = _fixture.CreateClient($"ghost-{Guid.NewGuid():N}");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Ghost Attempt", null, "Agency"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The required verification scenario: unauthorized protected mutation fails.</summary>
    [Fact]
    public async Task ObserverWithoutPermission_CannotCreateAnOrganization()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Observer);

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"Refused {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The required verification scenario: authorized protected mutation succeeds.</summary>
    [Fact]
    public async Task AdministratorWithPermission_CanCreateAnOrganization()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Administrator);

        using HttpClient client = _fixture.CreateClient(subject);
        string name = $"Permitted {Guid.NewGuid():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest(name, "Permitted Holdings Ltd", "Agency"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        OrganizationResponse? created = await response.Content.ReadFromJsonAsync<OrganizationResponse>();

        Assert.NotNull(created);
        Assert.Equal(name, created.Name);
        Assert.Equal("Agency", created.Type);
        Assert.Equal("Active", created.Status);
    }

    /// <summary>
    /// Permission held in one organization confers nothing in another.
    /// </summary>
    /// <remarks>
    /// The endpoint gate passes here - the caller does hold <c>memberships.grant</c>
    /// somewhere - and the request is still refused, because the authoritative
    /// check inside the handler is scoped to the target organization.
    /// </remarks>
    [Fact]
    public async Task AdministratorOfOneOrganization_CannotGrantMembershipInAnother()
    {
        (string subject, _, _) = await SeedActorAsync(AgencyRole.Administrator);

        User outsider = await _fixture.SeedUserAsync($"outsider-{Guid.NewGuid():N}", "Outsider");
        User strangerOwner = await _fixture.SeedUserAsync($"stranger-{Guid.NewGuid():N}", "Stranger Owner");
        Organization elsewhere = await _fixture.SeedOrganizationAsync(
            $"Elsewhere {Guid.NewGuid():N}",
            strangerOwner.Id);

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{elsewhere.Id.Value}/memberships",
            new GrantMembershipRequest(outsider.Id.Value, "Member"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorOfTheOrganization_CanGrantMembershipWithinIt()
    {
        (string subject, _, Organization organization) = await SeedActorAsync(AgencyRole.Administrator);

        User recruit = await _fixture.SeedUserAsync($"recruit-{Guid.NewGuid():N}", "Recruit");

        using HttpClient client = _fixture.CreateClient(subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id.Value}/memberships",
            new GrantMembershipRequest(recruit.Id.Value, "Member"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        MembershipResponse? membership = await response.Content.ReadFromJsonAsync<MembershipResponse>();

        Assert.NotNull(membership);
        Assert.Equal(recruit.Id.Value, membership.UserId);
        Assert.Equal("Member", membership.Role);
        Assert.Equal("Active", membership.Status);
    }

    /// <summary>
    /// Revoking a membership takes effect immediately, because permissions are read
    /// from current state rather than from a cached claim.
    /// </summary>
    [Fact]
    public async Task RevokedMembership_ImmediatelyLosesItsPermissions()
    {
        (string subject, User actor, Organization organization) = await SeedActorAsync(AgencyRole.Administrator);

        using HttpClient client = _fixture.CreateClient(subject);

        using (HttpResponseMessage before = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"Before Revocation {Guid.NewGuid():N}", null, "Agency")))
        {
            Assert.Equal(HttpStatusCode.Created, before.StatusCode);
        }

        await RevokeAllMembershipsAsync(actor.Id);

        using HttpResponseMessage after = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"After Revocation {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    private async Task<(string Subject, User User, Organization Organization)> SeedActorAsync(AgencyRole role)
    {
        string subject = $"actor-{Guid.NewGuid():N}";

        User user = await _fixture.SeedUserAsync(subject, $"Actor {role}");
        Organization organization = await _fixture.SeedOrganizationAsync($"Org {Guid.NewGuid():N}", user.Id);

        await _fixture.SeedMembershipAsync(organization.Id, user.Id, role, user.Id);

        return (subject, user, organization);
    }

    private async Task RevokeAllMembershipsAsync(UserId userId)
    {
        await using var context = _fixture.CreateDbContext();

        var memberships = context.Memberships.Where(x => x.UserId == userId).ToList();

        foreach (var membership in memberships)
        {
            membership.Revoke(userId, DateTimeOffset.UtcNow, otherActiveOwners: 1);
        }

        await context.SaveChangesAsync();
    }
}
