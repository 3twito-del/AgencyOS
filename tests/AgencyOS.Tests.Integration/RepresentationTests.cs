using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M4 representation workflow, end to end against PostgreSQL.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class RepresentationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public RepresentationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole representation workflow, in the order an agency performs it.
    /// </summary>
    /// <remarks>
    /// One test on purpose: the value of M4 is that these steps connect. Separate
    /// tests would each pass while the joins between them stayed broken.
    /// </remarks>
    [Fact]
    public async Task Workflow_FromProspectToClientOverview()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-workflow");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        // 1. A person the agency knows about.
        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Imogen", "Vasquez");

        // 2. A talent profile: what the agency thinks about representing them.
        TalentDetailResponse talent = await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/talent",
            new CreateTalentProfileRequest(
                person.Person.Id,
                CareerStage: "Emerging",
                Summary: "Playwright moving into television.",
                PositioningNotes: "Strong voice; needs a staffing introduction.",
                BaseMarket: "London",
                Disciplines: ["Writer", "Creator"])));

        Assert.Equal("Imogen Vasquez", talent.Talent.DisplayName);
        Assert.Equal(["Creator", "Writer"], talent.Talent.Disciplines.Order().ToArray());

        // Not a client: there is no representation yet, and client-ness is derived.
        Assert.False(talent.Talent.IsClient);
        Assert.Null(talent.Talent.RepresentationStatus);

        // 3. Start pursuing them.
        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(
                person.Person.Id,
                actor.User.Id.Value,
                new DateOnly(2026, 2, 1),
                Source: "Royal Court showcase",
                StrategyNotes: "Approach through her agent at the theatre.",
                NextFollowUpOn: new DateOnly(2026, 2, 14))));

        Assert.Equal("Identified", prospect.Stage);

        // 4. Advance the pursuit.
        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}/advance",
            new AdvanceProspectRequest("Contacted", new DateOnly(2026, 2, 3), prospect.Version)));

        ProspectResponse contacted = await GetAsync<ProspectResponse>(
            client, $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}");

        Assert.Equal("Contacted", contacted.Stage);
        Assert.Equal(prospect.Version + 1, contacted.Version);

        // 5. Convert: the representation and the closed pursuit land together.
        RepresentationResponse representation = await CreatedAsync<RepresentationResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}/convert",
            new ConvertProspectRequest(
                new DateOnly(2026, 3, 1),
                actor.User.Id.Value,
                ["Television", "Literary"],
                contacted.Version,
                IsExclusive: true,
                Territory: "Worldwide")));

        Assert.Equal("Active", representation.Status);
        Assert.Equal(["Literary", "Television"], representation.Scopes.Select(x => x.Area).Order().ToArray());

        // The lead is derived from the team, so it is a real assignment.
        RepresentationTeamMemberResponse lead = Assert.Single(representation.Team, x => x.Role == "Lead");
        Assert.Equal(actor.User.Id.Value, lead.UserId);
        Assert.Null(lead.EndsOn);

        ProspectResponse converted = await GetAsync<ProspectResponse>(
            client, $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}");

        Assert.Equal("Converted", converted.Stage);
        Assert.Equal(representation.Id, converted.ConvertedToRepresentationId);

        // 6. They are now a client, derived from representation status.
        TalentDetailResponse asClient = await GetAsync<TalentDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}");

        Assert.True(asClient.Talent.IsClient);
        Assert.Equal("Active", asClient.Talent.RepresentationStatus);
        Assert.Equal(actor.User.Id.Value, asClient.Talent.LeadUserId);

        // 7. Credits and materials.
        await CreatedAsync<JsonElement>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/credits",
            new AddCreditRequest(
                person.Person.Id,
                "The Long Field",
                "Writing",
                Role: "Episodes 1-3",
                Status: "Released",
                Year: 2025,
                Source: "BBC press release")));

        await CreatedAsync<JsonElement>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/materials",
            new AddMaterialRequest(
                person.Person.Id,
                "Meridian",
                "Pilot",
                Status: "Ready",
                VersionLabel: "Draft 4",
                ExternalUri: "https://example.invalid/meridian-draft-4")));

        // 8. Scope expands, then the lead changes: both are history, not overwrites.
        RepresentationResponse current = await GetAsync<RepresentationResponse>(
            client, $"/api/v1/organizations/{tenant}/representations/{representation.Id}");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/scopes",
            new ChangeRepresentationScopeRequest("Film", new DateOnly(2026, 6, 1), current.Version)));

        // 9. The client overview answers the operational questions.
        ClientOverviewResponse overview = await GetAsync<ClientOverviewResponse>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}/overview");

        Assert.True(overview.Talent.Talent.IsClient);
        Assert.NotNull(overview.Representation);
        Assert.Equal(3, overview.Representation!.Scopes.Count(x => x.EndsOn is null));
        Assert.Single(overview.Credits);
        Assert.Single(overview.Materials);
        Assert.NotEmpty(overview.RecentHistory);

        // 10. History preserves the path, not just the destination.
        RepresentationHistoryEntryResponse[] history = await GetAsync<RepresentationHistoryEntryResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{person.Person.Id}/history");

        Assert.Contains(history, x => x.Kind == "prospect.stage" && x.Title.Contains("Identified", StringComparison.Ordinal));
        Assert.Contains(history, x => x.Kind == "representation.status");
        Assert.Contains(history, x => x.Kind == "representation.scope");
        Assert.Contains(history, x => x.Kind == "representation.team");

        // 11. The consequential mutations are audited.
        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        string[] actions = await context.AuditEvents
            .Where(x => x.OrganizationId == actor.Organization.Id)
            .Select(x => x.Action)
            .ToArrayAsync();

        Assert.Contains(AuditAction.TalentProfileCreated, actions);
        Assert.Contains(AuditAction.ProspectCreated, actions);
        Assert.Contains(AuditAction.ProspectConverted, actions);
        Assert.Contains(AuditAction.CreditAdded, actions);
        Assert.Contains(AuditAction.MaterialAdded, actions);
    }

    /// <summary>
    /// Concurrent conversions of one prospect produce exactly one representation.
    /// </summary>
    /// <remarks>
    /// This is the failure M4 most needs to be impossible, and the reason there is
    /// a partial unique index rather than only a handler check. Eight simultaneous
    /// attempts: one succeeds, the rest are refused, and the agency ends up
    /// believing it represents the person once.
    /// </remarks>
    [Fact]
    public async Task ConcurrentConversions_ProduceExactlyOneRepresentation()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-race");
        Guid tenant = actor.Organization.Id.Value;

        using HttpClient setup = _fixture.CreateClient(actor.Subject);

        PersonDetailResponse person = await CreatePersonAsync(setup, tenant, "Rafael", "Onwuka");

        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(setup.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(person.Person.Id, actor.User.Id.Value, new DateOnly(2026, 1, 1))));

        // Eight clients all convert the same prospect at once, each holding the
        // version they read.
        Task<HttpStatusCode>[] attempts =
        [
            .. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                using HttpClient racer = _fixture.CreateClient(actor.Subject);

                using HttpResponseMessage response = await racer.PostAsJsonAsync(
                    $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}/convert",
                    new ConvertProspectRequest(
                        new DateOnly(2026, 4, 1),
                        actor.User.Id.Value,
                        ["Acting"],
                        prospect.Version));

                return response.StatusCode;
            })),
        ];

        HttpStatusCode[] outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(x => x == HttpStatusCode.Created));

        // Everything else is refused, by version, by duplicate, or by the index.
        Assert.All(
            outcomes.Where(x => x != HttpStatusCode.Created),
            status => Assert.True(
                status is HttpStatusCode.Conflict or HttpStatusCode.InternalServerError,
                $"Unexpected status {status} from a losing conversion."));

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        int live = await context.Representations
            .CountAsync(x => x.OrganizationId == actor.Organization.Id
                && x.PersonId == new Domain.People.PersonId(person.Person.Id));

        Assert.Equal(1, live);
    }

    /// <summary>A retried conversion under one key signs the client once.</summary>
    [Fact]
    public async Task AReplayedConversion_SignsTheClientOnce()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-idem");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Noor", "Haddad");

        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(person.Person.Id, actor.User.Id.Value, new DateOnly(2026, 1, 1))));

        string key = Guid.NewGuid().ToString("N");

        ConvertProspectRequest request = new(
            new DateOnly(2026, 5, 1),
            actor.User.Id.Value,
            ["Acting"],
            prospect.Version);

        RepresentationResponse first = await ConvertWithKeyAsync(client, tenant, prospect.Id, request, key);
        RepresentationResponse replay = await ConvertWithKeyAsync(client, tenant, prospect.Id, request, key);

        // The same answer, not a second signing. Without replay the retry would
        // have been a version conflict, because the first attempt moved the
        // prospect on.
        Assert.Equal(first.Id, replay.Id);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        Assert.Equal(
            1,
            await context.Representations.CountAsync(x => x.OrganizationId == actor.Organization.Id));
    }

    /// <summary>A stale version is refused, so a queued edit cannot overwrite.</summary>
    [Fact]
    public async Task AStaleRepresentationVersion_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-stale");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        RepresentationResponse representation = await SignAsync(client, actor, "Elif", "Demirci");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Suspended", new DateOnly(2026, 7, 1), representation.Version)));

        using HttpResponseMessage stale = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Terminated", new DateOnly(2026, 8, 1), representation.Version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        JsonElement problem = await stale.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("version_conflict", problem.GetProperty("code").GetString());
    }

    /// <summary>An illegal transition is refused by the domain, not silently applied.</summary>
    [Fact]
    public async Task AnIllegalTransition_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-illegal");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        RepresentationResponse representation = await SignAsync(client, actor, "Kwame", "Boateng");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Terminated", new DateOnly(2026, 9, 1), representation.Version)));

        RepresentationResponse terminated = await GetAsync<RepresentationResponse>(
            client, $"/api/v1/organizations/{tenant}/representations/{representation.Id}");

        // Terminal means terminal. Re-signing would be a new representation.
        using HttpResponseMessage revive = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Active", new DateOnly(2026, 10, 1), terminated.Version));

        Assert.Equal(HttpStatusCode.BadRequest, revive.StatusCode);
    }

    /// <summary>A former client can be signed again, as a new relationship.</summary>
    /// <remarks>
    /// The case that justifies keeping prospecting and representation as separate
    /// concepts: with one enum there would be nowhere to put a pursuit of somebody
    /// the agency used to represent.
    /// </remarks>
    [Fact]
    public async Task AFormerClient_CanBeSignedAgain()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-resign");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        RepresentationResponse first = await SignAsync(client, actor, "Sian", "Prydderch");

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{first.Id}/transition",
            new TransitionRepresentationRequest(
                "Terminated",
                new DateOnly(2026, 9, 1),
                first.Version,
                "Moved to another agency.")));

        // A fresh pursuit of a former client is legitimate, and only possible
        // because prospecting is not the same concept as representation.
        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(first.PersonId, actor.User.Id.Value, new DateOnly(2027, 1, 1))));

        RepresentationResponse second = await CreatedAsync<RepresentationResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects/{prospect.Id}/convert",
            new ConvertProspectRequest(
                new DateOnly(2027, 2, 1),
                actor.User.Id.Value,
                ["Acting"],
                prospect.Version)));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Active", second.Status);

        // Both relationships survive: the gap is part of the record.
        RepresentationHistoryEntryResponse[] history = await GetAsync<RepresentationHistoryEntryResponse[]>(
            client, $"/api/v1/organizations/{tenant}/talent/{first.PersonId}/history?limit=100");

        Assert.Contains(history, x => x.Title.Contains("Terminated", StringComparison.Ordinal));
    }

    /// <summary>Pursuing somebody already represented is refused.</summary>
    [Fact]
    public async Task PursuingACurrentClient_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-double");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        RepresentationResponse representation = await SignAsync(client, actor, "Yusuf", "Karim");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/prospects",
            new CreateProspectRequest(representation.PersonId, actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>A team member must actually work for the tenant.</summary>
    [Fact]
    public async Task AssigningSomebodyFromAnotherTenant_IsRefused()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-team-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-team-theirs");

        using HttpClient client = _fixture.CreateClient(mine.Subject);
        Guid tenant = mine.Organization.Id.Value;

        RepresentationResponse representation = await SignAsync(client, mine, "Otto", "Lindqvist");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/team",
            new AssignRepresentationTeamMemberRequest(
                theirs.User.Id.Value,
                "Agent",
                new DateOnly(2026, 6, 1),
                representation.Version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A material link must be an address other people can open.</summary>
    /// <remarks>
    /// A device-local path is refused rather than stored: it resolves on exactly
    /// one machine and carries the author's account name into a shared record.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\Users\someone\Documents\draft.pdf")]
    [InlineData("file:///home/someone/draft.pdf")]
    [InlineData("not a uri at all")]
    public async Task ADeviceLocalMaterialPath_IsRefused(string uri)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-material-uri");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Marta", "Kowalczyk");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/materials",
            new AddMaterialRequest(person.Person.Id, "Draft", "Screenplay", ExternalUri: uri));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Credits and materials are findable by the M3 ranked search.</summary>
    [Fact]
    public async Task CreditsAndMaterials_AreSearchable()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-search");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Anouk", "Devereux");

        await CreatedAsync<JsonElement>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/credits",
            new AddCreditRequest(person.Person.Id, "Kestrel Hollow", "Directing")));

        await CreatedAsync<JsonElement>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/materials",
            new AddMaterialRequest(person.Person.Id, "Kestrel Hollow Lookbook", "Lookbook")));

        SearchResponse results = await GetAsync<SearchResponse>(
            client, $"/api/v1/organizations/{tenant}/search?q=Kestrel");

        Assert.Contains(results.Hits, x => x.Type == "Credit");
        Assert.Contains(results.Hits, x => x.Type == "Material");
    }

    /// <summary>Nothing in the representation model crosses a tenant.</summary>
    [Fact]
    public async Task RepresentationReads_NeverCrossATenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-tenant-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-tenant-theirs");

        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);
        await SignAsync(theirClient, theirs, "Private", "Client");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);

        foreach (string path in new[] { "talent", "prospects" })
        {
            using HttpResponseMessage crossing = await myClient.GetAsync(
                $"/api/v1/organizations/{theirs.Organization.Id.Value}/{path}");

            Assert.Equal(HttpStatusCode.Forbidden, crossing.StatusCode);
        }

        TalentSummaryResponse[] mineTalent = await GetAsync<TalentSummaryResponse[]>(
            myClient, $"/api/v1/organizations/{mine.Organization.Id.Value}/talent");

        Assert.Empty(mineTalent);
    }

    [Fact]
    public async Task RepresentationEndpoints_RequireAuthentication()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m4-anon");
        using HttpClient anonymous = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await anonymous.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/talent");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private async Task<RepresentationResponse> SignAsync(
        HttpClient client,
        SeededActor actor,
        string firstName,
        string lastName)
    {
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, firstName, lastName);

        RepresentationResponse representation = await CreatedAsync<RepresentationResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations",
            new CreateRepresentationRequest(
                person.Person.Id,
                new DateOnly(2026, 1, 1),
                actor.User.Id.Value,
                ["Acting"])));

        await NoContentAsync(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/representations/{representation.Id}/transition",
            new TransitionRepresentationRequest("Active", new DateOnly(2026, 1, 1), representation.Version)));

        return await GetAsync<RepresentationResponse>(
            client, $"/api/v1/organizations/{tenant}/representations/{representation.Id}");
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

    private static async Task<RepresentationResponse> ConvertWithKeyAsync(
        HttpClient client,
        Guid tenant,
        Guid prospectId,
        ConvertProspectRequest request,
        string key)
    {
        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"/api/v1/organizations/{tenant}/prospects/{prospectId}/convert")
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RepresentationResponse>())!;
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
