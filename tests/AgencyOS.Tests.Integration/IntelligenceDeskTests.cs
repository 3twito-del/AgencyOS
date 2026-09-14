using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The Intelligence desk, called rather than documented.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001 found that <c>GET /intelligence/command-center</c> answered 500 on
/// every tenant, including an empty one, so the module's landing tab had never
/// opened successfully. It survived 751 integration tests because the route
/// appeared in the suite exactly once — as an <c>[InlineData]</c> string in
/// <c>OpenApiContractTests</c>, asserting the path is described in the published
/// contract (<c>AOS-R001-002</c>, <c>AOS-R001-014</c>).
/// </para>
/// <para>
/// A route being present in OpenAPI is not coverage. It says the document mentions
/// it, which is true of a route that throws on every request. Every test here
/// issues the request.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class IntelligenceDeskTests
{
    private readonly AgencyOsTestFixture _fixture;

    public IntelligenceDeskTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A. An empty tenant.
    /// </summary>
    /// <remarks>
    /// The minimal reproduction of <c>AOS-R001-002</c>. No research case, no task,
    /// no signal, no prediction — and before the repair this answered 500, which is
    /// what made the defect unconditional rather than data-dependent.
    /// </remarks>
    [Fact]
    public async Task TheDeskOpensOnAnEmptyTenant()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-empty");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.Empty(desk.ResearchCasesWithOverdueTasks);
        Assert.Empty(desk.PredictionsAwaitingResolution);
        Assert.Equal(0, desk.AwaitingResolutionCount);
    }

    /// <summary>
    /// B. A research case with no task attached.
    /// </summary>
    /// <remarks>
    /// The panel asks which open cases have overdue work. A case with no work at
    /// all is not overdue, and must not appear merely because it exists.
    /// </remarks>
    [Fact]
    public async Task ACaseWithNoTaskIsNotOverdue()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-notask");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        await OpenCaseAsync(client, actor, "Who is buying limited series this quarter?");

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.Empty(desk.ResearchCasesWithOverdueTasks);
    }

    /// <summary>
    /// C. A research case whose attached task is overdue.
    /// </summary>
    /// <remarks>
    /// The positive case, and the one that proves the repaired subquery still
    /// selects. A fix that made the expression translate by never matching anything
    /// would pass A and B and fail here.
    /// </remarks>
    [Fact]
    public async Task ACaseWithAnOverdueTaskAppears()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-overdue");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Guid caseId = await OpenCaseAsync(client, actor, "Which buyers moved on animation?");
        Guid task = await CreateTaskAsync(client, actor, "Chase the buyer list", DateTimeOffset.UtcNow.AddDays(-3));

        await LinkTaskAsync(client, actor, caseId, task);

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.Contains(desk.ResearchCasesWithOverdueTasks, x => x.Id == caseId);
    }

    /// <summary>
    /// A task that is attached and not yet due does not make a case overdue.
    /// </summary>
    /// <remarks>
    /// The other half of C. The repaired subquery carries the due-date predicate;
    /// dropping it would light up every case with any open work.
    /// </remarks>
    [Fact]
    public async Task ACaseWhoseTaskIsNotYetDueDoesNotAppear()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-future");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Guid caseId = await OpenCaseAsync(client, actor, "What is the streamer doing in Q4?");
        Guid task = await CreateTaskAsync(client, actor, "Read the trades", DateTimeOffset.UtcNow.AddDays(14));

        await LinkTaskAsync(client, actor, caseId, task);

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.DoesNotContain(desk.ResearchCasesWithOverdueTasks, x => x.Id == caseId);
    }

    /// <summary>
    /// D. Several tasks on one case, and several cases.
    /// </summary>
    /// <remarks>
    /// A case with three attached tasks is listed once, not three times: the
    /// predicate is an existence test, and a repair that turned it into a join
    /// would duplicate the row.
    /// </remarks>
    [Fact]
    public async Task ACaseWithSeveralOverdueTasksIsListedOnce()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-many");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Guid caseId = await OpenCaseAsync(client, actor, "Who is attached to the adaptation?");

        for (int index = 0; index < 3; index++)
        {
            Guid task = await CreateTaskAsync(
                client, actor, $"Overdue step {index}", DateTimeOffset.UtcNow.AddDays(-index - 1));

            await LinkTaskAsync(client, actor, caseId, task);
        }

        Guid quiet = await OpenCaseAsync(client, actor, "A case with nothing outstanding");

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.Equal(1, desk.ResearchCasesWithOverdueTasks.Count(x => x.Id == caseId));
        Assert.DoesNotContain(desk.ResearchCasesWithOverdueTasks, x => x.Id == quiet);
    }

    /// <summary>
    /// E. A sensitive case is withheld from somebody without the grant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repair moved the task predicate and left the sensitivity narrowing
    /// alone. This asserts that: an owner sees the source-sensitive case on the
    /// desk, a member without <c>intelligence.sensitive.read</c> does not, and the
    /// desk still answers for them rather than refusing.
    /// </para>
    /// <para>
    /// List narrowing, not existence disclosure. ADR-0038 governs what a refusal
    /// may admit about a single record; a panel that omits what the caller may not
    /// read is the narrowing the panel has always done.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASensitiveCaseIsOnTheOwnersDeskAndNotOnAMembers()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-sensitive");
        using HttpClient ownerClient = _fixture.CreateClient(owner.Subject);

        Guid caseId = await OpenCaseAsync(
            ownerClient, owner, "Who told us about the departure?", sensitivity: "SourceSensitive");

        Guid task = await CreateTaskAsync(
            ownerClient, owner, "Confirm with a second source", DateTimeOffset.UtcNow.AddDays(-2));

        await LinkTaskAsync(ownerClient, owner, caseId, task);

        Assert.Contains(
            (await DeskAsync(ownerClient, owner)).ResearchCasesWithOverdueTasks,
            x => x.Id == caseId);

        SeededActor member = await _fixture.SeedActorAsync(AgencyRole.Member, "desk-plain");

        await _fixture.SeedMembershipAsync(
            owner.Organization.Id, member.User.Id, AgencyRole.Member, owner.User.Id);

        using HttpClient memberClient = _fixture.CreateClient(member.Subject);

        IntelligenceCommandCenterResponse theirDesk = await DeskAsync(memberClient, owner);

        Assert.DoesNotContain(theirDesk.ResearchCasesWithOverdueTasks, x => x.Id == caseId);
    }

    /// <summary>
    /// F. The desk is refused to somebody outside the tenant.
    /// </summary>
    /// <remarks>
    /// The repaired subquery filters tasks by organization, which the previous
    /// shape did not. This asserts the surface as a whole still refuses a stranger.
    /// </remarks>
    [Fact]
    public async Task AnotherTenantsDeskIsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-tenant");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-outsider");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/intelligence/command-center");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"A stranger reading another tenant's desk got {(int)response.StatusCode}.");
    }

    /// <summary>
    /// A task belonging to another tenant cannot be attached in the first place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repair added a tenant filter to the task lookup, and the honest way to
    /// test it turned out to be this rather than the scenario it was written for.
    /// Attaching a foreign task is refused at the command, with 404, so a link that
    /// crosses a tenant cannot exist for the query to find.
    /// </para>
    /// <para>
    /// The filter stays: it makes the guarantee local to the query rather than
    /// something inherited from whoever wrote the link command, and 404 is the
    /// answer ADR-0038 requires — another tenant's identifier is indistinguishable
    /// from one that names nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ATaskFromAnotherTenantCannotBeAttachedToACase()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-cross");
        SeededActor other = await _fixture.SeedActorAsync(AgencyRole.Owner, "desk-crossother");

        using HttpClient client = _fixture.CreateClient(actor.Subject);
        using HttpClient otherClient = _fixture.CreateClient(other.Subject);

        Guid caseId = await OpenCaseAsync(client, actor, "A case in our tenant");
        Guid theirTask = await CreateTaskAsync(
            otherClient, other, "Their overdue work", DateTimeOffset.UtcNow.AddDays(-5));

        ResearchCaseDetailResponse current = (await client.GetFromJsonAsync<ResearchCaseDetailResponse>(
            $"{Root(actor)}/intelligence/research-cases/{caseId}"))!;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Root(actor)}/intelligence/research-cases/{caseId}/links",
            new LinkResearchItemRequest("Task", theirTask, current.ResearchCase.Version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        IntelligenceCommandCenterResponse desk = await DeskAsync(client, actor);

        Assert.DoesNotContain(desk.ResearchCasesWithOverdueTasks, x => x.Id == caseId);
    }

    // ------------------------------------------------------------- helpers

    private static string Root(SeededActor actor) =>
        $"/api/v1/organizations/{actor.Organization.Id.Value}";

    private static async Task<IntelligenceCommandCenterResponse> DeskAsync(
        HttpClient client,
        SeededActor actor)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(actor)}/intelligence/command-center");

        Assert.True(
            response.IsSuccessStatusCode,
            $"The intelligence desk answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IntelligenceCommandCenterResponse>())!;
    }

    private static async Task<Guid> OpenCaseAsync(
        HttpClient client,
        SeededActor actor,
        string question,
        string sensitivity = "Internal")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Root(actor)}/intelligence/research-cases",
            new OpenResearchCaseRequest(question, sensitivity, actor.User.Id.Value));

        Assert.True(
            response.IsSuccessStatusCode,
            $"Opening a research case answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IntelligenceIdResponse>())!.Id;
    }

    private static async Task<Guid> CreateTaskAsync(
        HttpClient client,
        SeededActor actor,
        string title,
        DateTimeOffset dueAt)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Root(actor)}/tasks",
            new CreateTaskRequest(title, dueAt));

        Assert.True(
            response.IsSuccessStatusCode,
            $"Creating a task answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task LinkTaskAsync(
        HttpClient client,
        SeededActor actor,
        Guid researchCaseId,
        Guid taskId)
    {
        ResearchCaseDetailResponse current = (await client.GetFromJsonAsync<ResearchCaseDetailResponse>(
            $"{Root(actor)}/intelligence/research-cases/{researchCaseId}"))!;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Root(actor)}/intelligence/research-cases/{researchCaseId}/links",
            new LinkResearchItemRequest("Task", taskId, current.ResearchCase.Version));

        Assert.True(
            response.IsSuccessStatusCode,
            $"Attaching a task answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
    }

    private sealed record CreatedId(Guid Id);
}
