using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Authorization, tenant isolation and release enforcement over the M2 surface.
/// </summary>
/// <remarks>
/// The M2 endpoints are tenant-scoped by route, so "may this caller act" and "in
/// which tenant" are one question. These tests ask it from every wrong angle.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class PeopleSliceSecurityTests
{
    private readonly AgencyOsTestFixture _fixture;

    public PeopleSliceSecurityTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // -------------------------------------------------------- authorization

    [Fact]
    public async Task AnonymousCaller_CannotCreateAPerson()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "anon");
        using HttpClient client = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people",
            new CreatePersonRequest("Nobody"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An observer may read the directory but not change it.</summary>
    [Fact]
    public async Task Observer_CanReadButNotWrite()
    {
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "observer");
        using HttpClient client = _fixture.CreateClient(observer.Subject);
        Guid tenant = observer.Organization.Id.Value;

        using (HttpResponseMessage read = await client.GetAsync($"/api/v1/organizations/{tenant}/people"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using HttpResponseMessage write = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest("Refused"));

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    /// <summary>
    /// An observer cannot record interactions either. The day-to-day operational
    /// role is Member.
    /// </summary>
    [Fact]
    public async Task Observer_CannotRecordInteractions()
    {
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "observer-interaction");
        using HttpClient client = _fixture.CreateClient(observer.Subject);
        Guid tenant = observer.Organization.Id.Value;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Call",
                DateTimeOffset.UtcNow,
                "Refused",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", Guid.NewGuid()))]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------ tenant isolation

    /// <summary>
    /// Holding a permission in one tenant confers nothing in another. The endpoint
    /// gate passes here - the caller really does hold <c>people.read</c> somewhere -
    /// and the request is still refused by the scoped check.
    /// </summary>
    [Fact]
    public async Task Member_CannotReadAnotherTenantsDirectory()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "tenant-a");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "tenant-b");

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/people");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Member_CannotWriteIntoAnotherTenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "write-a");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "write-b");

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/people",
            new CreatePersonRequest("Trespasser"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();
        Assert.False(await context.People.AsNoTracking()
            .AnyAsync(p => p.OrganizationId == theirs.Organization.Id));
    }

    /// <summary>
    /// A relationship cannot reach across tenants. The handler refuses first with a
    /// comprehensible error; the database would refuse anyway through its composite
    /// foreign keys (ADR-0011).
    /// </summary>
    [Fact]
    public async Task Relationship_CannotReferenceAPartyInAnotherTenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "rel-a");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "rel-b");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);
        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);

        PersonDetailResponse myPerson = await CreatePersonAsync(myClient, mine.Organization.Id.Value, "Mine");
        CompanyDetailResponse theirCompany = await CreateCompanyAsync(
            theirClient, theirs.Organization.Id.Value, "Theirs");

        using HttpResponseMessage response = await myClient.PostAsJsonAsync(
            $"/api/v1/organizations/{mine.Organization.Id.Value}/relationships",
            new CreateRelationshipRequest(
                new PartyRefRequest("Person", myPerson.Person.Id),
                new PartyRefRequest("Company", theirCompany.Company.Id),
                "Employment"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A person in another tenant is not found, not forbidden-then-leaked.</summary>
    [Fact]
    public async Task GetPerson_DoesNotRevealRecordsFromAnotherTenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "read-a");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "read-b");

        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);
        PersonDetailResponse hidden = await CreatePersonAsync(
            theirClient, theirs.Organization.Id.Value, "Hidden");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage response = await myClient.GetAsync(
            $"/api/v1/organizations/{mine.Organization.Id.Value}/people/{hidden.Person.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------- release enforcement

    /// <summary>
    /// The M2 endpoints are protected mutations, so a revoked build is refused
    /// there exactly as it is anywhere else.
    /// </summary>
    [Fact]
    public async Task RevokedClient_CannotCreateAPerson()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "revoked");

        using HttpClient client = _fixture.CreateClient(
            actor.Subject,
            clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people",
            new CreatePersonRequest("From a revoked build"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("REVOKED", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientBelowMinimumVersion_CannotRecordAnInteraction()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "stale");

        using HttpClient client = _fixture.CreateClient(actor.Subject, clientVersion: "0.1.0");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/interactions",
            new RecordInteractionRequest(
                "Call",
                DateTimeOffset.UtcNow,
                "From a stale build",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", Guid.NewGuid()))]));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    /// <summary>
    /// Reads stay open to a revoked build, so it can still explain itself and
    /// update.
    /// </summary>
    [Fact]
    public async Task RevokedClient_MayStillReadTheDirectory()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "revoked-read");

        using HttpClient client = _fixture.CreateClient(
            actor.Subject,
            clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Contract 2 added the people slice additively, so the server still serves a
    /// contract-1 client. This is the first real exercise of the supported range.
    /// </summary>
    [Fact]
    public async Task ContractOneClient_IsStillAccepted()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "contract-one");

        using HttpClient client = _fixture.CreateClient(
            actor.Subject,
            apiContractVersion: ApiContract.MinimumSupported);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people",
            new CreatePersonRequest("Older client"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ClientOnAnUnsupportedContract_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "contract-future");

        using HttpClient client = _fixture.CreateClient(
            actor.Subject,
            apiContractVersion: ApiContract.Current + 1);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people",
            new CreatePersonRequest("From the future"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    // ------------------------------------------------------------- helpers

    private static async Task<PersonDetailResponse> CreatePersonAsync(HttpClient client, Guid tenant, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(name));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<CompanyDetailResponse> CreateCompanyAsync(HttpClient client, Guid tenant, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest(name, "Studio"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CompanyDetailResponse>())!;
    }
}
