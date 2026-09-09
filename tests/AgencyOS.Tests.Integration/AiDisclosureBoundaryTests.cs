using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Ai;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The ways an AI answer could hand somebody more than they may read.
/// </summary>
/// <remarks>
/// <para>
/// M13 gave AgencyOS three new routes to an AI result that do not go through the
/// screen the reader was looking at: a citation they can click, a toast they can
/// act on, and a deep link that opens the application at an object. Each is a
/// pointer produced while somebody was authorized, and each can be followed later
/// (§L, §AA, ADR-0035).
/// </para>
/// <para>
/// The property under test is that none of them carries authority. A pointer says
/// where to look; whether this reader may look is decided again, on the server, at
/// the moment they ask. The failures would all be silent — a citation that opened
/// a signal the reader had lost access to would look exactly like one that worked.
/// </para>
/// <para>
/// The alternate-path tests are here for the same reason. Withholding a result
/// from the detail route means nothing if the list route carries it or the trace
/// quotes it, so those are asserted rather than assumed.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AiDisclosureBoundaryTests
{
    private readonly AgencyOsTestFixture _fixture;

    public AiDisclosureBoundaryTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A citation names an object. It does not open one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brief below really does cite the signal — the citation validator only
    /// leaves references the run was actually given, so its presence in the stored
    /// text proves this is a live citation and not a string the test wrote.
    /// </para>
    /// <para>
    /// Following it is an ordinary read of an ordinary object, authorized when it
    /// happens. A colleague who was never on the run cannot open it, and neither
    /// can the person who commissioned the brief once the grant behind that
    /// material is gone. Otherwise a brief would be a durable, transferable copy of
    /// its author's permissions.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACitationDoesNotOpenWhatTheReaderMayNotSee()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-cite");
        Actor mine = Actor.For(_fixture, owner);
        Actor colleague = await SameTenantAsync(owner, AgencyRole.Member, "m13-cite-other");

        await EnableAsync(mine);

        PersonDetailResponse person = await CreatePersonAsync(mine, "Marguerite Sable");

        IntelligenceIdResponse source = await PostAsync<IntelligenceIdResponse>(
            mine,
            "intelligence/sources",
            new RecordSourceRequest(
                "ManualObservation",
                "Told over dinner",
                "SourceSensitive",
                Publisher: "A person who was there"));

        IntelligenceIdResponse signal = await PostAsync<IntelligenceIdResponse>(
            mine,
            "intelligence/signals",
            new RecordSignalRequest(
                "Sable leaving Northgate",
                "Marguerite Sable is expected to leave Northgate before the autumn.",
                "PersonnelMove",
                "SourceSensitive",
                [new SignalEvidenceRequest(source.Id, "Primary", "…is expected to depart…")],
                [new IntelligenceSubjectRequest("Person", person.Person.Id, "The subject")]));

        Fake().ScriptText(
            $"She is expected to move before the autumn [cite:Signal:{signal.Id:D}].");

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            mine,
            "ai/runs",
            new StartAgentRunRequest(
                "RelationshipBrief", "Brief me before I call her.", "Person", person.Person.Id));

        AgentRunDetailResponse brief = await GetAsync<AgentRunDetailResponse>(
            mine, $"ai/runs/{run.Id}");

        // The citation survived validation, so it points at something the run held.
        Assert.NotNull(brief.Result);
        Assert.Contains($"[cite:Signal:{signal.Id:D}]", brief.Result, StringComparison.Ordinal);

        // A colleague holding the identifier cannot open it.
        using HttpResponseMessage theirs =
            await colleague.Client.GetAsync($"{colleague.Root}/intelligence/signals/{signal.Id}");

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);

        // Neither can the person the brief was written for, once the grant is gone.
        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        using HttpResponseMessage after =
            await mine.Client.GetAsync($"{mine.Root}/intelligence/signals/{signal.Id}");

        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    /// <summary>
    /// A notification that has been overtaken restores nothing.
    /// </summary>
    /// <remarks>
    /// A toast is written at one moment and acted on at another, and the gap can
    /// hold anything — including the withdrawal of the permission that justified
    /// sending it. Acting on it is a fresh request, so it is answered by what is
    /// true when it arrives rather than by what was true when the toast was
    /// written (§8, §AA).
    /// </remarks>
    [Fact]
    public async Task AStaleNotificationRestoresNothing()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-stale");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "Who is leaving Northgate?",
                "SourceSensitive",
                Context: "Rousseau said so over dinner, in confidence."));

        Fake().ScriptText("Somebody close to the company expects a departure.");

        // What the notification would carry: the identifier of a finished run.
        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize this case.", "ResearchCase", research.Id));

        AgentRunDetailResponse arrived = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.False(arrived.ResultWithheld);

        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        // The same identifier, followed later, the way opening the toast would.
        AgentRunDetailResponse opened = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.True(opened.ResultWithheld);
        Assert.Null(opened.Result);
    }

    /// <summary>
    /// The trace does not quote what the result withheld.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run's steps stay readable after a result is withheld, which is
    /// deliberate: the reader asked the question, and a run that vanished would
    /// look like data loss. It only works because the steps record what AgencyOS
    /// did rather than what the model said — "Assembled 4 context blocks", "Called
    /// windows-local" — so there is nothing in them to leak.
    /// </para>
    /// <para>
    /// That is a property of every step written today, and this test is what stops
    /// a future step from quoting the answer into a surface that is not
    /// re-authorized.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheTraceDoesNotRestoreAWithheldResult()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-trace");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "What did Rousseau say?",
                "SourceSensitive",
                Context: "Said in confidence over dinner."));

        const string Answer = "Rousseau expects Sable to leave before the autumn.";

        Fake().ScriptText(Answer);

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize this case.", "ResearchCase", research.Id));

        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        AgentRunDetailResponse later = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.True(later.ResultWithheld);
        Assert.Null(later.Result);

        // The trace is still there, and says nothing the result was withholding.
        Assert.NotEmpty(later.Steps);

        foreach (AgentStepResponse step in later.Steps)
        {
            Assert.DoesNotContain(Answer, step.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(Answer, step.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("Rousseau", step.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("Rousseau", step.Summary, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The list of runs carries no result to withhold.
    /// </summary>
    /// <remarks>
    /// The alternate path that would make the whole re-authorization pointless. The
    /// summary a list returns is deliberately narrower than the detail, so there is
    /// no second copy of the answer a reader could reach without passing the check
    /// — and this asserts that rather than trusting the shape to stay narrow.
    /// </remarks>
    [Fact]
    public async Task TheRunListNeverCarriesAResult()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-list");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "Anything at all.", "SourceSensitive", Context: "In confidence."));

        const string Answer = "A sentence that must not appear in a listing.";

        Fake().ScriptText(Answer);

        await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize.", "ResearchCase", research.Id));

        using HttpResponseMessage listed = await a.Client.GetAsync($"{a.Root}/ai/runs");

        await EnsureAsync(listed);

        string body = await listed.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Answer, body, StringComparison.Ordinal);
    }

    /// <summary>An approval is not readable from another tenant.</summary>
    /// <remarks>
    /// The deep-link case at its widest. An approval identifier is a bare GUID in a
    /// URI that another organization's user can paste; the route is tenant-scoped
    /// and answers uniformly, so pasting it proves only that the identifier is
    /// well-formed.
    /// </remarks>
    [Fact]
    public async Task AnApprovalIsNotReadableFromAnotherTenant()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-approval-mine");
        Actor mine = Actor.For(_fixture, owner);

        SeededActor stranger = await _fixture.SeedActorAsync(
            AgencyRole.Owner, "m13-approval-theirs");
        Actor theirs = Actor.For(_fixture, stranger);

        await EnableAsync(mine);
        Fake().ScriptToolCall("task.create", @"{""title"":""A proposal from another tenant""}");

        await PostAsync<AgentRunIdResponse>(
            mine,
            "ai/runs",
            new StartAgentRunRequest("ResearchCopilot", "Chase this."));

        AiApprovalResponse approval = Assert.Single(
            await GetAsync<IReadOnlyList<AiApprovalResponse>>(mine, "ai/approvals"));

        using HttpResponseMessage read =
            await theirs.Client.GetAsync($"{theirs.Root}/ai/approvals/{approval.Id}");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        using HttpResponseMessage decided = await theirs.Client.PostAsJsonAsync(
            $"{theirs.Root}/ai/approvals/{approval.Id}/decision",
            new DecideApprovalRequest(Approve: true, approval.Version));

        Assert.Equal(HttpStatusCode.NotFound, decided.StatusCode);
    }

    // ------------------------------------------------------------- helpers

    private FakeModelProvider Fake()
    {
        FakeModelProvider provider = _fixture.Factory.Services
            .GetRequiredService<FakeModelProvider>();

        provider.Reset();

        return provider;
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

        return Actor.For(_fixture, new SeededActor(subject, user, tenant.Organization));
    }

    private static async Task EnableAsync(Actor actor)
    {
        using HttpResponseMessage response = await actor.Client.PutAsJsonAsync(
            $"{actor.Root}/ai/policies/fake",
            new UpdateAiProviderPolicyRequest(true, "Protected", true, ExpectedVersion: 0));

        await EnsureAsync(response);
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(Actor actor, string name)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/people", new CreatePersonRequest(name));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private sealed record Actor(HttpClient Client, string Root)
    {
        public static Actor For(AgencyOsTestFixture fixture, SeededActor actor) =>
            new(
                fixture.CreateClient(actor.Subject),
                $"/api/v1/organizations/{actor.Organization.Id.Value}");
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
