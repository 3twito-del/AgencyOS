using System.Net.Http.Json;
using AgencyOS.Contracts.Ai;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Ai;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// What happens to a stored AI result when the permission behind it goes away.
/// </summary>
/// <remarks>
/// <para>
/// The question M13 had to answer explicitly. A result is a derived work of
/// everything the run was given, so a brief drawn from a source-sensitive signal
/// carries that signal's confidence even though nothing in it looks like one.
/// Storing it and reading it back under "you started this run" would make
/// historical authorization permanent (§L, ADR-0035).
/// </para>
/// <para>
/// The honest limit is stated in the ADR and holds here too: this governs what
/// AgencyOS will show from now on. It does not reach a copy somebody already read,
/// pasted or remembered, and nothing in these tests claims otherwise.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class AiResultRevocationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public AiResultRevocationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A result drawn from sensitive material is withheld once the grant is gone.
    /// </summary>
    /// <remarks>
    /// The run stays visible and says the result was withheld. Hiding the run
    /// would tell the reader less than the truth — they asked this question — and
    /// a run that vanished would look like data loss.
    /// </remarks>
    [Fact]
    public async Task ASensitiveResultIsWithheldAfterTheGrantIsRevoked()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-revoke");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        // A research case classified source-sensitive: the strongest thing the
        // brief can be drawn from.
        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "Who is leaving Northgate?",
                "SourceSensitive",
                Context: "Rousseau said so over dinner, in confidence."));

        Fake().ScriptText("Somebody close to the company expects a departure.");

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize this case.", "ResearchCase", research.Id));

        AgentRunDetailResponse generated = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        // While authorized: the result reads.
        Assert.Equal("Completed", generated.Run.Status);
        Assert.False(generated.ResultWithheld);
        Assert.Equal(
            "Somebody close to the company expects a departure.", generated.Result);

        // The sensitive grant goes away. The person keeps ai.use and keeps the run.
        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        AgentRunDetailResponse later = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.True(later.ResultWithheld);
        Assert.Null(later.Result);

        // The run itself is still theirs to see, and still says what happened.
        Assert.Equal("Completed", later.Run.Status);
        Assert.NotEmpty(later.Steps);
    }

    /// <summary>
    /// An ordinary result is unaffected.
    /// </summary>
    /// <remarks>
    /// The re-authorization is against the material, not against AI in general.
    /// Withholding every result on a role change would make the feature useless
    /// and would teach people to distrust the run history.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryResultSurvivesARoleChange()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-ordinary");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "What is public about Northgate?",
                "Internal",
                Context: "Everything here came from the trade press."));

        Fake().ScriptText("Northgate restructured its drama slate in August.");

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize this case.", "ResearchCase", research.Id));

        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        AgentRunDetailResponse later = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.False(later.ResultWithheld);
        Assert.Equal("Northgate restructured its drama slate in August.", later.Result);
    }

    /// <summary>
    /// The classification is recorded at generation time, not recomputed on read.
    /// </summary>
    /// <remarks>
    /// Recomputing would ask "what would this material be classified as now",
    /// which is a different question and answerable only by reassembling context
    /// the reader may no longer be allowed to see. The recorded value is what the
    /// result was actually drawn from.
    /// </remarks>
    [Fact]
    public async Task TheClassificationIsRecordedNotRecomputed()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m13-recorded");
        Actor a = Actor.For(_fixture, owner);

        await EnableAsync(a);

        IntelligenceIdResponse research = await PostAsync<IntelligenceIdResponse>(
            a,
            "intelligence/research-cases",
            new OpenResearchCaseRequest(
                "Sensitive at the time.",
                "SourceSensitive",
                Context: "Told in confidence."));

        Fake().ScriptText("A brief.");

        AgentRunIdResponse run = await PostAsync<AgentRunIdResponse>(
            a,
            "ai/runs",
            new StartAgentRunRequest(
                "ResearchCopilot", "Summarize.", "ResearchCase", research.Id));

        // The case is reclassified downward afterwards. The result keeps the
        // classification it was produced under: it was drawn from what the case
        // said when it was sensitive.
        IntelligenceIdResponse reclassified = research;

        await _fixture.ChangeRoleAsync(
            owner.Organization.Id, owner.User.Id, AgencyRole.Member, owner.User.Id);

        AgentRunDetailResponse later = await GetAsync<AgentRunDetailResponse>(
            a, $"ai/runs/{run.Id}");

        Assert.True(later.ResultWithheld);
        Assert.Equal(research.Id, reclassified.Id);
    }

    // ------------------------------------------------------------- helpers

    private FakeModelProvider Fake()
    {
        FakeModelProvider provider = _fixture.Factory.Services
            .GetRequiredService<FakeModelProvider>();

        provider.Reset();

        return provider;
    }

    private static async Task EnableAsync(Actor actor)
    {
        using HttpResponseMessage response = await actor.Client.PutAsJsonAsync(
            $"{actor.Root}/ai/policies/fake",
            new UpdateAiProviderPolicyRequest(true, "Protected", true, ExpectedVersion: 0));

        await EnsureAsync(response);
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
