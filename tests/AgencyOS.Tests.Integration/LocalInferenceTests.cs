using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Ai;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The device-local inference protocol, end to end against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No model runs in any of these tests.</strong> The workstation half is
/// simulated by calling the endpoints a Windows client would call, which is what
/// makes the protocol testable on hardware that has no NPU and in CI that has no
/// GPU. What is under test is AgencyOS's half: what it discloses, what it accepts
/// back, and what it refuses (§63, ADR-0035).
/// </para>
/// <para>
/// Every refusal below is a substitution somebody could attempt with nothing more
/// than a lease identifier and a text editor.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class LocalInferenceTests
{
    private readonly AgencyOsTestFixture _fixture;

    public LocalInferenceTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // -------------------------------------------------------- the happy path

    /// <summary>
    /// A relationship brief, computed on the workstation.
    /// </summary>
    /// <remarks>
    /// The first device-local workload. Read-only, bounded context, no tool and no
    /// canonical write — chosen because it exercises the whole lease protocol
    /// without anything consequential riding on it (§7).
    /// </remarks>
    [Fact]
    public async Task ARelationshipBriefRunsOnTheDeviceAndComesBack()
    {
        Actor a = await ActorAsync("m13-brief", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Odile Ferrand");

        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        // What the workstation received: AgencyOS's own framing, the authorized
        // context, and nothing else.
        Assert.Equal("DeviceLocal", lease.Residency);
        Assert.NotEmpty(lease.Prompt);
        Assert.NotEmpty(lease.Context);
        Assert.Equal(64, lease.ContextFingerprint.Length);
        Assert.True(lease.ExpiresAt > lease.IssuedAt);

        AgentRunDetailResponse waiting = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.Equal("AwaitingLocalExecution", waiting.Run.Status);
        Assert.Equal("DeviceLocal", waiting.Run.Residency);

        SubmitLocalResultResponse accepted = await SubmitAsync(
            a, run.Id, lease.LeaseId, text: "Odile is a long-standing client.");

        Assert.True(accepted.Accepted);
        Assert.Equal("Completed", accepted.Status);

        AgentRunDetailResponse finished = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.Equal("Completed", finished.Run.Status);
        Assert.Equal("Odile is a long-standing client.", finished.Result);
    }

    /// <summary>
    /// The lease envelope carries no secret and no policy internals.
    /// </summary>
    /// <remarks>
    /// The envelope is the one place server-side material crosses to a device, so
    /// what it does <em>not</em> carry matters as much as what it does.
    /// </remarks>
    [Fact]
    public async Task TheEnvelopeCarriesNoSecrets()
    {
        Actor a = await ActorAsync("m13-envelope", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Dror Almagor");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);

        using HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/runs/{run.Id}/local-lease",
            new IssueContextLeaseRequest(WindowsLocalModel.Key));

        await EnsureAsync(response);

        string body = await response.Content.ReadAsStringAsync();

        foreach (string forbidden in new[]
        {
            "apiKey", "api_key", "secret", "password", "connectionString", "Bearer",
        })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------- what it refuses

    /// <summary>A lease is used once. A replay produces no second result.</summary>
    [Fact]
    public async Task AReplayedResultIsRefused()
    {
        Actor a = await ActorAsync("m13-replay", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Marguerite Sable");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        Assert.True((await SubmitAsync(a, run.Id, lease.LeaseId, "First.")).Accepted);

        SubmitLocalResultResponse replay = await SubmitAsync(
            a, run.Id, lease.LeaseId, "Second.");

        Assert.False(replay.Accepted);
        Assert.Equal("Consumed", replay.Refusal);

        AgentRunDetailResponse finished = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.Equal("First.", finished.Result);
    }

    /// <summary>
    /// A lease cannot cross a tenant, a user or a run.
    /// </summary>
    /// <remarks>
    /// All three answer the same way. A caller trying identifiers learns only that
    /// nothing of theirs matches, which is the same rule M12 applies to approvals.
    /// </remarks>
    [Fact]
    public async Task ALeaseCannotBePresentedByAnybodyElse()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-mine");
        Actor mine = Actor.For(_fixture, owner);
        Actor colleague = await SameTenantAsync(owner, AgencyRole.Member, "m13-theirs");
        Actor stranger = await ActorAsync("m13-other-tenant", AgencyRole.Owner);

        await EnableLocalAsync(mine);

        PersonDetailResponse person = await CreatePersonAsync(mine, "Netta Barzilai");
        AgentRunIdResponse run = await StartLocalAsync(mine, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(mine, run.Id);

        // A colleague in the same tenant, holding the identifiers.
        SubmitLocalResultResponse byColleague = await SubmitAsync(
            colleague, run.Id, lease.LeaseId, "Injected.");

        Assert.False(byColleague.Accepted);
        Assert.Equal("NotFound", byColleague.Refusal);

        // Somebody in another tenant, against their own root. The lease lookup is
        // tenant-scoped and finds nothing, so they get exactly the answer the
        // colleague got. That uniformity is the point: neither learns anything
        // about a run that is not theirs.
        SubmitLocalResultResponse byStranger = await SubmitAsync(
            stranger, run.Id, lease.LeaseId, "Injected.");

        Assert.False(byStranger.Accepted);
        Assert.Equal("NotFound", byStranger.Refusal);

        // And the run is untouched.
        AgentRunDetailResponse still = await GetAsync<AgentRunDetailResponse>(
            mine, $"ai/runs/{run.Id}");

        Assert.Equal("AwaitingLocalExecution", still.Run.Status);
    }

    /// <summary>A lease belongs to one run.</summary>
    [Fact]
    public async Task ALeaseCannotBePresentedAgainstAnotherRun()
    {
        Actor a = await ActorAsync("m13-wrong-run", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Yotam Cohen");

        AgentRunIdResponse first = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, first.Id);

        AgentRunIdResponse second = await StartLocalAsync(a, person.Person.Id);

        SubmitLocalResultResponse crossed = await SubmitAsync(
            a, second.Id, lease.LeaseId, "Wrong run.");

        Assert.False(crossed.Accepted);
        Assert.Equal("NotFound", crossed.Refusal);
    }

    /// <summary>
    /// Changed context invalidates a lease.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property the fingerprint exists for, exercised through the one lever a
    /// client actually has: the underlying record moves while the model runs. The
    /// server reassembles, the fingerprint no longer matches, and the result is
    /// refused (§B).
    /// </para>
    /// <para>
    /// Refusing here is the conservative answer rather than the obviously correct
    /// one — the brief was produced from material that has since changed, and
    /// accepting it would file an answer about a record that no longer says that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ChangedContextInvalidatesTheLease()
    {
        Actor a = await ActorAsync("m13-context-drift", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Tamar Weiss");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        // The record moves while the model is running. The name is what the
        // relationship context actually carries, so changing it is what changes
        // the material the brief was produced from -- a note the assembly does not
        // read would leave the fingerprint intact and rightly so.
        await NoContentAsync(a.Client.PutAsJsonAsync(
            $"{a.Root}/people/{person.Person.Id}",
            new UpdatePersonRequest(
                "Tamar", person.Person.Version, LastName: "Weiss-Adler")));

        SubmitLocalResultResponse stale = await SubmitAsync(
            a, run.Id, lease.LeaseId, "Produced from what the record used to say.");

        Assert.False(stale.Accepted);
        Assert.Equal("ContextMismatch", stale.Refusal);
    }

    /// <summary>A cancelled run withdraws its lease.</summary>
    /// <remarks>
    /// It does not reach the context already in the device's memory. It stops that
    /// disclosure from becoming an accepted result (§C.3).
    /// </remarks>
    [Fact]
    public async Task CancellingWithdrawsTheLease()
    {
        Actor a = await ActorAsync("m13-cancel", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Amit Regev");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        AgentRunDetailResponse waiting = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/runs/{run.Id}/cancel",
            new CancelAgentRunRequest(waiting.Run.Version)));

        SubmitLocalResultResponse late = await SubmitAsync(
            a, run.Id, lease.LeaseId, "Arrived after the cancellation.");

        Assert.False(late.Accepted);
        Assert.Equal("Invalidated", late.Refusal);

        AgentRunDetailResponse cancelled = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.Equal("Cancelled", cancelled.Run.Status);
    }

    /// <summary>A workstation that reports it cannot run says so, and nothing is sent.</summary>
    /// <remarks>
    /// The honest outcome on hardware without a device-local model, which is every
    /// machine AgencyOS is currently developed on. The run fails with a category;
    /// it does not become a cloud run (§E, §F).
    /// </remarks>
    [Fact]
    public async Task AnUnavailableLocalModelFailsTheRunAndSendsNothing()
    {
        Actor a = await ActorAsync("m13-unavailable", AgencyRole.Owner);
        await EnableLocalAsync(a);

        FakeModelProvider fake = Fake();

        PersonDetailResponse person = await CreatePersonAsync(a, "Roni Shaked");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        SubmitLocalResultResponse reported = await SubmitAsync(
            a, run.Id, lease.LeaseId, text: null, failure: "LocalModelNotReady");

        Assert.True(reported.Accepted);
        Assert.Equal("Failed", reported.Status);

        AgentRunDetailResponse failed = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.Equal("Failed", failed.Run.Status);
        Assert.Equal("LocalModelNotReady", failed.Run.Failure);

        // The decisive assertion: no cloud provider was reached at any point.
        Assert.Empty(fake.Requests);
    }

    /// <summary>
    /// The server refuses to run a device-local model itself.
    /// </summary>
    /// <remarks>
    /// The no-fallback guarantee at the layer every path goes through. Asking the
    /// gateway to complete a device-local model means some code path lost track of
    /// where the material was permitted to go.
    /// </remarks>
    [Fact]
    public async Task TheServerWillNotRunADeviceLocalModelItself()
    {
        Actor a = await ActorAsync("m13-no-fallback", AgencyRole.Owner);
        await EnableLocalAsync(a);

        FakeModelProvider fake = Fake();
        fake.ScriptText("This must never be reached.");

        PersonDetailResponse person = await CreatePersonAsync(a, "Gil Peretz");

        // A run started against the device-local model but never leased: the
        // runtime has no path that would execute it server-side.
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);

        AgentRunDetailResponse waiting = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.NotEqual("Completed", waiting.Run.Status);
        Assert.Empty(fake.Requests);
    }

    /// <summary>A cloud run cannot take a lease.</summary>
    /// <remarks>
    /// The inverse of the fallback guarantee. Cloud inference runs on the server's
    /// own connection, so a lease for it would be a second and weaker path to the
    /// same context.
    /// </remarks>
    [Fact]
    public async Task ACloudRunCannotTakeALease()
    {
        Actor a = await ActorAsync("m13-cloud-lease", AgencyRole.Owner);
        await EnableLocalAsync(a);

        Fake().ScriptText("A cloud brief.");

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a, "ai/runs", new StartAgentRunRequest("ResearchCopilot", "Anything."));

        using HttpResponseMessage response = await a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/runs/{run.Id}/local-lease",
            new IssueContextLeaseRequest(WindowsLocalModel.Key));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ the schema

    /// <summary>A consumed lease cannot be re-opened, by any path.</summary>
    [Fact]
    public async Task TheDatabaseRefusesToReopenALease()
    {
        Actor a = await ActorAsync("m13-lease-trigger", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Shira Halevi");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        await SubmitAsync(a, run.Id, lease.LeaseId, "Done.");

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_context_leases SET state = 1, resolved_at = NULL WHERE id = @id",
                ("id", lease.LeaseId)));

        Assert.Contains("already resolved", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What a lease binds to cannot be swapped underneath it.</summary>
    [Fact]
    public async Task TheDatabaseRefusesToRebindALease()
    {
        Actor a = await ActorAsync("m13-lease-rebind", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Eyal Baruch");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_context_leases SET context_fingerprint = @fingerprint "
                    + "WHERE id = @id",
                ("fingerprint", new string('a', 64)),
                ("id", lease.LeaseId)));

        Assert.Contains("cannot be changed", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A cloud lease cannot be written at all.</summary>
    [Fact]
    public async Task TheDatabaseRefusesACloudLease()
    {
        Actor a = await ActorAsync("m13-lease-residency", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Noa Erlich");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_context_leases SET residency = 1 WHERE id = @id",
                ("id", lease.LeaseId)));

        // The trigger sees the update before the CHECK constraint does, and its
        // message is the more informative of the two: residency is part of what a
        // lease binds to, and none of that can be swapped underneath it.
        Assert.Contains(
            "cannot be changed", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- the audit

    /// <summary>
    /// Disclosure to a device is recorded.
    /// </summary>
    /// <remarks>
    /// The one fact a reviewer cannot reconstruct from anything else afterwards:
    /// material left the server for a workstation, and no later revocation reaches
    /// into that device's memory (§C.3).
    /// </remarks>
    [Fact]
    public async Task DisclosureToADeviceIsAudited()
    {
        Actor a = await ActorAsync("m13-audit", AgencyRole.Owner);
        await EnableLocalAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Liat Mizrahi");
        AgentRunIdResponse run = await StartLocalAsync(a, person.Person.Id);
        ContextLeaseResponse lease = await LeaseAsync(a, run.Id);

        await SubmitAsync(a, run.Id, lease.LeaseId, "A brief.");

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        string leaseId = lease.LeaseId.ToString();
        string runId = run.Id.ToString();

        List<Domain.Audit.AuditEvent> events = await context.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == leaseId || x.EntityId == runId)
            .ToListAsync();

        Assert.Contains(events, x => x.Action == AuditAction.AiContextLeaseIssued);
        Assert.Contains(events, x => x.Action == AuditAction.AiLocalResultAccepted);
        Assert.All(events, x => Assert.NotNull(x.ActorUserId));
    }

    // ------------------------------------------------------------- helpers

    private FakeModelProvider Fake()
    {
        FakeModelProvider provider = _fixture.Factory.Services
            .GetRequiredService<FakeModelProvider>();

        provider.Reset();

        return provider;
    }

    private static Task<AgentRunIdResponse> StartLocalAsync(Actor actor, Guid personId) =>
        PostAsync<AgentRunIdResponse>(
            actor,
            "ai/runs",
            new StartAgentRunRequest(
                "RelationshipBrief",
                "Brief me on this person.",
                "Person",
                personId,
                WindowsLocalModel.Key,
                Residency: "DeviceLocal"));

    private static Task<ContextLeaseResponse> LeaseAsync(Actor actor, Guid runId) =>
        PostAsync<ContextLeaseResponse>(
            actor,
            $"ai/runs/{runId}/local-lease",
            new IssueContextLeaseRequest(WindowsLocalModel.Key));

    private static async Task<SubmitLocalResultResponse> SubmitAsync(
        Actor actor, Guid runId, Guid leaseId, string? text = null, string? failure = null)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/ai/runs/{runId}/local-result",
            new SubmitLocalResultRequest(leaseId, text, failure, "Cpu"));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<SubmitLocalResultResponse>())!;
    }

    private static async Task EnableLocalAsync(Actor actor)
    {
        foreach (string provider in new[] { "fake", WindowsLocalModel.ProviderKey })
        {
            using HttpResponseMessage response = await actor.Client.PutAsJsonAsync(
                $"{actor.Root}/ai/policies/{provider}",
                new UpdateAiProviderPolicyRequest(true, "Confidential", true, 0));

            await EnsureAsync(response);
        }
    }

    private async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private sealed record Actor(HttpClient Client, string Root)
    {
        public static Actor For(AgencyOsTestFixture fixture, SeededActor actor) =>
            new(
                fixture.CreateClient(actor.Subject),
                $"/api/v1/organizations/{actor.Organization.Id.Value}");
    }

    private async Task<Actor> ActorAsync(string label, AgencyRole role = AgencyRole.Member)
    {
        SeededActor actor = await _fixture.SeedActorAsync(role, label);

        return Actor.For(_fixture, actor);
    }

    private async Task<Actor> SameTenantAsync(SeededActor tenant, AgencyRole role, string label)
    {
        string subject = $"{label}-{Guid.NewGuid():N}";

        Domain.Identity.User user = await _fixture
            .SeedUserAsync(subject, $"{label} {role}")
            .ConfigureAwait(false);

        await _fixture
            .SeedMembershipAsync(tenant.Organization.Id, user.Id, role, tenant.User.Id)
            .ConfigureAwait(false);

        return new Actor(
            _fixture.CreateClient(subject),
            $"/api/v1/organizations/{tenant.Organization.Id.Value}");
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(Actor actor, string name)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/people", new CreatePersonRequest(name));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<T> GetAsync<T>(Actor actor, string route)
    {
        using HttpResponseMessage response = await actor.Client.GetAsync($"{actor.Root}/{route}");

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PostAsync<T>(Actor actor, string route, object body)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/{route}", body);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        await EnsureAsync(response);
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
