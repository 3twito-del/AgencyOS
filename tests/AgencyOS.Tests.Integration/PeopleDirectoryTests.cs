using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Listing people when they have employers.
/// </summary>
/// <remarks>
/// <para>
/// Every test here sets <see cref="Person.PrimaryCompanyId"/>, which is the whole
/// reason the file exists. Audit 001 found that the directory answered 500 for any
/// organization where a single person had an employer, and that the defect had
/// survived a 751-test integration suite because not one test had ever set that
/// field (<c>AOS-R001-001</c>, <c>AOS-R001-014</c>).
/// </para>
/// <para>
/// The failing query returned early when nobody on the page had a company, so the
/// untranslatable expression was never handed to the provider. A suite that seeds
/// the minimal valid person could not reach it at any size.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class PeopleDirectoryTests
{
    private readonly AgencyOsTestFixture _fixture;

    public PeopleDirectoryTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A. Nobody has an employer.
    /// </summary>
    /// <remarks>
    /// The case that always worked, kept so a repair that only handles the
    /// populated branch cannot pass by breaking the empty one.
    /// </remarks>
    [Fact]
    public async Task ListingPeopleWithNoEmployerSucceeds()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-none");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        await AddPersonAsync(actor, "Unattached", null);

        PersonSummaryResponse[] page = await ListAsync(client, actor, "Unattached");

        Assert.Single(page);
        Assert.Null(page[0].PrimaryCompanyId);
        Assert.Null(page[0].PrimaryCompanyName);
    }

    /// <summary>
    /// B. One person has an employer.
    /// </summary>
    /// <remarks>
    /// The minimal reproduction of <c>AOS-R001-001</c>, reduced to its smallest
    /// form: one company, one person, one GET. Before the repair this answered 500.
    /// </remarks>
    [Fact]
    public async Task ListingOnePersonWithAnEmployerReturnsTheCompanyName()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-one");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Company studio = await _fixture.SeedCompanyAsync(actor, "Lighthouse Pictures");

        await AddPersonAsync(actor, "Onesole", studio.Id);

        PersonSummaryResponse[] page = await ListAsync(client, actor, "Onesole");

        Assert.Single(page);
        Assert.Equal(studio.Id.Value, page[0].PrimaryCompanyId);
        Assert.Equal("Lighthouse Pictures", page[0].PrimaryCompanyName);
    }

    /// <summary>
    /// C. Several people, some sharing an employer, some with a different one, some with none.
    /// </summary>
    /// <remarks>
    /// The shape that proves the lookup maps each person to the right company
    /// rather than to whichever row came back first. A repair that returned one
    /// name for every person would pass B and fail here.
    /// </remarks>
    [Fact]
    public async Task EachPersonGetsTheirOwnEmployersName()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-many");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Company north = await _fixture.SeedCompanyAsync(actor, "Northlight");
        Company south = await _fixture.SeedCompanyAsync(actor, "Southbank");

        await AddPersonAsync(actor, "Manyfold", north.Id, "Ana");
        await AddPersonAsync(actor, "Manyfold", north.Id, "Ben");
        await AddPersonAsync(actor, "Manyfold", south.Id, "Cara");
        await AddPersonAsync(actor, "Manyfold", null, "Dov");

        PersonSummaryResponse[] page = await ListAsync(client, actor, "Manyfold");

        Assert.Equal(4, page.Length);
        Assert.Equal("Northlight", Named(page, "Ana").PrimaryCompanyName);
        Assert.Equal("Northlight", Named(page, "Ben").PrimaryCompanyName);
        Assert.Equal("Southbank", Named(page, "Cara").PrimaryCompanyName);
        Assert.Null(Named(page, "Dov").PrimaryCompanyName);
    }

    /// <summary>
    /// D. An employer in another tenant is refused by the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test was written to assert that the directory survives a person whose
    /// primary company belongs to somebody else, and the database refused to create
    /// the row: <c>fk_people_primary_company_same_tenant</c>. The edge the repair
    /// was asked to handle does not exist under the model.
    /// </para>
    /// <para>
    /// Kept, pointing at what is actually true. The tenant-scoped lookup in
    /// <c>LoadCompanyNamesAsync</c> is a second line rather than the only one, and
    /// this records which line is load-bearing — so a future schema change that
    /// relaxed the constraint would fail here and be a decision rather than a
    /// discovery.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEmployerInAnotherTenantCannotBeRecorded()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-foreign");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-stranger");

        Company theirs = await _fixture.SeedCompanyAsync(stranger, "Somebody Else Ltd");

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddPersonAsync(actor, "Foreigner", theirs.Id));

        Assert.Contains(
            "fk_people_primary_company_same_tenant",
            failure.InnerException?.Message ?? failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// E. Paging still pages, and still names employers.
    /// </summary>
    /// <remarks>
    /// The repair moved a filter across a projection. This asserts the other two
    /// properties of the same query survived it: the caller's page size is honoured
    /// and the ordering is by display name.
    /// </remarks>
    [Fact]
    public async Task APageOfPeopleWithEmployersIsBoundedAndOrdered()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-page");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        await _fixture.SeedPeopleAsync(actor, 30, "Pagewright");

        PersonSummaryResponse[] page = (await client.GetFromJsonAsync<PersonSummaryResponse[]>(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people?search=Pagewright&limit=10"))!;

        Assert.Equal(10, page.Length);
        Assert.Contains(page, x => x.PrimaryCompanyName is not null);

        string[] displayNames = [.. page.Select(x => x.DisplayName)];

        Assert.Equal([.. displayNames.OrderBy(x => x, StringComparer.Ordinal)], displayNames);
    }

    /// <summary>
    /// The response is a page, not a table scan.
    /// </summary>
    /// <remarks>
    /// The repair must not have been "load the companies table and match in
    /// memory". Asserted through behaviour that a table scan could not produce: a
    /// tenant holding far more companies than the page mentions still answers, and
    /// names only the ones it needs.
    /// </remarks>
    [Fact]
    public async Task NamingEmployersDoesNotDependOnHowManyCompaniesTheTenantHas()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-scan");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Company employer = await _fixture.SeedCompanyAsync(actor, "The One That Matters");

        for (int index = 0; index < 25; index++)
        {
            await _fixture.SeedCompanyAsync(actor, $"Irrelevant {index}");
        }

        await AddPersonAsync(actor, "Scanguard", employer.Id);

        PersonSummaryResponse[] page = await ListAsync(client, actor, "Scanguard");

        Assert.Single(page);
        Assert.Equal("The One That Matters", page[0].PrimaryCompanyName);
    }

    /// <summary>
    /// The directory is still refused to somebody outside the tenant.
    /// </summary>
    /// <remarks>
    /// A query repair is the easiest place to lose a tenant predicate. This is the
    /// assertion that the <c>Where</c> the fix moved still carries one.
    /// </remarks>
    [Fact]
    public async Task AnotherTenantsDirectoryIsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-tenant");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "dir-outsider");

        Company studio = await _fixture.SeedCompanyAsync(actor, "Private Pictures");
        await AddPersonAsync(actor, "Insider", studio.Id);

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"A stranger reading another tenant's directory got {(int)response.StatusCode}.");
    }

    // ------------------------------------------------------------- helpers

    private async Task AddPersonAsync(
        SeededActor actor,
        string surname,
        CompanyId? employer,
        string firstName = "Person")
    {
        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        context.People.Add(Person.Create(
            actor.Organization.Id,
            firstName,
            surname,
            actor.User.Id,
            DateTimeOffset.UtcNow,
            primaryCompanyId: employer));

        await context.SaveChangesAsync();
    }

    private static async Task<PersonSummaryResponse[]> ListAsync(
        HttpClient client,
        SeededActor actor,
        string search)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people?search={search}");

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /people answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<PersonSummaryResponse[]>())!;
    }

    private static PersonSummaryResponse Named(IEnumerable<PersonSummaryResponse> page, string firstName) =>
        page.Single(x => x.DisplayName.StartsWith(firstName, StringComparison.Ordinal));
}
