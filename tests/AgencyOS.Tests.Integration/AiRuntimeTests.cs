using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Ai;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M12 AI runtime, end to end against PostgreSQL and a deterministic model.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No test here reaches a model provider.</strong> Every model answer is
/// scripted, because the properties under test are AgencyOS's and not a model's: a
/// run that changes nothing without a person, an approval that binds to one exact
/// action, a canonical effect that happens at most once, and an organization that
/// transmits nothing until it says otherwise. A real provider could not be made to
/// produce the hostile answers these tests depend on (§74, §86).
/// </para>
/// <para>
/// The database-level refusals are exercised directly rather than through the API,
/// for the same reason M11 does it: testing only the aggregate would prove that
/// one code path is careful, not that the schema is.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AiRuntimeTests
{
    private readonly AgencyOsTestFixture _fixture;

    public AiRuntimeTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // -------------------------------------------------------- the happy path

    /// <summary>
    /// A brief, from asking to reading it.
    /// </summary>
    /// <remarks>
    /// The trace is the assertion that matters. Every step says whether the model
    /// was asked or AgencyOS acted, which is what makes a run explicable rather
    /// than merely logged (§64).
    /// </remarks>
    [Fact]
    public async Task ARun_CompletesAndRecordsWhatHappened()
    {
        Actor a = await ActorAsync("m12-brief", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptText("Northgate has been quiet since August.");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "What is Northgate doing?");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Completed", run.Run.Status);
        Assert.Equal("None", run.Run.Failure);
        Assert.Equal("Northgate has been quiet since August.", run.Result);
        Assert.Equal(1, run.Run.ModelInvocationCount);
        Assert.NotNull(run.Run.CompletedAt);

        // Which wording produced this. The wording itself is in source control,
        // and no route returns it.
        Assert.NotEmpty(run.PromptTemplateId);
        Assert.True(run.PromptTemplateVersion >= 1);

        Assert.Contains(run.Steps, x => x.Kind == "ContextAssembled");
        Assert.Contains(run.Steps, x => x.Kind == "ModelInvocation");
        Assert.Equal([1, 2], run.Steps.Select(x => x.Sequence).ToArray());
    }

    /// <summary>A read-only tool runs without asking anybody.</summary>
    [Fact]
    public async Task AReadOnlyToolRunsWithoutApproval()
    {
        Actor a = await ActorAsync("m12-read", AgencyRole.Owner);
        await EnableProviderAsync(a);

        PersonDetailResponse person = await CreatePersonAsync(a, "Odile Ferrand");

        Fake()
            .ScriptToolCall("person.get", $$"""{"id":"{{person.Person.Id}}"}""")
            .ScriptText("She is a client.");

        AgentRunIdResponse started = await StartAsync(a, "RelationshipBrief", "Brief me on Odile.");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Completed", run.Run.Status);
        Assert.Equal(1, run.Run.ToolCallCount);

        AiToolRequestResponse request = Assert.Single(run.ToolRequests);
        Assert.Equal("person.get", request.ToolName);
        Assert.Equal("ReadOnly", request.Effect);
        Assert.Equal("Executed", request.Status);

        // Nothing was put to anybody.
        Assert.Empty(await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));
    }

    // ------------------------------------------------------- proposal to act

    /// <summary>
    /// The protocol M12 exists to prove: propose, approve, execute, once.
    /// </summary>
    /// <remarks>
    /// The intermediate assertion is the important one. Between the run parking and
    /// the person approving, the task does not exist — which is the difference
    /// between a model that proposes and a model that acts (§41).
    /// </remarks>
    [Fact]
    public async Task AWrite_IsProposedThenApprovedThenExecutedOnce()
    {
        Actor a = await ActorAsync("m12-write", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall(
            "task.create",
            """{"title":"Call Northgate about the slate","priority":"High"}""");

        AgentRunIdResponse started = await StartAsync(
            a, "ResearchCopilot", "Chase Northgate.");

        AgentRunDetailResponse parked = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{started.Id}");

        Assert.Equal("AwaitingApproval", parked.Run.Status);

        AiToolRequestResponse request = Assert.Single(parked.ToolRequests);
        Assert.Equal("CanonicalWrite", request.Effect);
        Assert.Equal("AwaitingApproval", request.Status);

        // Nothing has happened yet, and that is the whole point.
        Assert.DoesNotContain(
            await GetListAsync<TaskResponse>(a, "tasks"),
            x => x.Title == "Call Northgate about the slate");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        // What the person is shown is AgencyOS's account of the validated
        // arguments, not the model's account of its own request.
        Assert.Contains("Call Northgate about the slate", approval.Summary, StringComparison.Ordinal);
        Assert.Contains("priority High", approval.Summary, StringComparison.Ordinal);
        Assert.False(approval.HasExpired);
        Assert.Equal("Pending", approval.Decision);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/approvals/{approval.Id}/decision",
            new DecideApprovalRequest(Approve: true, approval.Version)));

        ExecuteApprovedToolResponse executed = await PostAsync<ExecuteApprovedToolResponse>(
            a, $"ai/tool-requests/{approval.ToolRequestId}/execute", new { });

        Assert.True(executed.Succeeded);

        TaskResponse task = Assert.Single(
            await GetListAsync<TaskResponse>(a, "tasks"),
            x => x.Title == "Call Northgate about the slate");

        Assert.Equal("High", task.Priority);

        // At most one canonical effect. A second execution is refused rather than
        // creating a second task.
        ExecuteApprovedToolResponse again = await PostAsync<ExecuteApprovedToolResponse>(
            a, $"ai/tool-requests/{approval.ToolRequestId}/execute", new { });

        Assert.False(again.Succeeded);
        Assert.Single(
            await GetListAsync<TaskResponse>(a, "tasks"),
            x => x.Title == "Call Northgate about the slate");
    }

    /// <summary>A rejection executes nothing, and says so if asked again.</summary>
    [Fact]
    public async Task ARejectedProposal_ExecutesNothing()
    {
        Actor a = await ActorAsync("m12-reject", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Do not create me"}""");

        await StartAsync(a, "ResearchCopilot", "Something.");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/approvals/{approval.Id}/decision",
            new DecideApprovalRequest(Approve: false, approval.Version, "Not warranted.")));

        ExecuteApprovedToolResponse executed = await PostAsync<ExecuteApprovedToolResponse>(
            a, $"ai/tool-requests/{approval.ToolRequestId}/execute", new { });

        Assert.False(executed.Succeeded);
        Assert.DoesNotContain(
            await GetListAsync<TaskResponse>(a, "tasks"), x => x.Title == "Do not create me");
    }

    /// <summary>Executing something nobody approved is refused.</summary>
    /// <remarks>
    /// The execution route exists to run an approved request. Reaching it directly
    /// with an identifier is the obvious attempt, and it produces the same refusal
    /// as a rejected one (§14).
    /// </remarks>
    [Fact]
    public async Task ExecutingWithoutADecision_IsRefused()
    {
        Actor a = await ActorAsync("m12-no-decision", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Unapproved"}""");

        await StartAsync(a, "ResearchCopilot", "Something.");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        ExecuteApprovedToolResponse executed = await PostAsync<ExecuteApprovedToolResponse>(
            a, $"ai/tool-requests/{approval.ToolRequestId}/execute", new { });

        Assert.False(executed.Succeeded);
        Assert.DoesNotContain(
            await GetListAsync<TaskResponse>(a, "tasks"), x => x.Title == "Unapproved");
    }

    /// <summary>
    /// Cancelling abandons what has not happened and keeps what has.
    /// </summary>
    /// <remarks>
    /// A cancelled run does not leave an approval somebody could grant afterwards,
    /// and it does not unmake a task that was already approved and created. The
    /// second half is the one people expect to be wrong (§45).
    /// </remarks>
    [Fact]
    public async Task Cancelling_AbandonsThePendingRequestAndKeepsWhatHappened()
    {
        Actor a = await ActorAsync("m12-cancel", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Abandoned proposal"}""");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Something.");

        AgentRunDetailResponse parked = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{started.Id}");

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/runs/{started.Id}/cancel",
            new CancelAgentRunRequest(parked.Run.Version)));

        AgentRunDetailResponse cancelled = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{started.Id}");

        Assert.Equal("Cancelled", cancelled.Run.Status);
        Assert.Equal("Abandoned", Assert.Single(cancelled.ToolRequests).Status);

        // The history survives the cancellation. What happened, happened.
        Assert.NotEmpty(cancelled.Steps);
    }

    // ------------------------------------------------------------ the policy

    /// <summary>
    /// An organization that has not enabled a provider transmits nothing.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is on the provider, not on the run. A run that
    /// failed for policy reasons after the prompt was already sent would be a
    /// disclosure with a tidy error message on top (§43).
    /// </remarks>
    [Fact]
    public async Task WithNoPolicy_NothingIsSent()
    {
        Actor a = await ActorAsync("m12-closed", AgencyRole.Owner);

        FakeModelProvider fake = Fake();
        fake.ScriptText("This should never be reached.");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Failed", run.Run.Status);
        Assert.Equal("PolicyRefused", run.Run.Failure);
        Assert.Empty(fake.Requests);
        Assert.Contains(run.Steps, x => x.Kind == "Refused");
    }

    /// <summary>
    /// No ceiling reaches Restricted.
    /// </summary>
    /// <remarks>
    /// Refused by the API, by the aggregate and by a CHECK constraint. All three
    /// are wanted: material an organization marked as never leaving has no setting
    /// at any level that permits transmitting it (§5, §43).
    /// </remarks>
    [Fact]
    public async Task RestrictedCannotBeSetAsACeiling()
    {
        Actor a = await ActorAsync("m12-restricted", AgencyRole.Owner);

        using HttpResponseMessage response = await a.Client.PutAsJsonAsync(
            $"{a.Root}/ai/policies/fake",
            new UpdateAiProviderPolicyRequest(true, "Restricted", false, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Turning proposals off removes the write tool from what the model is offered.
    /// </summary>
    /// <remarks>
    /// Filtered before the model is called rather than refused after it asks, so
    /// the model never learns the tool exists. A refusal after the fact would tell
    /// it what to ask for next time (§42).
    /// </remarks>
    [Fact]
    public async Task WithProposalsOff_TheWriteToolIsNotOffered()
    {
        Actor a = await ActorAsync("m12-no-proposals", AgencyRole.Owner);
        await EnableProviderAsync(a, allowWrites: false);

        IReadOnlyList<AiToolDescriptorResponse> offered =
            await GetListAsync<AiToolDescriptorResponse>(a, "ai/agents/ResearchCopilot/tools");

        Assert.NotEmpty(offered);
        Assert.DoesNotContain(offered, x => x.Effect == "CanonicalWrite");

        FakeModelProvider fake = Fake();
        fake.ScriptText("Nothing to propose.");

        await StartAsync(a, "ResearchCopilot", "Anything.");

        Assert.DoesNotContain(
            Assert.Single(fake.Requests).Tools, x => x.Name == "task.create");
    }

    /// <summary>The policy screen never reports whether a credential is configured.</summary>
    /// <remarks>
    /// Whether the server holds a key is deployment infrastructure. Reporting it on
    /// a tenant screen would describe the deployment to anybody who can open one
    /// (§3).
    /// </remarks>
    [Fact]
    public async Task ThePolicyNeverReportsCredentialState()
    {
        Actor a = await ActorAsync("m12-credential", AgencyRole.Owner);
        await EnableProviderAsync(a);

        AiProviderPolicyResponse policy = Assert.Single(
            await GetListAsync<AiProviderPolicyResponse>(a, "ai/policies"),
            x => x.ProviderKey == "fake");

        Assert.False(policy.IsProviderConfigured);
    }

    // ----------------------------------------------------------- the failure

    /// <summary>
    /// A provider failure is a category, not a stack trace.
    /// </summary>
    /// <remarks>
    /// Whether to retry, ask an administrator or narrow the question are different
    /// answers, and the failure kind is what distinguishes them for a user who
    /// cannot read a log (§68).
    /// </remarks>
    [Fact]
    public async Task AProviderFailure_IsRecordedAsAKind()
    {
        Actor a = await ActorAsync("m12-failure", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptFailure(Domain.Ai.AgentFailureKind.ProviderTimeout, "took too long");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Failed", run.Run.Status);
        Assert.Equal("ProviderTimeout", run.Run.Failure);
        Assert.NotNull(run.Run.CompletedAt);
    }

    /// <summary>
    /// A model asking for a tool it does not have is told no and carries on.
    /// </summary>
    /// <remarks>
    /// Not an error and not a crash. The registry is a closed list, and a request
    /// outside it is a message back to the model — which is exactly what has to
    /// happen when a document has talked the model into asking for something
    /// (§32, §49).
    /// </remarks>
    [Fact]
    public async Task AnUnregisteredTool_IsRefusedWithoutFailingTheRun()
    {
        Actor a = await ActorAsync("m12-unknown-tool", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake()
            .ScriptToolCall("sql.execute", """{"query":"SELECT * FROM users"}""")
            .ScriptText("I could not do that, so here is what I have.");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Completed", run.Run.Status);
        Assert.Contains(
            run.Steps,
            x => x.Kind == "Refused" && x.Summary.Contains("sql.execute", StringComparison.Ordinal));

        // No request row was written for something that does not exist.
        Assert.Empty(run.ToolRequests);
    }

    /// <summary>Malformed arguments are refused rather than guessed at.</summary>
    [Fact]
    public async Task MalformedArguments_AreRefused()
    {
        Actor a = await ActorAsync("m12-malformed", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake()
            .ScriptToolCall("task.create", "not json at all")
            .ScriptText("Understood.");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");
        AgentRunDetailResponse run = await GetAsync<AgentRunDetailResponse>(a, $"ai/runs/{started.Id}");

        Assert.Equal("Completed", run.Run.Status);
        Assert.Contains(run.Steps, x => x.Kind == "Refused");
        Assert.Empty(run.ToolRequests);
    }

    // -------------------------------------------------------------- the audit

    /// <summary>
    /// Every consequential act names the person, never the model.
    /// </summary>
    /// <remarks>
    /// The actor on an AI-proposed write is the human who approved it. A model is
    /// not an actor and cannot be one: it holds no permissions and cannot be held
    /// to account (§9, §46).
    /// </remarks>
    [Fact]
    public async Task TheAuditNamesThePersonWhoApproved()
    {
        Actor a = await ActorAsync("m12-audit", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Audited proposal"}""");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/approvals/{approval.Id}/decision",
            new DecideApprovalRequest(Approve: true, approval.Version)));

        await PostAsync<ExecuteApprovedToolResponse>(
            a, $"ai/tool-requests/{approval.ToolRequestId}/execute", new { });

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        string run = started.Id.ToString();
        string approvalId = approval.Id.ToString();
        string requestId = approval.ToolRequestId.ToString();

        List<Domain.Audit.AuditEvent> events = await context.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == run || x.EntityId == approvalId || x.EntityId == requestId)
            .ToListAsync();

        Assert.Contains(events, x => x.Action == AuditAction.AgentRunStarted);
        Assert.Contains(events, x => x.Action == AuditAction.AiApprovalGranted);
        Assert.Contains(events, x => x.Action == AuditAction.AiProposedWriteExecuted);

        // A person on every one of them. A model is not an actor: it holds no
        // permissions and cannot be held to account.
        Assert.All(events, x => Assert.NotNull(x.ActorUserId));
    }

    // ------------------------------------------------------------ the schema

    /// <summary>
    /// The arguments an approval covers cannot change under it.
    /// </summary>
    /// <remarks>
    /// Enforced by a trigger, so it holds for any code path rather than for the one
    /// the handler takes. Approving one action and executing another is exactly
    /// what the fingerprint prevents, and this is the layer nothing goes around
    /// (§13, §48).
    /// </remarks>
    [Fact]
    public async Task TheDatabaseRefusesToRewriteAProposedAction()
    {
        Actor a = await ActorAsync("m12-immutable-args", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Original"}""");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_tool_requests SET arguments = @arguments WHERE id = @id",
                ("arguments", """{"title":"Something else entirely"}"""),
                ("id", approval.ToolRequestId)));

        Assert.Contains("cannot change", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A decided approval cannot be re-decided, by any path.</summary>
    [Fact]
    public async Task TheDatabaseRefusesToRedecideAnApproval()
    {
        Actor a = await ActorAsync("m12-immutable-decision", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Decided once"}""");

        await StartAsync(a, "ResearchCopilot", "Anything.");

        AiApprovalResponse approval = Assert.Single(
            await GetListAsync<AiApprovalResponse>(a, "ai/approvals"));

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/ai/approvals/{approval.Id}/decision",
            new DecideApprovalRequest(Approve: false, approval.Version, "No.")));

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_approvals SET decision = 2 WHERE id = @id",
                ("id", approval.Id)));

        Assert.Contains("already decided", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A run's history is what happened. It is not edited.</summary>
    [Fact]
    public async Task TheDatabaseRefusesToRewriteHistory()
    {
        Actor a = await ActorAsync("m12-immutable-steps", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptText("A brief.");

        AgentRunIdResponse started = await StartAsync(a, "ResearchCopilot", "Anything.");

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_run_steps SET summary = 'Something else' WHERE ai_run_id = @id",
                ("id", started.Id)));

        Assert.Contains("cannot be edited", failure.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No external-effect request can exist, even written directly.
    /// </summary>
    /// <remarks>
    /// The aggregate refuses it and so does a CHECK constraint. Sending is a
    /// canonical workflow a person carries out, and the schema says so rather than
    /// trusting that no future code path will register such a tool (§12, §15).
    /// </remarks>
    [Fact]
    public async Task TheDatabaseRefusesAnExternalEffectRequest()
    {
        Actor a = await ActorAsync("m12-external", AgencyRole.Owner);
        await EnableProviderAsync(a);

        Fake().ScriptToolCall("task.create", """{"title":"Effect check"}""");

        AiApprovalResponse approval = await ProposeAsync(a, "Anything.");

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                "UPDATE ai_tool_requests SET effect = 3 WHERE id = @id",
                ("id", approval.ToolRequestId)));

        Assert.Contains("ck_ai_tool_requests_effect", failure.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- helpers

    private FakeModelProvider Fake()
    {
        FakeModelProvider provider =
            _fixture.Factory.Services.GetRequiredService<FakeModelProvider>();

        provider.Reset();

        return provider;
    }

    private async Task<AiApprovalResponse> ProposeAsync(Actor actor, string task)
    {
        await StartAsync(actor, "ResearchCopilot", task);

        return Assert.Single(await GetListAsync<AiApprovalResponse>(actor, "ai/approvals"));
    }

    private static async Task<AgentRunIdResponse> StartAsync(
        Actor actor, string kind, string task) =>
        await PostAsync<AgentRunIdResponse>(
            actor, "ai/runs", new StartAgentRunRequest(kind, task));

    private static async Task EnableProviderAsync(Actor actor, bool allowWrites = true)
    {
        using HttpResponseMessage response = await actor.Client.PutAsJsonAsync(
            $"{actor.Root}/ai/policies/fake",
            new UpdateAiProviderPolicyRequest(
                true, "Confidential", allowWrites, ExpectedVersion: 0));

        await EnsureAsync(response);
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

    private static async Task<IReadOnlyList<T>> GetListAsync<T>(Actor actor, string route) =>
        await GetAsync<IReadOnlyList<T>>(actor, route);

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
