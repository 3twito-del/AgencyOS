using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Saved views over the M4 targets, against PostgreSQL.
/// </summary>
/// <remarks>
/// A saved view is a second route to records the user can already read, so these
/// tests care about two things: that the filters an agency actually uses select
/// the right rows, and that arriving by this route reveals no more than arriving
/// by the talent or prospects endpoint would.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class RepresentationSavedViewTests
{
    private readonly AgencyOsTestFixture _fixture;

    public RepresentationSavedViewTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ATalentView_NarrowedToClients_ExcludesTalentTheAgencyDoesNotRepresent()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-talent-clients");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await SignAsync(client, actor, "Rosalind", "Achebe", "Television");
        await ProfileOnlyAsync(client, tenant, "Tobias", "Nkemdirim");

        SavedViewResultsResponse everyone = await RunAsync(
            client,
            tenant,
            "All talent",
            new SavedViewDefinitionModel(2, "Talent", new SavedViewFiltersModel()));

        Assert.Equal("Talent", everyone.Target);
        Assert.Equal(2, everyone.Talent.Count);

        SavedViewResultsResponse clients = await RunAsync(
            client,
            tenant,
            "Clients only",
            new SavedViewDefinitionModel(2, "Talent", new SavedViewFiltersModel(ClientsOnly: true)));

        TalentSummaryResponse only = Assert.Single(clients.Talent);

        Assert.Equal("Rosalind Achebe", only.DisplayName);
        Assert.True(only.IsClient);
        Assert.Equal("Active", only.RepresentationStatus);
        Assert.Empty(clients.People);
        Assert.Empty(clients.Prospects);
    }

    [Fact]
    public async Task ATalentView_CanNarrowToOneRepresentedArea()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-talent-scope");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await SignAsync(client, actor, "Priya", "Raghunathan", "Literary");
        await SignAsync(client, actor, "Callum", "Ferreira", "Acting");

        SavedViewResultsResponse literary = await RunAsync(
            client,
            tenant,
            "Literary clients",
            new SavedViewDefinitionModel(
                2,
                "Talent",
                new SavedViewFiltersModel(ClientsOnly: true, ScopeArea: "Literary")));

        Assert.Equal("Priya Raghunathan", Assert.Single(literary.Talent).DisplayName);
    }

    /// <summary>
    /// A terminated representation moves somebody from one list to the other, with
    /// nothing overwritten.
    /// </summary>
    [Fact]
    public async Task AFormerClientsView_ListsPeopleTheAgencyUsedToRepresent()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-talent-former");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        RepresentationResponse signed = await SignAsync(client, actor, "Marguerite", "Osei", "Acting");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{signed.Id}/transition",
            new TransitionRepresentationRequest("Terminated", new DateOnly(2026, 6, 30), signed.Version)));

        SavedViewResultsResponse current = await RunAsync(
            client,
            tenant,
            "Current clients",
            new SavedViewDefinitionModel(2, "Talent", new SavedViewFiltersModel(ClientsOnly: true)));

        Assert.Empty(current.Talent);

        SavedViewResultsResponse former = await RunAsync(
            client,
            tenant,
            "Former clients",
            new SavedViewDefinitionModel(2, "Talent", new SavedViewFiltersModel(FormerClientsOnly: true)));

        TalentSummaryResponse alumni = Assert.Single(former.Talent);

        Assert.Equal("Marguerite Osei", alumni.DisplayName);
        Assert.False(alumni.IsClient);
        Assert.Equal("Terminated", alumni.RepresentationStatus);
    }

    /// <summary>The list an agent opens on a Monday: who is owed a follow-up.</summary>
    [Fact]
    public async Task AProspectView_ListsOnlyThoseDueForFollowUpInWindow()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-prospect-followup");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        await StartPursuitAsync(client, actor, "Imogen", "Villanueva", today.AddDays(2));
        await StartPursuitAsync(client, actor, "Desmond", "Adeyemi", today.AddDays(90));

        SavedViewResultsResponse soon = await RunAsync(
            client,
            tenant,
            "Follow up this week",
            new SavedViewDefinitionModel(2, "Prospects", new SavedViewFiltersModel(FollowUpWithinDays: 7)));

        Assert.Equal("Prospects", soon.Target);
        Assert.Equal("Imogen Villanueva", Assert.Single(soon.Prospects).DisplayName);

        SavedViewResultsResponse all = await RunAsync(
            client,
            tenant,
            "Every open pursuit",
            new SavedViewDefinitionModel(2, "Prospects", new SavedViewFiltersModel()));

        Assert.Equal(2, all.Prospects.Count);
    }

    /// <summary>
    /// The one that matters: a saved view must not reveal what the endpoint hides.
    /// </summary>
    /// <remarks>
    /// Saved view results are produced by the projection directly rather than by
    /// <c>RepresentationQueryService</c>, so this route once returned strategy
    /// notes in full to an observer the prospects endpoint redacted them for. The
    /// redaction now lives in one shared place; this asserts both routes agree.
    /// </remarks>
    [Fact]
    public async Task AProspectView_RedactsStrategyNotesForAnObserver()
    {
        SeededActor member = await _fixture.SeedActorAsync(AgencyRole.Member, "views-notes-member");
        Guid tenant = member.Organization.Id.Value;

        string observerSubject = $"views-notes-observer-{Guid.NewGuid():N}";
        Domain.Identity.User observer = await _fixture.SeedUserAsync(observerSubject, "Observer");

        await _fixture.SeedMembershipAsync(
            member.Organization.Id, observer.Id, AgencyRole.Observer, member.User.Id);

        using HttpClient memberClient = _fixture.CreateClient(member.Subject);

        PersonDetailResponse person = await CreatePersonAsync(memberClient, tenant, "Yusuf", "Bektas");

        await CreatedAsync<ProspectResponse>(memberClient.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(
                person.Person.Id,
                member.User.Id.Value,
                new DateOnly(2026, 1, 5),
                Source: "Festival",
                StrategyNotes: "Unhappy at their current agency; approach after the festival.",
                NextFollowUpOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))));

        // The member wrote the note and may read it back, by either route.
        ProspectResponse[] direct = await GetAsync<ProspectResponse[]>(
            memberClient, $"/api/v1/organizations/{tenant}/prospects");

        Assert.Contains(
            "festival",
            Assert.Single(direct).StrategyNotes,
            StringComparison.OrdinalIgnoreCase);

        SavedViewResultsResponse memberView = await RunAsync(
            memberClient,
            tenant,
            "My pursuits",
            new SavedViewDefinitionModel(2, "Prospects", new SavedViewFiltersModel()));

        Assert.NotNull(Assert.Single(memberView.Prospects).StrategyNotes);

        // The observer may see the pursuit, by either route, and the note by neither.
        using HttpClient observerClient = _fixture.CreateClient(observerSubject);

        ProspectResponse[] observed = await GetAsync<ProspectResponse[]>(
            observerClient, $"/api/v1/organizations/{tenant}/prospects");

        Assert.Null(Assert.Single(observed).StrategyNotes);

        SavedViewResultsResponse observerView = await RunAsync(
            observerClient,
            tenant,
            "Pursuits",
            new SavedViewDefinitionModel(2, "Prospects", new SavedViewFiltersModel()));

        ProspectResponse seen = Assert.Single(observerView.Prospects);

        // Everything else is intact: the observer is not being refused, only redacted.
        Assert.Equal("Yusuf Bektas", seen.DisplayName);
        Assert.Equal("Festival", seen.Source);
        Assert.Null(seen.StrategyNotes);
    }

    /// <summary>A view runs under the permission its target needs.</summary>
    [Fact]
    public async Task ATalentView_CannotBeCreatedWithoutTalentRead()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "views-talent-perm");
        Guid tenant = owner.Organization.Id.Value;

        // A user with no membership in this tenant, and so no representation grant.
        string strangerSubject = $"views-talent-stranger-{Guid.NewGuid():N}";
        await _fixture.SeedUserAsync(strangerSubject, "Stranger");

        using HttpClient strangerClient = _fixture.CreateClient(strangerSubject);

        using HttpResponseMessage response = await strangerClient.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views",
            new CreateSavedViewRequest(
                "Clients",
                new SavedViewDefinitionModel(2, "Talent", new SavedViewFiltersModel(ClientsOnly: true))));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Talent", "ProspectStage")]
    [InlineData("Prospects", "ScopeArea")]
    [InlineData("People", "ClientsOnly")]
    [InlineData("Tasks", "Discipline")]
    public async Task AnM4FilterOnTheWrongTarget_IsRefused(string target, string filter)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-m4-filter");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        SavedViewFiltersModel filters = filter switch
        {
            "ProspectStage" => new SavedViewFiltersModel(ProspectStage: "Courting"),
            "ScopeArea" => new SavedViewFiltersModel(ScopeArea: "Literary"),
            "ClientsOnly" => new SavedViewFiltersModel(ClientsOnly: true),
            _ => new SavedViewFiltersModel(Discipline: "Actor"),
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                $"{target} by {filter}",
                new SavedViewDefinitionModel(2, target, filters)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Contradictory filters are refused rather than silently returning nothing.</summary>
    [Fact]
    public async Task AskingForCurrentAndFormerClientsAtOnce_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-talent-both");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                "Everyone ever",
                new SavedViewDefinitionModel(
                    2,
                    "Talent",
                    new SavedViewFiltersModel(ClientsOnly: true, FormerClientsOnly: true))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A view saved before M4 still means what it meant.
    /// </summary>
    /// <remarks>
    /// Definition version 2 only added targets and filters, so version 1 documents
    /// are read rather than refused. Refusing them would have broken every view
    /// saved before this milestone for no reason at all.
    /// </remarks>
    [Fact]
    public async Task AViewSavedAtDefinitionVersionOne_StillRuns()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-v1-still-runs");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Aurelia", "Sandoval");

        SavedViewResultsResponse results = await RunAsync(
            client,
            tenant,
            "People, the old way",
            new SavedViewDefinitionModel(1, "People", new SavedViewFiltersModel(Status: "Active")));

        Assert.Equal("People", results.Target);
        Assert.Equal("Aurelia Sandoval", Assert.Single(results.People).DisplayName);
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

    private async Task<RepresentationResponse> SignAsync(
        HttpClient client,
        SeededActor actor,
        string firstName,
        string lastName,
        string scope)
    {
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, firstName, lastName);

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(person.Person.Id, "Established", Disciplines: ["Actor"])));

        RepresentationResponse representation = await CreatedAsync<RepresentationResponse>(
            client.PostAsJsonAsync(
                $"/api/v1/organizations/{tenant}/representations",
                new CreateRepresentationRequest(
                    person.Person.Id,
                    new DateOnly(2026, 1, 1),
                    actor.User.Id.Value,
                    [scope])));

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Active", new DateOnly(2026, 1, 1), representation.Version)));

        return await GetAsync<RepresentationResponse>(
            client, $"/api/v1/organizations/{tenant}/representations/{representation.Id}");
    }

    private static async Task ProfileOnlyAsync(
        HttpClient client,
        Guid tenant,
        string firstName,
        string lastName)
    {
        PersonDetailResponse person = await CreatePersonAsync(client, tenant, firstName, lastName);

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(person.Person.Id, "Emerging", Disciplines: ["Writer"])));
    }

    private static async Task StartPursuitAsync(
        HttpClient client,
        SeededActor actor,
        string firstName,
        string lastName,
        DateOnly followUp)
    {
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, firstName, lastName);

        await CreatedAsync<ProspectResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(
                person.Person.Id,
                actor.User.Id.Value,
                new DateOnly(2026, 1, 5),
                NextFollowUpOn: followUp)));
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
}
