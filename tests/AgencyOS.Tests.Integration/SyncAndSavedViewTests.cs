using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Sync;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The change feed a client caches from, and the saved views it lists with.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class SyncTests
{
    private readonly AgencyOsTestFixture _fixture;

    public SyncTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A first sync from position zero returns everything the tenant holds.
    /// </summary>
    /// <remarks>
    /// There is one mechanism, not two: "everything" and "what changed" are the
    /// same query, because the migration backfills an entry per existing record. A
    /// separate snapshot endpoint would need its own consistency argument against
    /// the feed's.
    /// </remarks>
    [Fact]
    public async Task FirstSync_ReturnsEverythingTheTenantHolds()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-first");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Sarah", "Klein");
        await CreateCompanyAsync(client, tenant, "Vertex Studios");
        await CreateTaskAsync(client, tenant, "Send the deck");

        SyncChangesResponse page = await ReadChangesAsync(client, tenant, cursor: 0);

        Assert.Equal(3, page.Changes.Count);
        Assert.Single(page.People);
        Assert.Single(page.Companies);
        Assert.Single(page.Tasks);
        Assert.True(page.Cursor > 0);
        Assert.False(page.HasMore);
    }

    /// <summary>
    /// Sequence order is commit order, with no gaps below the cursor.
    /// </summary>
    /// <remarks>
    /// The property a bare <c>BIGSERIAL</c> would not give: two transactions can
    /// take values 5 and 6 and commit in the opposite order, and a client that read
    /// to 6 would never see 5. Positions come from a counter row held under its
    /// lock until commit, so this holds (ADR-0013).
    /// </remarks>
    [Fact]
    public async Task FeedSequences_AreStrictlyIncreasingAndGapless()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-order");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        for (int index = 0; index < 6; index++)
        {
            await CreatePersonAsync(client, tenant, $"Person{index}", "Sequence");
        }

        SyncChangesResponse page = await ReadChangesAsync(client, tenant, cursor: 0);

        long[] sequences = [.. page.Changes.Select(x => x.Sequence)];

        Assert.Equal(sequences.OrderBy(x => x).ToArray(), sequences);
        Assert.Equal(sequences.Length, sequences.Distinct().Count());
        Assert.Equal(sequences[^1] - sequences[0] + 1, sequences.Length);
    }

    /// <summary>An incremental sync returns only what happened after the cursor.</summary>
    [Fact]
    public async Task IncrementalSync_ReturnsOnlyWhatIsNew()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-incremental");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "First", "Sync");

        SyncChangesResponse initial = await ReadChangesAsync(client, tenant, cursor: 0);
        long cursor = initial.Cursor;

        SyncChangesResponse quiet = await ReadChangesAsync(client, tenant, cursor);

        Assert.Empty(quiet.Changes);
        Assert.Equal(cursor, quiet.Cursor);

        await CreatePersonAsync(client, tenant, "Second", "Sync");

        SyncChangesResponse next = await ReadChangesAsync(client, tenant, cursor);

        Assert.Single(next.Changes);
        Assert.True(next.Cursor > cursor);
        Assert.Equal("Second Sync", Assert.Single(next.People).DisplayName);
    }

    /// <summary>An update produces a feed entry carrying the record's current state.</summary>
    [Fact]
    public async Task AnUpdate_AppearsInTheFeedWithItsNewVersion()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-update");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Adaeze", "Okonkwo");

        long cursor = (await ReadChangesAsync(client, tenant, cursor: 0)).Cursor;

        using (HttpResponseMessage update = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}",
            new UpdatePersonRequest("Adaeze", 1, "Okonkwo-Bello")))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        SyncChangesResponse page = await ReadChangesAsync(client, tenant, cursor);

        Assert.Equal("Person", Assert.Single(page.Changes).EntityType);
        Assert.Equal("Upsert", page.Changes[0].Kind);
        Assert.Equal(2, Assert.Single(page.People).Version);
    }

    /// <summary>Reading a page twice from the same cursor gives the same answer.</summary>
    [Fact]
    public async Task ReadingTheSameCursorTwice_IsIdempotent()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-repeat");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Repeat", "Read");

        SyncChangesResponse first = await ReadChangesAsync(client, tenant, cursor: 0);
        SyncChangesResponse second = await ReadChangesAsync(client, tenant, cursor: 0);

        Assert.Equal(first.Cursor, second.Cursor);
        Assert.Equal(
            first.Changes.Select(x => x.Sequence).ToArray(),
            second.Changes.Select(x => x.Sequence).ToArray());
    }

    [Fact]
    public async Task Feed_Paginates()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-page");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        for (int index = 0; index < 4; index++)
        {
            await CreatePersonAsync(client, tenant, $"Page{index}", "Sample");
        }

        SyncChangesResponse first = await ReadChangesAsync(client, tenant, cursor: 0, take: 2);

        Assert.Equal(2, first.Changes.Count);
        Assert.True(first.HasMore);

        SyncChangesResponse second = await ReadChangesAsync(client, tenant, first.Cursor, take: 2);

        Assert.Equal(2, second.Changes.Count);
        Assert.False(second.HasMore);
        Assert.All(second.Changes, change => Assert.True(change.Sequence > first.Cursor));
    }

    /// <summary>A tenant's feed contains nothing from any other tenant.</summary>
    [Fact]
    public async Task Feed_IsScopedToItsTenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-theirs");

        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);
        await CreatePersonAsync(theirClient, theirs.Organization.Id.Value, "Their", "Person");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);

        SyncChangesResponse mineFeed = await ReadChangesAsync(myClient, mine.Organization.Id.Value, cursor: 0);

        Assert.Empty(mineFeed.Changes);

        using HttpResponseMessage crossing = await myClient.GetAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/sync/changes?cursor=0");

        Assert.Equal(HttpStatusCode.Forbidden, crossing.StatusCode);
    }

    /// <summary>Each tenant's positions start at one, independently of every other.</summary>
    [Fact]
    public async Task EachTenantHasItsOwnSequence()
    {
        SeededActor first = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-seq-a");
        SeededActor second = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-seq-b");

        using HttpClient firstClient = _fixture.CreateClient(first.Subject);
        using HttpClient secondClient = _fixture.CreateClient(second.Subject);

        await CreatePersonAsync(firstClient, first.Organization.Id.Value, "Tenant", "One");
        await CreatePersonAsync(secondClient, second.Organization.Id.Value, "Tenant", "Two");

        SyncChangesResponse a = await ReadChangesAsync(firstClient, first.Organization.Id.Value, cursor: 0);
        SyncChangesResponse b = await ReadChangesAsync(secondClient, second.Organization.Id.Value, cursor: 0);

        Assert.Equal(1, Assert.Single(a.Changes).Sequence);
        Assert.Equal(1, Assert.Single(b.Changes).Sequence);
    }

    [Fact]
    public async Task Head_ReportsThePositionAFullSyncWouldReach()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-head");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Head", "Check");

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{tenant}/sync/head");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement head = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            (await ReadChangesAsync(client, tenant, cursor: 0)).Cursor,
            head.GetProperty("cursor").GetInt64());
    }

    [Fact]
    public async Task Feed_RequiresAuthentication()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "sync-anon");
        using HttpClient anonymous = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await anonymous.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/sync/changes?cursor=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private static async Task<SyncChangesResponse> ReadChangesAsync(
        HttpClient client,
        Guid tenant,
        long cursor,
        int? take = null)
    {
        string uri = $"/api/v1/organizations/{tenant}/sync/changes?cursor={cursor}";

        if (take is { } size)
        {
            uri += $"&take={size}";
        }

        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SyncChangesResponse>())!;
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        HttpClient client,
        Guid tenant,
        string firstName,
        string lastName)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(firstName, lastName));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task CreateCompanyAsync(HttpClient client, Guid tenant, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest(name, "Studio"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task CreateTaskAsync(HttpClient client, Guid tenant, string title)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks",
            new CreateTaskRequest(title));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

/// <summary>
/// Saved views: private to their owner, versioned in both senses.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class SavedViewTests
{
    private readonly AgencyOsTestFixture _fixture;

    public SavedViewTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ASavedView_CanBeCreatedListedUpdatedAndDeleted()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-crud");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        SavedViewResponse created = await CreateAsync(client, tenant, "Priority contacts", "People");

        Assert.Equal(1, created.Version);
        Assert.Equal(1, created.DefinitionVersion);
        Assert.Equal("People", created.Target);

        SavedViewResponse[] listed = await ListAsync(client, tenant);

        Assert.Contains(listed, x => x.Id == created.Id);

        using (HttpResponseMessage update = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views/{created.Id}",
            new UpdateSavedViewRequest("Key contacts", Definition("People"), created.Version)))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            SavedViewResponse updated = (await update.Content.ReadFromJsonAsync<SavedViewResponse>())!;

            Assert.Equal("Key contacts", updated.Name);
            Assert.Equal(2, updated.Version);
        }

        using (HttpResponseMessage delete = await client.DeleteAsync(
            $"/api/v1/organizations/{tenant}/saved-views/{created.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.DoesNotContain(await ListAsync(client, tenant), x => x.Id == created.Id);
    }

    /// <summary>Editing a saved view from a stale copy is refused.</summary>
    [Fact]
    public async Task UpdatingWithAStaleVersion_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-stale");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        SavedViewResponse created = await CreateAsync(client, tenant, "Stale test", "People");

        using (HttpResponseMessage first = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views/{created.Id}",
            new UpdateSavedViewRequest("Renamed once", Definition("People"), 1)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using HttpResponseMessage stale = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views/{created.Id}",
            new UpdateSavedViewRequest("Renamed twice", Definition("People"), 1));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    /// <summary>
    /// A definition version the server does not understand is rejected, not guessed at.
    /// </summary>
    /// <remarks>
    /// The whole point of versioning the document: filter semantics can change
    /// without silently reinterpreting views saved months earlier.
    /// </remarks>
    [Fact]
    public async Task AnUnknownDefinitionVersion_IsRejected()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-version");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                "From the future",
                new SavedViewDefinitionModel(99, "People", new SavedViewFiltersModel())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A filter meaningless for the target is rejected rather than ignored.</summary>
    [Fact]
    public async Task AFilterThatCannotApplyToTheTarget_IsRejected()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-filter");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/saved-views",
            new CreateSavedViewRequest(
                "Companies that are overdue",
                new SavedViewDefinitionModel(1, "Companies", new SavedViewFiltersModel(OverdueOnly: true))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TwoViewsWithTheSameName_AreRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "views-duplicate");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreateAsync(client, tenant, "Duplicate name", "People");

        using HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views",
            new CreateSavedViewRequest("Duplicate name", Definition("People")));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// A saved view belongs to its owner alone.
    /// </summary>
    /// <remarks>
    /// Sharing means deciding what happens when the owner's permissions differ from
    /// a viewer's. That is a real design question, deferred until there is a reason
    /// to answer it - so until then, private means private.
    /// </remarks>
    [Fact]
    public async Task OneUsersSavedViews_AreInvisibleToAnother()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Member, "views-owner");

        // A second member of the same tenant.
        string otherSubject = $"views-other-{Guid.NewGuid():N}";

        Domain.Identity.User other = await _fixture.SeedUserAsync(otherSubject, "Other Member");

        await _fixture.SeedMembershipAsync(owner.Organization.Id, other.Id, AgencyRole.Member, owner.User.Id);

        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);
        Guid tenant = owner.Organization.Id.Value;

        SavedViewResponse mine = await CreateAsync(ownerClient, tenant, "Only mine", "People");

        using HttpClient otherClient = _fixture.CreateClient(otherSubject);

        Assert.DoesNotContain(await ListAsync(otherClient, tenant), x => x.Id == mine.Id);

        using HttpResponseMessage direct = await otherClient.GetAsync(
            $"/api/v1/organizations/{tenant}/saved-views/{mine.Id}");

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
    }

    [Fact]
    public async Task SavedViews_NeverCrossATenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "views-tenant-a");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "views-tenant-b");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage crossing = await myClient.GetAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/saved-views");

        Assert.Equal(HttpStatusCode.Forbidden, crossing.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private static SavedViewDefinitionModel Definition(string target) =>
        new(1, target, new SavedViewFiltersModel(Status: "Active"));

    private static async Task<SavedViewResponse> CreateAsync(
        HttpClient client,
        Guid tenant,
        string name,
        string target)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/saved-views",
            new CreateSavedViewRequest(name, Definition(target)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SavedViewResponse>())!;
    }

    private static async Task<SavedViewResponse[]> ListAsync(HttpClient client, Guid tenant)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{tenant}/saved-views");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SavedViewResponse[]>())!;
    }
}
