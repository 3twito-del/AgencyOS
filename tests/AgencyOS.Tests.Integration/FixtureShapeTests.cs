using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That the suite's own fixtures are shaped like real records.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001 found two 500s on the product's most-used read paths, both of which
/// had survived 3,749 unit, 521 Windows and 751 integration tests. Neither was a
/// gap in the number of tests. Both were a gap in the <em>shape</em> of the data
/// the tests created: every fixture built the minimal valid record, so any branch
/// that only runs when an optional relationship is populated was unreachable by
/// construction (<c>AOS-R001-014</c>).
/// </para>
/// <para>
/// The specific case was <see cref="Person.PrimaryCompanyId"/>, which appeared in
/// no integration test at all. The query that broke on it returned early when no
/// person on the page had a company, so the suite could not reach the defect at
/// any size.
/// </para>
/// <para>
/// These tests assert the property that fixed it: the shared seeding helper
/// populates the optional relationship. They are deliberately few. The lesson is
/// not "write more tests" — it is that a helper which builds only required fields
/// silently narrows everything downstream of it, and a future simplification of
/// that helper should fail something that says so.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class FixtureShapeTests
{
    private readonly AgencyOsTestFixture _fixture;

    public FixtureShapeTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Seeded people have employers.
    /// </summary>
    /// <remarks>
    /// The guard. If somebody simplifies the helper back to required fields only,
    /// this fails and explains what that costs, rather than the suite quietly
    /// losing coverage of every query that touches the relationship.
    /// </remarks>
    [Fact]
    public async Task SeededPeopleIncludeSomebodyWithAnEmployer()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "shape-employer");

        IReadOnlyList<Person> people = await _fixture.SeedPeopleAsync(actor, 8, "Shapewell");

        Assert.Contains(people, x => x.PrimaryCompanyId.HasValue);
    }

    /// <summary>
    /// And some of them do not.
    /// </summary>
    /// <remarks>
    /// The mirror of the guard above, and the reason the helper does not simply set
    /// the field on everybody. A page where every row has the relationship tests a
    /// projection no less narrowly than a page where none does — it just fails in
    /// the opposite direction. Both shapes have to be on the same page.
    /// </remarks>
    [Fact]
    public async Task SeededPeopleAlsoIncludeSomebodyWithout()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "shape-noemployer");

        IReadOnlyList<Person> people = await _fixture.SeedPeopleAsync(actor, 8, "Shapewell");

        Assert.Contains(people, x => !x.PrimaryCompanyId.HasValue);
    }

    /// <summary>
    /// The employer is real, and belongs to the same tenant.
    /// </summary>
    /// <remarks>
    /// A helper that set the field to a random GUID would satisfy the first test
    /// and would exercise the lookup's miss path only, never its hit path — which
    /// is where the name actually comes from.
    /// </remarks>
    [Fact]
    public async Task TheSeededEmployerExistsInTheSameTenant()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "shape-real");

        IReadOnlyList<Person> people = await _fixture.SeedPeopleAsync(actor, 4, "Shapewell");

        Person employed = people.First(x => x.PrimaryCompanyId.HasValue);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        bool exists = context.Companies.Any(x =>
            x.Id == employed.PrimaryCompanyId!.Value && x.OrganizationId == actor.Organization.Id);

        Assert.True(exists, "The seeded primary company does not exist in the actor's tenant.");
    }
}
