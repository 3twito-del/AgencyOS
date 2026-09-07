using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Saved views over the M5 targets, against PostgreSQL.
/// </summary>
/// <remarks>
/// A saved view is a second route to records the user can already read, so these
/// tests care about the same two things M4's did: that the filters an agency
/// actually uses select the right rows, and that arriving by this route reveals no
/// more than arriving by the projects or packages endpoint would.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ProjectSavedViewTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ProjectSavedViewTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AProjectView_NarrowsByStage()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-stage");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreateProjectAsync(client, tenant, "The Undertow", "Packaging");
        await CreateProjectAsync(client, tenant, "Salt Road", "Development");

        SavedViewResultsResponse packaging = await RunAsync(
            client,
            tenant,
            "In packaging",
            new SavedViewDefinitionModel(
                3, "Projects", new SavedViewFiltersModel(DevelopmentStage: "Packaging")));

        Assert.Equal("Projects", packaging.Target);
        Assert.Equal("The Undertow", Assert.Single(packaging.Projects).Title);
        Assert.Empty(packaging.People);
        Assert.Empty(packaging.Packages);
    }

    [Fact]
    public async Task AProjectView_NarrowsByType()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-type");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreateProjectAsync(client, tenant, "The Undertow", "Development", "FeatureFilm");
        await CreateProjectAsync(client, tenant, "Salt Road", "Development", "TelevisionSeries");

        SavedViewResultsResponse features = await RunAsync(
            client,
            tenant,
            "Features",
            new SavedViewDefinitionModel(3, "Projects", new SavedViewFiltersModel(ProjectType: "FeatureFilm")));

        Assert.Equal("The Undertow", Assert.Single(features.Projects).Title);
    }

    /// <summary>
    /// "Missing a director" answers a stated rule, not a guess.
    /// </summary>
    /// <remarks>
    /// A project counts as missing a director when nothing currently holds a
    /// directing role - whether or not such a role row exists at all. A project
    /// that never created the role is missing one just as much as one whose
    /// director walked away, and a rule that only looked at open role rows would
    /// quietly exclude the first case.
    /// </remarks>
    [Fact]
    public async Task AMissingRoleView_FindsProjectsNobodyHoldsThatRoleOn()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-missing");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        // Directed.
        ProjectDetailResponse directed = await CreateProjectAsync(client, tenant, "The Undertow", "Development");

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{directed.Project.Id}/roles",
            new CreateProjectRoleRequest("Director", directed.Project.Version, IsExclusive: true)));

        PersonDetailResponse director = await CreatePersonAsync(client, tenant, "Ada", "Reyes");

        directed = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{directed.Project.Id}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{directed.Project.Id}/roles/{role}/attachments",
            new AttachToRoleRequest(
                "Attached",
                new DateOnly(2026, 3, 1),
                directed.Project.Version,
                PersonId: director.Person.Id)));

        // Has an open directing role, nobody in it.
        ProjectDetailResponse seeking = await CreateProjectAsync(client, tenant, "Salt Road", "Development");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{seeking.Project.Id}/roles",
            new CreateProjectRoleRequest("Director", seeking.Project.Version, IsExclusive: true)));

        // Has no directing role at all.
        await CreateProjectAsync(client, tenant, "Nightjar", "Concept");

        SavedViewResultsResponse missing = await RunAsync(
            client,
            tenant,
            "Needs a director",
            new SavedViewDefinitionModel(3, "Projects", new SavedViewFiltersModel(MissingRoleType: "Director")));

        string[] titles = [.. missing.Projects.Select(x => x.Title).Order()];

        Assert.Equal(["Nightjar", "Salt Road"], titles);
    }

    /// <summary>A view can list the projects one person is currently working on.</summary>
    [Fact]
    public async Task AnAttachedPersonView_ListsWhatTheyAreOn()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-attached");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Ada", "Reyes");

        ProjectDetailResponse theirs = await CreateProjectAsync(client, tenant, "The Undertow", "Development");

        Guid role = await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{theirs.Project.Id}/roles",
            new CreateProjectRoleRequest("Director", theirs.Project.Version, IsExclusive: true)));

        theirs = await GetAsync<ProjectDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/projects/{theirs.Project.Id}");

        await CreatedIdAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects/{theirs.Project.Id}/roles/{role}/attachments",
            new AttachToRoleRequest(
                "Attached", new DateOnly(2026, 3, 1), theirs.Project.Version, PersonId: person.Person.Id)));

        await CreateProjectAsync(client, tenant, "Salt Road", "Development");

        SavedViewResultsResponse mine = await RunAsync(
            client,
            tenant,
            "Ada's projects",
            new SavedViewDefinitionModel(
                3, "Projects", new SavedViewFiltersModel(AttachedPersonId: person.Person.Id)));

        Assert.Equal("The Undertow", Assert.Single(mine.Projects).Title);
    }

    [Fact]
    public async Task APackageView_NarrowsByStatus()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-packages");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        ProjectDetailResponse project = await CreateProjectAsync(client, tenant, "The Undertow", "Packaging");

        PackageDetailResponse assembling = await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Lead package", actor.User.Id.Value)));

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages/{assembling.Package.Id}/status",
            new ChangePackageStatusRequest("Assembling", assembling.Package.Version)));

        await CreatedAsync<PackageDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/packages",
            new CreatePackageRequest(project.Project.Id, "Draft package", actor.User.Id.Value)));

        SavedViewResultsResponse inProgress = await RunAsync(
            client,
            tenant,
            "Assembling",
            new SavedViewDefinitionModel(3, "Packages", new SavedViewFiltersModel(PackageStatus: "Assembling")));

        Assert.Equal("Packages", inProgress.Target);
        Assert.Equal("Lead package", Assert.Single(inProgress.Packages).Name);
    }

    /// <summary>
    /// A packages view runs under the package permission, not the project one.
    /// </summary>
    /// <remarks>
    /// A caller who may see the slate is not automatically entitled to what the
    /// agency is quietly assembling on it, so the two grants stay separate here as
    /// they do on the endpoints.
    /// </remarks>
    [Fact]
    public async Task APackagesView_CannotBeCreatedWithoutPackagesRead()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m5-views-perm");
        Guid tenant = owner.Organization.Id.Value;

        string strangerSubject = $"m5-views-stranger-{Guid.NewGuid():N}";
        await _fixture.SeedUserAsync(strangerSubject, "Stranger");

        using HttpClient strangerClient = _fixture.CreateClient(strangerSubject);

        using HttpResponseMessage response = await strangerClient.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views",
            new CreateSavedViewRequest(
                "Packages",
                new SavedViewDefinitionModel(3, "Packages", new SavedViewFiltersModel())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Projects", "PackageStatus")]
    [InlineData("Packages", "DevelopmentStage")]
    [InlineData("People", "ProjectType")]
    [InlineData("Talent", "MissingRoleType")]
    [InlineData("Prospects", "AttachedPersonId")]
    public async Task AnM5FilterOnTheWrongTarget_IsRefused(string target, string filter)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-filter");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        SavedViewFiltersModel filters = filter switch
        {
            "PackageStatus" => new SavedViewFiltersModel(PackageStatus: "Ready"),
            "DevelopmentStage" => new SavedViewFiltersModel(DevelopmentStage: "Packaging"),
            "ProjectType" => new SavedViewFiltersModel(ProjectType: "FeatureFilm"),
            "MissingRoleType" => new SavedViewFiltersModel(MissingRoleType: "Director"),
            _ => new SavedViewFiltersModel(AttachedPersonId: Guid.NewGuid()),
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest($"{target} by {filter}", new SavedViewDefinitionModel(3, target, filters)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The people-and-companies Status field is refused on a project view.
    /// </summary>
    /// <remarks>
    /// A project has its own status vocabulary. Accepting the wrong field would
    /// silently return everything rather than what was asked for, which is the
    /// worst available failure: it looks like a working view.
    /// </remarks>
    [Fact]
    public async Task TheWrongStatusField_IsRefusedOnAProjectView()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m5-views-status");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                "Active projects, the wrong way",
                new SavedViewDefinitionModel(3, "Projects", new SavedViewFiltersModel(Status: "Active"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Views saved before M5 still mean what they meant.
    /// </summary>
    /// <remarks>
    /// Definition version 3 only adds targets and filters, so versions 1 and 2 are
    /// read rather than refused. Refusing them would have broken every view saved
    /// before this milestone for no reason at all.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AViewSavedAtAnEarlierDefinitionVersion_StillRuns(int definitionVersion)
    {
        SeededActor actor = await _fixture.SeedActorAsync(
            AgencyRole.Member, $"m5-views-v{definitionVersion}");

        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Aurelia", "Sandoval");

        SavedViewResultsResponse results = await RunAsync(
            client,
            tenant,
            $"People at version {definitionVersion}",
            new SavedViewDefinitionModel(
                definitionVersion, "People", new SavedViewFiltersModel(Status: "Active")));

        Assert.Equal("People", results.Target);
        Assert.Equal("Aurelia Sandoval", Assert.Single(results.People).DisplayName);
    }

    /// <summary>An M5 target cannot be requested at a definition version that lacked it.</summary>
    [Theory]
    [InlineData("Projects")]
    [InlineData("Packages")]
    public async Task AnM5TargetAtDefinitionVersionTwo_IsRefused(string target)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, $"m5-views-early-{target}");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                $"{target}, impossibly early",
                new SavedViewDefinitionModel(2, target, new SavedViewFiltersModel())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private async Task<SavedViewResultsResponse> RunAsync(
        HttpClient client,
        Guid tenant,
        string name,
        SavedViewDefinitionModel definition)
    {
        SavedViewResponse view = await CreatedAsync<SavedViewResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views",
            new CreateSavedViewRequest(name, definition)));

        return await GetAsync<SavedViewResultsResponse>(
            client, $"/api/v1/organizations/{tenant}/saved-views/{view.Id}/results");
    }

    private static async Task<ProjectDetailResponse> CreateProjectAsync(
        HttpClient client,
        Guid tenant,
        string title,
        string stage,
        string type = "FeatureFilm")
    {
        return await CreatedAsync<ProjectDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/projects",
            new CreateProjectRequest(title, type, Stage: stage)));
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        HttpClient client,
        Guid tenant,
        string firstName,
        string lastName)
    {
        return await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(firstName, lastName)));
    }

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedId? created = await response.Content.ReadFromJsonAsync<CreatedId>();

        return created!.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record CreatedId(Guid Id);
}
