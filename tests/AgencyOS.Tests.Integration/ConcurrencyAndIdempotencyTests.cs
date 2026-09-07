using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The two guarantees the offline write queue rests on: a stale write cannot
/// overwrite, and a retried command cannot take effect twice.
/// </summary>
/// <remarks>
/// Both are enforced server-side. A client that behaves correctly is not the
/// mechanism; it is the thing being protected against. These tests submit
/// directly over HTTP, without the client, for exactly that reason.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ConcurrencyAndIdempotencyTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ConcurrencyAndIdempotencyTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------ concurrency

    /// <summary>A created record starts at version 1 and the client is told so.</summary>
    [Fact]
    public async Task ACreatedRecord_CarriesAVersion()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "version-new");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        PersonDetailResponse person = await CreatePersonAsync(client, actor, "Sarah", "Klein");

        Assert.Equal(1, person.Person.Version);
    }

    [Fact]
    public async Task UpdatingWithTheCurrentVersion_SucceedsAndAdvancesIt()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "version-ok");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, actor, "Sarah", "Klein");

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}",
            new UpdatePersonRequest("Sarah", person.Person.Version, "Klein-Moreau"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PersonDetailResponse updated = (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;

        Assert.Equal(2, updated.Person.Version);
    }

    /// <summary>
    /// A stale write is refused, and told exactly what it was refused against.
    /// </summary>
    /// <remarks>
    /// This is the property that makes last-write-wins unreachable. The response
    /// carries both versions and a machine-readable code, because the client has to
    /// present a decision to a person, not an error.
    /// </remarks>
    [Fact]
    public async Task UpdatingWithAStaleVersion_IsRefusedWithBothVersions()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "version-stale");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, actor, "Sarah", "Klein");

        // Somebody else edits first.
        using (HttpResponseMessage first = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}",
            new UpdatePersonRequest("Sarah", 1, "Klein-Moreau")))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        // This caller is still holding version 1.
        using HttpResponseMessage stale = await client.PutAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}",
            new UpdatePersonRequest("Sarah", 1, "Kline"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        JsonElement problem = await stale.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("version_conflict", problem.GetProperty("code").GetString());
        Assert.Equal(1, problem.GetProperty("expectedVersion").GetInt32());
        Assert.Equal(2, problem.GetProperty("actualVersion").GetInt32());

        // The refused write left nothing behind.
        using HttpResponseMessage read = await client.GetAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}");

        PersonDetailResponse current = (await read.Content.ReadFromJsonAsync<PersonDetailResponse>())!;

        Assert.Equal("Sarah Klein-Moreau", current.Person.DisplayName);
    }

    /// <summary>
    /// The version token is a required field, so it cannot be omitted into
    /// last-write-wins.
    /// </summary>
    /// <remarks>
    /// An optional concurrency token is last-write-wins with extra steps: whoever
    /// forgets it wins. Making it required means a client that does not send it is
    /// refused rather than quietly privileged.
    /// </remarks>
    [Fact]
    public async Task OmittingTheVersion_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "version-missing");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, actor, "Sarah", "Klein");

        using StringContent body = new(
            """{"firstName":"Sarah","lastName":"Kline"}""",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.PutAsync(
            $"/api/v1/organizations/{tenant}/people/{person.Person.Id}",
            body);

        // Absent expectedVersion binds as 0, which no live record ever holds.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Task transitions are guarded too, so an offline completion cannot overwrite.</summary>
    [Fact]
    public async Task CompletingATaskWithAStaleVersion_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "task-version");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        Guid taskId = await CreateTaskAsync(client, tenant, "Call the studio back");

        using (HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks/{taskId}/complete",
            new TaskTransitionRequest(1)))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        // A second client still believes the task is at version 1.
        using HttpResponseMessage stale = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks/{taskId}/complete",
            new TaskTransitionRequest(1));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    // ----------------------------------------------------------- idempotency

    /// <summary>
    /// The same key submitted twice produces one record and one answer.
    /// </summary>
    /// <remarks>
    /// The <c>AtMostOneEffect</c> invariant from <c>specs/OfflineWriteQueue.tla</c>,
    /// asserted against the server. This is the case the whole queue exists for: a
    /// reconnect after a lost response.
    /// </remarks>
    [Fact]
    public async Task TheSameKeySubmittedTwice_CreatesOneRecord()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-once");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        string key = Guid.NewGuid().ToString("N");
        CreatePersonRequest request = new("Ingrid", "Solberg", Title: "Producer");

        PersonDetailResponse first = await PostWithKeyAsync<PersonDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/people", request, key, HttpStatusCode.Created);

        PersonDetailResponse replayed = await PostWithKeyAsync<PersonDetailResponse>(
            client, $"/api/v1/organizations/{tenant}/people", request, key, HttpStatusCode.Created);

        // The identical answer, not a second person.
        Assert.Equal(first.Person.Id, replayed.Person.Id);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        int count = await context.People
            .CountAsync(x => x.OrganizationId == actor.Organization.Id && x.DisplayName == "Ingrid Solberg");

        Assert.Equal(1, count);
    }

    /// <summary>A task completed twice under one key is completed once.</summary>
    [Fact]
    public async Task AReplayedTransition_DoesNotConflictWithItself()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-transition");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        Guid taskId = await CreateTaskAsync(client, tenant, "Send the contract");

        string key = Guid.NewGuid().ToString("N");
        TaskTransitionRequest request = new(1);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpRequestMessage message = new(
                HttpMethod.Post,
                $"/api/v1/organizations/{tenant}/tasks/{taskId}/complete")
            {
                Content = JsonContent.Create(request),
            };

            message.Headers.Add(ClientHeaders.IdempotencyKey, key);

            using HttpResponseMessage response = await client.SendAsync(message);

            // Without replay the second attempt would be a version conflict,
            // because the first attempt moved the task to version 2.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    /// <summary>
    /// One key for two different requests is refused, because either answer would
    /// be wrong.
    /// </summary>
    [Fact]
    public async Task OneKeyForTwoDifferentRequests_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-mismatch");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        string key = Guid.NewGuid().ToString("N");

        await PostWithKeyAsync<PersonDetailResponse>(
            client,
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest("Anahit", "Petrosyan"),
            key,
            HttpStatusCode.Created);

        using HttpRequestMessage message = new(HttpMethod.Post, $"/api/v1/organizations/{tenant}/people")
        {
            Content = JsonContent.Create(new CreatePersonRequest("Someone", "Else")),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("idempotency_key_reused", problem.GetProperty("code").GetString());
    }

    /// <summary>Keys are scoped to a tenant, so one can never replay another's answer.</summary>
    [Fact]
    public async Task KeysAreScopedToTheirTenant()
    {
        SeededActor first = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-tenant-a");
        SeededActor second = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-tenant-b");

        string key = Guid.NewGuid().ToString("N");
        CreatePersonRequest request = new("Delphine", "Vanterpool");

        using HttpClient firstClient = _fixture.CreateClient(first.Subject);
        using HttpClient secondClient = _fixture.CreateClient(second.Subject);

        PersonDetailResponse a = await PostWithKeyAsync<PersonDetailResponse>(
            firstClient,
            $"/api/v1/organizations/{first.Organization.Id.Value}/people",
            request,
            key,
            HttpStatusCode.Created);

        PersonDetailResponse b = await PostWithKeyAsync<PersonDetailResponse>(
            secondClient,
            $"/api/v1/organizations/{second.Organization.Id.Value}/people",
            request,
            key,
            HttpStatusCode.Created);

        Assert.NotEqual(a.Person.Id, b.Person.Id);
    }

    /// <summary>A request without a key behaves exactly as it did before M3.</summary>
    [Fact]
    public async Task WithoutAKey_NothingIsRecordedAndNothingChanges()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-none");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        CreatePersonRequest request = new("Bruno", "Ferreira");

        PersonDetailResponse first = await CreatePersonAsync(client, actor, "Bruno", "Ferreira");
        PersonDetailResponse second = await CreatePersonAsync(client, actor, "Bruno", "Ferreira");

        // Two submissions without a key are two commands, which is correct: the
        // caller never asked for them to be treated as one.
        Assert.NotEqual(first.Person.Id, second.Person.Id);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        Assert.Empty(await context.IdempotencyKeys
            .Where(x => x.OrganizationId == actor.Organization.Id)
            .ToListAsync());
    }

    /// <summary>
    /// A queued command is re-authorized when it runs, not when it was captured.
    /// </summary>
    /// <remarks>
    /// Offline possession of a record is not permission to change it later. A
    /// build that has since been revoked is refused for mutations even with a
    /// valid key, because release enforcement is the outermost gate.
    /// </remarks>
    [Fact]
    public async Task ARevokedBuild_IsRefusedEvenWithAnIdempotencyKey()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-revoked");

        using HttpClient revoked = _fixture.CreateClient(
            actor.Subject,
            clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people")
        {
            Content = JsonContent.Create(new CreatePersonRequest("Should", "Never")),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await revoked.SendAsync(message);

        // Revoked answers 403 and incompatible answers 426; both mean the build
        // may not write. Release enforcement is the outermost gate, so it refuses
        // before the key is even reserved.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        Assert.Empty(await context.People
            .Where(x => x.OrganizationId == actor.Organization.Id && x.DisplayName == "Should Never")
            .ToListAsync());
    }

    /// <summary>A key longer than the recorded column is refused rather than truncated.</summary>
    [Fact]
    public async Task AnOverlongKey_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "idem-long");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people")
        {
            Content = JsonContent.Create(new CreatePersonRequest("Too", "Long")),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, new string('k', 200));

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        HttpClient client,
        SeededActor actor,
        string firstName,
        string lastName)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/people",
            new CreatePersonRequest(firstName, lastName));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient client, Guid tenant, string title)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks",
            new CreateTaskRequest(title));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<T> PostWithKeyAsync<T>(
        HttpClient client,
        string uri,
        object body,
        string key,
        HttpStatusCode expected)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(expected, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
