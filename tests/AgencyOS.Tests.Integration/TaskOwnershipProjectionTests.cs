using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That every read which reports a task's owner resolves who that owner is.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 80 found the client saying "Unassigned" about a
/// task assigned to a named member. The domain, the column and the API were all
/// correct: the talent overview called a projection overload whose assignee lookup
/// is optional, so the task came back carrying an id and no name, and the surface
/// could not tell that from nobody being accountable.
/// </para>
/// <para>
/// This is the contract that would have caught it without any UI: <strong>a
/// projection that reports an assignee id must also report the name</strong>. It is
/// asserted against every operator-facing read that carries ownership, so a future
/// path that forgets the lookup fails here rather than in a handover.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class TaskOwnershipProjectionTests
{
    private readonly AgencyOsTestFixture _fixture;

    public TaskOwnershipProjectionTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>The task list resolves the assignee it reports.</summary>
    [Fact]
    public async Task TheTaskListNamesTheAssignee()
    {
        Scene s = await SetUpAsync("own-list");

        IReadOnlyList<TaskResponse> tasks = await ReadAsync<IReadOnlyList<TaskResponse>>(
            s.Client.GetAsync($"{s.Root}/tasks?openOnly=true&limit=200"));

        TaskResponse assigned = Assert.Single(tasks, x => x.Id == s.AssignedTaskId);

        Assert.Equal(s.MemberId, assigned.AssigneeUserId);
        Assert.False(string.IsNullOrWhiteSpace(assigned.AssigneeDisplayName));
    }

    /// <summary>
    /// The talent overview resolves it too — the read that did not.
    /// </summary>
    [Fact]
    public async Task TheTalentOverviewNamesTheAssignee()
    {
        Scene s = await SetUpAsync("own-overview");

        ClientOverviewResponse overview = await ReadAsync<ClientOverviewResponse>(
            s.Client.GetAsync($"{s.Root}/talent/{s.PersonId}/overview"));

        TaskResponse assigned = Assert.Single(
            overview.OpenTasks, x => x.Id == s.AssignedTaskId);

        Assert.Equal(s.MemberId, assigned.AssigneeUserId);
        Assert.False(string.IsNullOrWhiteSpace(assigned.AssigneeDisplayName));
    }

    /// <summary>
    /// The contract holds across every ownership-reporting read at once.
    /// </summary>
    /// <remarks>
    /// Stated as an invariant rather than per endpoint, so adding a surface that
    /// reports ownership without resolving it is caught by this test rather than by
    /// somebody reading a screen.
    /// </remarks>
    [Fact]
    public async Task NoReadReportsAnOwnerItCannotName()
    {
        Scene s = await SetUpAsync("own-invariant");

        IReadOnlyList<TaskResponse> listed = await ReadAsync<IReadOnlyList<TaskResponse>>(
            s.Client.GetAsync($"{s.Root}/tasks?openOnly=false&limit=200"));

        ClientOverviewResponse overview = await ReadAsync<ClientOverviewResponse>(
            s.Client.GetAsync($"{s.Root}/talent/{s.PersonId}/overview"));

        foreach (TaskResponse task in listed.Concat(overview.OpenTasks))
        {
            Assert.False(
                task.AssigneeUserId is not null
                    && string.IsNullOrWhiteSpace(task.AssigneeDisplayName),
                $"Task '{task.Title}' reports assignee {task.AssigneeUserId} without a name. "
                    + "A surface reading this cannot tell it from unassigned work.");
        }
    }

    /// <summary>Unassigned work reports neither, which is the honest pair.</summary>
    [Fact]
    public async Task UnassignedWorkReportsNoOwnerAndNoName()
    {
        Scene s = await SetUpAsync("own-none");

        ClientOverviewResponse overview = await ReadAsync<ClientOverviewResponse>(
            s.Client.GetAsync($"{s.Root}/talent/{s.PersonId}/overview"));

        TaskResponse open = Assert.Single(
            overview.OpenTasks, x => x.Id == s.UnassignedTaskId);

        Assert.Null(open.AssigneeUserId);
        Assert.Null(open.AssigneeDisplayName);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>What a create returns: the identifier and nothing else.</summary>
    private sealed record Created(Guid Id);

    private sealed record Scene(
        HttpClient Client,
        string Root,
        Guid PersonId,
        Guid MemberId,
        Guid AssignedTaskId,
        Guid UnassignedTaskId);

    private async Task<Scene> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Ottoline", "Brackenridge-Osei")));

        await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent", new CreateTalentProfileRequest(person.Person.Id)));

        PartyRefRequest subject = new("Person", person.Person.Id);

        Created assigned = await CreatedAsync<Created>(
            client.PostAsJsonAsync(
                $"{root}/tasks",
                new CreateTaskRequest(
                    "Confirm the option exercise window",
                    DateTimeOffset.UtcNow.AddDays(12),
                    "High",
                    subject,
                    AssigneeUserId: actor.User.Id.Value)));

        // Creating a task assigns it to whoever created it, so being accountable to
        // nobody is reached by clearing the assignment - which is a real state, not a
        // gap: work is sometimes genuinely unowned.
        Created unassigned = await CreatedAsync<Created>(
            client.PostAsJsonAsync(
                $"{root}/tasks",
                new CreateTaskRequest(
                    "Chase the counter-signature",
                    DateTimeOffset.UtcNow.AddDays(20),
                    "Normal",
                    subject)));

        IReadOnlyList<TaskResponse> current = await ReadAsync<IReadOnlyList<TaskResponse>>(
            client.GetAsync($"{root}/tasks?openOnly=true&limit=200"));

        int version = current.Single(x => x.Id == unassigned.Id).Version;

        using (HttpResponseMessage cleared = await client.PostAsJsonAsync(
            $"{root}/tasks/{unassigned.Id}/assignee", new AssignTaskRequest(version)))
        {
            Assert.True(
                cleared.IsSuccessStatusCode,
                $"{cleared.StatusCode}: {await cleared.Content.ReadAsStringAsync()}");
        }

        return new Scene(
            client, root, person.Person.Id, actor.User.Id.Value, assigned.Id, unassigned.Id);
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> call)
    {
        using HttpResponseMessage response = await call;

        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> call) =>
        await CreatedAsync<T>(call);
}
