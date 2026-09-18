using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Relationship intelligence about a company.
/// </summary>
/// <remarks>
/// <para>
/// The operational-alpha evaluation found <c>Person</c> answering <c>200</c> and
/// <c>Company</c> answering <c>500</c> on ALPHA 0.1.0 (<c>d8a8bc56</c>). The company
/// branch projected <c>new RadarPerson(default, …)</c>, and a constant of the
/// converted <c>PersonId</c> type inside a projection is not translatable.
/// </para>
/// <para>
/// The endpoint computes this for a person and a company only; other subject kinds
/// answer <c>404</c> because nothing is computed for them, and that is unchanged
/// here — this is a repair, not a widening.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class CompanyRelationshipIntelligenceTests
{
    private readonly AgencyOsTestFixture _fixture;

    public CompanyRelationshipIntelligenceTests(AgencyOsTestFixture fixture) =>
        _fixture = fixture;

    /// <summary>A company answers, and says which company it is.</summary>
    [Fact]
    public async Task ACompanyHasRelationshipIntelligence()
    {
        Scene s = await SetUpAsync("rel-company");

        using HttpResponseMessage response = await s.Client.GetAsync(
            $"{s.Root}/intelligence/relationships/Company/{s.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RelationshipIntelligenceResponse body =
            (await response.Content.ReadFromJsonAsync<RelationshipIntelligenceResponse>())!;

        Assert.Equal(s.CompanyId, body.SubjectId);
        Assert.Equal("Company", body.Kind);
        Assert.Equal("Northgate Pictures", body.DisplayName);
    }

    /// <summary>The person branch is unchanged.</summary>
    [Fact]
    public async Task APersonStillHasRelationshipIntelligence()
    {
        Scene s = await SetUpAsync("rel-person");

        using HttpResponseMessage response = await s.Client.GetAsync(
            $"{s.Root}/intelligence/relationships/Person/{s.PersonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RelationshipIntelligenceResponse body =
            (await response.Content.ReadFromJsonAsync<RelationshipIntelligenceResponse>())!;

        Assert.Equal(s.PersonId, body.SubjectId);
        Assert.Equal("Person", body.Kind);
    }

    /// <summary>A company in another tenant is not readable from this one.</summary>
    [Fact]
    public async Task ACompanyInAnotherTenantIsNotFound()
    {
        Scene mine = await SetUpAsync("rel-tenant-a");
        Scene theirs = await SetUpAsync("rel-tenant-b");

        using HttpResponseMessage response = await mine.Client.GetAsync(
            $"{mine.Root}/intelligence/relationships/Company/{theirs.CompanyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A subject kind this endpoint does not compute is unchanged.
    /// </summary>
    /// <remarks>
    /// Pinned so the repair cannot quietly widen what the endpoint claims to know.
    /// </remarks>
    [Fact]
    public async Task AnUncomputedSubjectKindIsStillNotFound()
    {
        Scene s = await SetUpAsync("rel-kind");

        using HttpResponseMessage response = await s.Client.GetAsync(
            $"{s.Root}/intelligence/relationships/Project/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Scene> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        CompanyDetailResponse company = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people",
                new CreatePersonRequest(
                    "Marguerite", "Okonjo-Lindqvist",
                    PrimaryCompanyId: company.Company.Id)));

        return new Scene(client, root, company.Company.Id, person.Person.Id);
    }

    private sealed record Scene(HttpClient Client, string Root, Guid CompanyId, Guid PersonId);

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
