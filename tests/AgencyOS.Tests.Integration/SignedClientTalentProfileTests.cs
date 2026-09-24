using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Signing a client, and giving them a talent profile, are two separate commands.
/// </summary>
/// <remarks>
/// <para>
/// The final-candidate RC at <c>f29a2ee</c> stopped at C7 step 2: a prospect converted
/// through the Windows client became an active client and could not appear in Talent
/// or be the subject of a talent pursuit, because both are built from talent profiles.
/// </para>
/// <para>
/// Owner decision C kept the concepts apart. Conversion must not create a profile and
/// must not need the talent permission; the profile is created by the existing talent
/// command, on its own permission, as a deliberate step. These hold the server side of
/// that, end to end, on the same person.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class SignedClientTalentProfileTests
{
    private readonly AgencyOsTestFixture _fixture;

    public SignedClientTalentProfileTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Prospect, convert, no profile and not in Talent; explicit profile; then in Talent
    /// as a client, with the representation conversion created unchanged.
    /// </summary>
    [Fact]
    public async Task AConvertedClient_EntersTalentOnlyThroughTheExplicitProfile()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "decision-c");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        (Guid personId, RepresentationResponse representation) = await SignAsync(client, actor, "Orla", "Brennan");

        // Conversion alone: an active client, and no talent profile anywhere.
        Assert.Equal("Active", representation.Status);
        Assert.Equal(0, await ProfilesFor(personId));

        using (HttpResponseMessage absent = await client.GetAsync($"/api/v1/organizations/{tenant}/talent/{personId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        }

        Assert.DoesNotContain(await TalentAsync(client, tenant), x => x.PersonId == personId);

        // The explicit step, on the existing contract.
        using (HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(personId, CareerStage: "Unknown")))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        Assert.Equal(1, await ProfilesFor(personId));

        // In the roster, as a client, with the representation's own scope - the
        // roster the talent-engagement subject picker is built from.
        TalentSummaryResponse listed = Assert.Single(await TalentAsync(client, tenant), x => x.PersonId == personId);

        Assert.True(listed.IsClient);
        Assert.Equal("Active", listed.RepresentationStatus);
        Assert.Equal(["Film"], listed.Scopes);
        Assert.Equal("Unknown", listed.CareerStage);

        Assert.Contains(await TalentAsync(client, tenant, clientsOnly: true), x => x.PersonId == personId);

        // The representation is the one conversion created, not a second.
        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        Assert.Equal(
            representation.Id,
            Assert.Single(await context.Representations
                .Where(x => x.PersonId == new PersonId(personId))
                .Select(x => x.Id.Value)
                .ToListAsync()));
    }

    /// <summary>A second profile for the same person is refused; there stays one.</summary>
    [Fact]
    public async Task ASecondProfile_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "decision-c-dup");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        (Guid personId, _) = await SignAsync(client, actor, "Pia", "Lindqvist");

        using (HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent", new CreateTalentProfileRequest(personId)))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent", new CreateTalentProfileRequest(personId, CareerStage: "Veteran")))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        Assert.Equal(1, await ProfilesFor(personId));
    }

    /// <summary>
    /// Without the talent permission the profile is refused, and the client stays signed.
    /// </summary>
    [Fact]
    public async Task WithoutTheTalentPermission_TheProfileIsRefused_AndTheClientStaysSigned()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "decision-c-authz");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        (Guid personId, RepresentationResponse representation) = await SignAsync(client, actor, "Rafe", "Okonjo");

        // An observer in the same organization holds no talent write permission.
        string subject = "decision-c-observer-" + Guid.NewGuid().ToString("N");
        User observer = await _fixture.SeedUserAsync(subject, "Observer");

        await _fixture.SeedMembershipAsync(actor.Organization.Id, observer.Id, AgencyRole.Observer, actor.User.Id);

        Assert.False(RolePermissions.For(AgencyRole.Observer).Contains(Permission.TalentWrite));

        using HttpClient reader = _fixture.CreateClient(subject);

        using (HttpResponseMessage refused = await reader.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent", new CreateTalentProfileRequest(personId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal(0, await ProfilesFor(personId));

        RepresentationResponse after = await GetAsync<RepresentationResponse>(
            client, $"/api/v1/organizations/{tenant}/representations/{representation.Id}");

        Assert.Equal("Active", after.Status);
        Assert.Equal(representation.Version, after.Version);
    }

    private async Task<(Guid PersonId, RepresentationResponse Representation)> SignAsync(
        HttpClient client,
        SeededActor actor,
        string firstName,
        string lastName)
    {
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people", new CreatePersonRequest(firstName, lastName)));

        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(person.Person.Id, actor.User.Id.Value, new DateOnly(2026, 9, 1))));

        RepresentationResponse representation = await CreatedAsync<RepresentationResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}/convert",
            new ConvertProspectRequest(new DateOnly(2026, 9, 24), actor.User.Id.Value, ["Film"], prospect.Version)));

        ProspectResponse converted = await GetAsync<ProspectResponse>(
            client, $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}");

        Assert.Equal("Converted", converted.Stage);
        Assert.Equal(representation.Id, converted.ConvertedToRepresentationId);

        return (person.Person.Id, representation);
    }

    private async Task<int> ProfilesFor(Guid personId)
    {
        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        return await context.TalentProfiles.CountAsync(x => x.PersonId == new PersonId(personId));
    }

    private static Task<List<TalentSummaryResponse>> TalentAsync(HttpClient client, Guid tenant, bool clientsOnly = false) =>
        GetAsync<List<TalentSummaryResponse>>(
            client, $"/api/v1/organizations/{tenant}/talent" + (clientsOnly ? "?clientsOnly=true" : string.Empty));

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
