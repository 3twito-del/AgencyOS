using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M2 workflow end to end: person, company, relationship, interaction,
/// follow-up task, Command Center and timeline.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class PeopleSliceTests
{
    private readonly AgencyOsTestFixture _fixture;

    public PeopleSliceTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole slice, in the order a user performs it. Written as one test on
    /// purpose: the value of M2 is that these steps connect, and separate tests
    /// would each pass while the join between them stayed broken.
    /// </summary>
    [Fact]
    public async Task Workflow_FromPersonToCommandCenter()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "workflow");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        // 1. A company the agency deals with.
        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest("Vertex Studios", "Studio", "Vertex Studios LLC")));

        Assert.Equal("Vertex Studios", studio.Company.Name);

        // 2. A person.
        PersonDetailResponse sarah = await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest("Sarah", "Okonkwo", Title: "Literary Agent", Email: "sarah@example.invalid")));

        Assert.Equal("Sarah Okonkwo", sarah.Person.DisplayName);

        // 3. Connect them.
        using HttpResponseMessage relationshipResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/relationships",
            new CreateRelationshipRequest(
                new PartyRefRequest("Person", sarah.Person.Id),
                new PartyRefRequest("Company", studio.Company.Id),
                "Employment",
                StartedAt: DateTimeOffset.UtcNow.AddYears(-2)));

        Assert.Equal(HttpStatusCode.Created, relationshipResponse.StatusCode);

        PersonDetailResponse connected = await client
            .GetFromJsonAsync<PersonDetailResponse>($"/api/v1/organizations/{tenant}/people/{sarah.Person.Id}")
            ?? throw new InvalidOperationException("Person disappeared.");

        Assert.Single(connected.Relationships);
        Assert.Equal("Employment", connected.Relationships[0].Type);
        Assert.Equal("Vertex Studios", connected.Relationships[0].To.Name);

        // 4. Record what happened, and the next move, in one command.
        RecordInteractionResponse recorded = await CreatedAsync<RecordInteractionResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Meeting",
                DateTimeOffset.UtcNow.AddHours(-2),
                "Met Sarah at dinner",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", sarah.Person.Id), "guest")],
                "She is interested in the thriller.",
                new FollowUpTaskRequest(
                    "Send Sarah the screenplay Monday",
                    DateTimeOffset.UtcNow.AddDays(3),
                    "High"))));

        Assert.NotEqual(Guid.Empty, recorded.InteractionId);
        Assert.NotNull(recorded.FollowUpTaskId);

        // 5. It shows up on the Command Center, backed by real queries.
        CommandCenterResponse centre = await client
            .GetFromJsonAsync<CommandCenterResponse>($"/api/v1/organizations/{tenant}/command-center")
            ?? throw new InvalidOperationException("No Command Center.");

        Assert.Contains(
            centre.DueSoon.Concat(centre.Overdue).Concat(centre.Unscheduled),
            task => task.Id == recorded.FollowUpTaskId);

        Assert.Contains(centre.RecentInteractions, i => i.Id == recorded.InteractionId);
        Assert.True(centre.OpenTaskCount >= 1);

        // 6. And on her timeline, alongside the relationship.
        TimelineEntryResponse[] timeline = await client
            .GetFromJsonAsync<TimelineEntryResponse[]>(
                $"/api/v1/organizations/{tenant}/people/{sarah.Person.Id}/timeline")
            ?? [];

        Assert.Contains(timeline, e => e.Kind == "interaction" && e.Title == "Met Sarah at dinner");
        Assert.Contains(timeline, e => e.Kind == "relationship.created");
        Assert.Contains(timeline, e => e.Kind == "task.created");

        // Newest first.
        Assert.Equal(
            timeline.OrderByDescending(e => e.OccurredAt).Select(e => e.OccurredAt),
            timeline.Select(e => e.OccurredAt));

        // 7. Complete it, and the Command Center agrees.
        using (HttpResponseMessage completed = await client.PostAsync(
            $"/api/v1/organizations/{tenant}/tasks/{recorded.FollowUpTaskId}/complete",
            content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        }

        CommandCenterResponse after = await client
            .GetFromJsonAsync<CommandCenterResponse>($"/api/v1/organizations/{tenant}/command-center")
            ?? throw new InvalidOperationException("No Command Center.");

        Assert.DoesNotContain(
            after.DueSoon.Concat(after.Overdue).Concat(after.Unscheduled),
            task => task.Id == recorded.FollowUpTaskId);
    }

    /// <summary>
    /// An interaction without a follow-up creates no task. "Call with studio
    /// executive" is a complete thought on its own.
    /// </summary>
    [Fact]
    public async Task RecordInteraction_WithoutFollowUp_CreatesNoTask()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "nofollowup");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Marcus");

        RecordInteractionResponse recorded = await CreatedAsync<RecordInteractionResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Call",
                DateTimeOffset.UtcNow,
                "Call with studio executive",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", person.Person.Id))])));

        Assert.Null(recorded.FollowUpTaskId);

        TaskResponse[] tasks = await client
            .GetFromJsonAsync<TaskResponse[]>($"/api/v1/organizations/{tenant}/tasks") ?? [];

        Assert.DoesNotContain(tasks, t => t.SourceInteractionId == recorded.InteractionId);
    }

    /// <summary>
    /// A refused interaction leaves neither the interaction nor a task behind: the
    /// command is one transaction.
    /// </summary>
    [Fact]
    public async Task RecordInteraction_WithAnUnknownParticipant_WritesNothing()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "atomic");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Meeting",
                DateTimeOffset.UtcNow,
                "Met a ghost",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", Guid.NewGuid()))],
                FollowUp: new FollowUpTaskRequest("Follow up with the ghost")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        Assert.False(await context.Interactions.AsNoTracking()
            .AnyAsync(i => i.OrganizationId == actor.Organization.Id));

        Assert.False(await context.Tasks.AsNoTracking()
            .AnyAsync(t => t.OrganizationId == actor.Organization.Id));
    }

    [Fact]
    public async Task Tasks_CanBeCompletedAndReopened()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "tasks");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks",
            new CreateTaskRequest("Review the deal memo", DateTimeOffset.UtcNow.AddDays(1), "High"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid taskId = (await created.Content.ReadFromJsonAsync<CreatedId>())!.Id;

        using (HttpResponseMessage complete = await client.PostAsync(
            $"/api/v1/organizations/{tenant}/tasks/{taskId}/complete", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        TaskResponse[] all = await client
            .GetFromJsonAsync<TaskResponse[]>($"/api/v1/organizations/{tenant}/tasks?openOnly=false") ?? [];

        TaskResponse completed = all.Single(t => t.Id == taskId);
        Assert.Equal("Completed", completed.State);
        Assert.NotNull(completed.CompletedAt);

        using (HttpResponseMessage reopen = await client.PostAsync(
            $"/api/v1/organizations/{tenant}/tasks/{taskId}/reopen", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, reopen.StatusCode);
        }

        all = await client
            .GetFromJsonAsync<TaskResponse[]>($"/api/v1/organizations/{tenant}/tasks?openOnly=false") ?? [];

        TaskResponse reopened = all.Single(t => t.Id == taskId);
        Assert.Equal("Open", reopened.State);

        // The completion stamp is cleared, so an open task never claims completion.
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Relationship_CanBeEndedAndStaysAsHistory()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "endrel");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Elena");
        CompanyDetailResponse company = await CreateCompanyAsync(client, tenant, "Northlight Media");

        using HttpResponseMessage createdRelationship = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/relationships",
            new CreateRelationshipRequest(
                new PartyRefRequest("Person", person.Person.Id),
                new PartyRefRequest("Company", company.Company.Id),
                "Employment"));

        Guid relationshipId = (await createdRelationship.Content.ReadFromJsonAsync<CreatedId>())!.Id;

        using (HttpResponseMessage ended = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/relationships/{relationshipId}/end",
            new EndRelationshipRequest(DateTimeOffset.UtcNow)))
        {
            Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        }

        PersonDetailResponse after = await client
            .GetFromJsonAsync<PersonDetailResponse>($"/api/v1/organizations/{tenant}/people/{person.Person.Id}")
            ?? throw new InvalidOperationException("Person disappeared.");

        // Still there, now ended. History is not deleted.
        RelationshipResponse relationship = Assert.Single(after.Relationships);
        Assert.Equal("Ended", relationship.Status);
        Assert.NotNull(relationship.EndedAt);
    }

    /// <summary>The domain's pairing rules are enforced through the API too.</summary>
    [Fact]
    public async Task Relationship_RejectsAPairingTheTypeDoesNotAccept()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "pairing");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse first = await CreatePersonAsync(client, tenant, "Ana");
        PersonDetailResponse second = await CreatePersonAsync(client, tenant, "Ben");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/relationships",
            new CreateRelationshipRequest(
                new PartyRefRequest("Person", first.Person.Id),
                new PartyRefRequest("Person", second.Person.Id),
                "Employment"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Company_TimelineShowsItsInteractionsAndStaff()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "companytimeline");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        CompanyDetailResponse company = await CreateCompanyAsync(client, tenant, "Harbour Pictures");

        await CreatedAsync<RecordInteractionResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Meeting",
                DateTimeOffset.UtcNow,
                "Pitch meeting at Harbour",
                [new InteractionParticipantRequest(new PartyRefRequest("Company", company.Company.Id))])));

        TimelineEntryResponse[] timeline = await client
            .GetFromJsonAsync<TimelineEntryResponse[]>(
                $"/api/v1/organizations/{tenant}/companies/{company.Company.Id}/timeline") ?? [];

        Assert.Contains(timeline, e => e.Kind == "interaction" && e.Title == "Pitch meeting at Harbour");
    }

    /// <summary>Basic name filtering is enough for M2; search architecture is M3.</summary>
    [Fact]
    public async Task ListPeople_FiltersByName()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Persephone");
        await CreatePersonAsync(client, tenant, "Gregor");

        PersonSummaryResponse[] matches = await client
            .GetFromJsonAsync<PersonSummaryResponse[]>(
                $"/api/v1/organizations/{tenant}/people?search=perse") ?? [];

        Assert.Single(matches);
        Assert.Equal("Persephone", matches[0].DisplayName);
    }

    /// <summary>Consequential mutations leave audit evidence.</summary>
    [Fact]
    public async Task Mutations_AreAudited()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "audit");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Audited");

        await CreatedAsync<RecordInteractionResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/interactions",
            new RecordInteractionRequest(
                "Note",
                DateTimeOffset.UtcNow,
                "Noted something",
                [new InteractionParticipantRequest(new PartyRefRequest("Person", person.Person.Id))])));

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        AuditEvent created = await context.AuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.Action == AuditAction.PersonCreated
                && x.EntityId == person.Person.Id.ToString());

        Assert.Equal(actor.User.Id, created.ActorUserId);
        Assert.Equal(Permission.PeopleWrite, created.Permission);
        Assert.Equal(actor.Organization.Id, created.OrganizationId);

        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(x =>
            x.Action == AuditAction.InteractionRecorded && x.OrganizationId == actor.Organization.Id));
    }

    // ------------------------------------------------------------- helpers

    private static async Task<PersonDetailResponse> CreatePersonAsync(HttpClient client, Guid tenant, string name) =>
        await CreatedAsync<PersonDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(name)));

    private static async Task<CompanyDetailResponse> CreateCompanyAsync(HttpClient client, Guid tenant, string name) =>
        await CreatedAsync<CompanyDetailResponse>(client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest(name, "Studio")));

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> call)
    {
        using HttpResponseMessage response = await call;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Expected a {typeof(T).Name} body.");
    }

    private sealed record CreatedId(Guid Id);
}
