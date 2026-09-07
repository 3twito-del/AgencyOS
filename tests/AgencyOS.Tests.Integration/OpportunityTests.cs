using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M6 pursuit workflow, end to end against PostgreSQL.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class OpportunityTests
{
    private readonly AgencyOsTestFixture _fixture;

    public OpportunityTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole workflow, in the order an agency performs it.
    /// </summary>
    /// <remarks>
    /// One test on purpose: the value of M6 is that these steps connect. Separate
    /// tests would each pass while the joins between them stayed broken.
    /// </remarks>
    [Fact]
    public async Task Workflow_FromOpportunityToPitch()
    {
        Fixture f = await SetUpAsync("m6-workflow");

        // A pursuit starts as a draft, because a draft is where the pieces are
        // assembled.
        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    f.Actor.User.Id.Value,
                    Description: "Take the feature out to studios.",
                    StrategyNotes: "Northgate last; they owe us and will wait.",
                    Subjects:
                    [
                        new OpportunitySubjectRequest("Project", f.ProjectId, "Primary"),
                    ])));

        Assert.Equal("Draft", opportunity.Opportunity.Status);
        Assert.Equal("Project", opportunity.Opportunity.PrimarySubject!.Kind);

        Guid id = opportunity.Opportunity.Id;

        // A target can be lined up while the pursuit is still a draft - that is what
        // a draft is for. Recording market activity against it cannot.
        await CreatedIdAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{id}/targets",
            new AddOpportunityTargetRequest(opportunity.Opportunity.Version, CompanyId: f.StudioId)));

        opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{id}");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{id}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{id}");

        OpportunityTargetResponse target = Assert.Single(opportunity.Targets);

        Assert.Equal("Identified", target.Stage);
        Assert.Equal("Northgate Pictures", target.DisplayName);

        // Approve, then approach.
        await MoveAsync(f, target.Id, "Approved", target.Version);
        await MoveAsync(f, target.Id, "Contacted", target.Version + 1);

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{target.Id}");

        // Recording a submission moves the target along and snapshots the material.
        RecordSubmissionResponse submitted = await CreatedAsync<RecordSubmissionResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{target.Id}/submissions",
                new RecordSubmissionRequest(
                    "Email",
                    target.Version,
                    Materials: [new SubmissionMaterialRequest(f.MaterialId, "The current draft")],
                    Subject: "The Undertow - draft 4",
                    ResponseExpectedBy: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                    FollowUp: new OpportunityFollowUpRequest(
                        "Chase Northgate", DateTimeOffset.UtcNow.AddDays(14)))));

        Assert.NotNull(submitted.FollowUpTaskId);

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{target.Id}");

        Assert.Equal("Engaged", target.Stage);
        Assert.Equal(1, target.SubmissionCount);
        Assert.NotNull(target.LastSubmittedAt);

        SubmissionResponse submission = await GetAsync<SubmissionResponse>(
            f.Client, $"{f.Root}/submissions/{submitted.SubmissionId}");

        SubmissionMaterialResponse material = Assert.Single(submission.Materials);

        Assert.Equal("The Undertow - draft 4", material.TitleAtSubmission);
        Assert.Equal("Draft 4", material.VersionLabelAtSubmission);

        // A response that does not decide anything.
        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{target.Id}/responses",
            new RecordTargetResponseRequest(
                "Acknowledged",
                target.Version,
                SubmissionId: submitted.SubmissionId,
                Note: "Received, reading this week.")));

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{target.Id}");

        Assert.Equal("Engaged", target.Stage);

        // A pitch: one interaction, one pitch, one follow-up, all together.
        RecordPitchResponse pitched = await CreatedAsync<RecordPitchResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{target.Id}/pitches",
                new RecordPitchRequest(
                    "Meeting",
                    "Pitched The Undertow to Northgate",
                    [new PitchParticipantRequest("Company", f.StudioId, "Buyer")],
                    "Formal",
                    "Interested",
                    target.Version,
                    Materials: [new SubmissionMaterialRequest(f.MaterialId)],
                    Subject: "The Undertow",
                    FollowUp: new OpportunityFollowUpRequest("Send the deck"))));

        Assert.NotEqual(Guid.Empty, pitched.InteractionId);
        Assert.NotNull(pitched.FollowUpTaskId);

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{target.Id}");

        // The pitch outcome moved the pipeline, so the two agree.
        Assert.Equal("Interested", target.Stage);
        Assert.Equal(1, target.PitchCount);

        // The history is one coherent list, and a pitch appears once.
        OpportunityHistoryEntryResponse[] history = await GetAsync<OpportunityHistoryEntryResponse[]>(
            f.Client, $"{f.Root}/opportunities/{id}/history");

        Assert.Contains(history, x => x.Kind == "Created");
        Assert.Contains(history, x => x.Kind == "Submission");
        Assert.Single(history, x => x.Kind == "Pitch");

        // The overview carries the follow-up tasks the workflow created.
        opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{id}");

        Assert.Equal(2, opportunity.OpenTasks.Count);
        Assert.Equal(1, opportunity.Opportunity.SubmissionCount);
    }

    /// <summary>
    /// A pitch produces exactly one interaction, shared with the M2 timeline.
    /// </summary>
    /// <remarks>
    /// One real-world event, one interaction identity. Two would put the same
    /// meeting on a person's timeline twice.
    /// </remarks>
    [Fact]
    public async Task APitch_CreatesOneInteractionAndNoDuplicate()
    {
        Fixture f = await SetUpAsync("m6-pitch-interaction");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        RecordPitchResponse pitched = await CreatedAsync<RecordPitchResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{targetId}/pitches",
                new RecordPitchRequest(
                    "Meeting",
                    "Pitched the feature",
                    [new PitchParticipantRequest("Person", f.ContactId, "Executive")],
                    "Formal",
                    "NoDecision",
                    target.Version)));

        // The interaction is a real M2 interaction: it appears on the person's
        // timeline, exactly once.
        TimelineEntryResponse[] timeline = await GetAsync<TimelineEntryResponse[]>(
            f.Client, $"{f.Root}/people/{f.ContactId}/timeline");

        Assert.Single(timeline, x => x.Title == "Pitched the feature");

        PitchResponse[] pitches = await GetAsync<PitchResponse[]>(
            f.Client, $"{f.Root}/pitches");

        Assert.Equal(pitched.InteractionId, Assert.Single(pitches).InteractionId);
    }

    /// <summary>
    /// A replayed submission records one submission, one task and one interaction.
    /// </summary>
    /// <remarks>
    /// The composite command is the one a lost acknowledgement could most easily
    /// duplicate: retrying it naively would produce a second submission, a second
    /// follow-up task nobody asked for, and a target moved twice.
    /// </remarks>
    [Fact]
    public async Task AReplayedSubmission_RecordsItOnce()
    {
        Fixture f = await SetUpAsync("m6-submission-replay");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        string key = Guid.NewGuid().ToString("N");

        RecordSubmissionRequest request = new(
            "Email",
            target.Version,
            Materials: [new SubmissionMaterialRequest(f.MaterialId)],
            Subject: "Draft 4",
            FollowUp: new OpportunityFollowUpRequest("Chase"));

        RecordSubmissionResponse first = await SubmitWithKeyAsync(f, targetId, request, key);
        RecordSubmissionResponse second = await SubmitWithKeyAsync(f, targetId, request, key);

        Assert.Equal(first.SubmissionId, second.SubmissionId);
        Assert.Equal(first.FollowUpTaskId, second.FollowUpTaskId);

        SubmissionResponse[] submissions = await GetAsync<SubmissionResponse[]>(
            f.Client, $"{f.Root}/submissions");

        Assert.Single(submissions);

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Assert.Single(opportunity.OpenTasks);
    }

    /// <summary>A replayed pitch records one interaction, not two.</summary>
    [Fact]
    public async Task AReplayedPitch_RecordsItOnce()
    {
        Fixture f = await SetUpAsync("m6-pitch-replay");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        string key = Guid.NewGuid().ToString("N");

        RecordPitchRequest request = new(
            "Meeting",
            "Pitched the feature",
            [new PitchParticipantRequest("Person", f.ContactId)],
            "Formal",
            "NoDecision",
            target.Version);

        RecordPitchResponse first = await PitchWithKeyAsync(f, targetId, request, key);
        RecordPitchResponse second = await PitchWithKeyAsync(f, targetId, request, key);

        Assert.Equal(first.PitchId, second.PitchId);
        Assert.Equal(first.InteractionId, second.InteractionId);

        TimelineEntryResponse[] timeline = await GetAsync<TimelineEntryResponse[]>(
            f.Client, $"{f.Root}/people/{f.ContactId}/timeline");

        Assert.Single(timeline, x => x.Title == "Pitched the feature");
    }

    /// <summary>
    /// Two people closing the same target differently: one wins, one is told.
    /// </summary>
    /// <remarks>
    /// This is a real newsroom-floor race - two agents hear from the same buyer and
    /// both record it. Last-write-wins would silently discard one account of what
    /// happened; the version guard turns it into a conflict somebody can resolve.
    /// </remarks>
    [Fact]
    public async Task TwoPeopleClosingOneTarget_ProduceAConflictRatherThanSilentLoss()
    {
        Fixture f = await SetUpAsync("m6-close-race");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        int observed = target.Version;

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/stage",
            new MoveOpportunityTargetRequest("Passed", observed, Note: "They passed")));

        using HttpResponseMessage conflict = await f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/stage",
            new MoveOpportunityTargetRequest("Withdrawn", observed, Note: "We pulled out"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        Assert.Equal("Passed", target.Stage);
    }

    /// <summary>
    /// Eight clients adding the same buyer produce one open target.
    /// </summary>
    /// <remarks>
    /// The handler checks and a partial unique index refuses. Two open targets for
    /// one studio means two agents working the same buyer without knowing about
    /// each other, which is the situation this part of the system exists to
    /// prevent.
    /// </remarks>
    [Fact]
    public async Task ConcurrentTargets_AddTheSameBuyerOnce()
    {
        Fixture f = await SetUpAsync("m6-target-race");

        await ActivateAsync(f);

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        int version = opportunity.Opportunity.Version;

        List<Task<HttpResponseMessage>> attempts = [];
        List<HttpClient> clients = [];

        for (int index = 0; index < 8; index++)
        {
            HttpClient client = _fixture.CreateClient(f.Actor.Subject);
            clients.Add(client);

            attempts.Add(client.PostAsJsonAsync(
                $"{f.Root}/opportunities/{f.OpportunityId}/targets",
                new AddOpportunityTargetRequest(version, CompanyId: f.StudioId)));
        }

        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        int created = responses.Count(x => x.StatusCode == HttpStatusCode.Created);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }

        Assert.Equal(1, created);

        OpportunityTargetResponse[] targets = await GetAsync<OpportunityTargetResponse[]>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}/targets");

        Assert.Single(targets);
    }

    /// <summary>A closed buyer can be approached again as a new target.</summary>
    [Fact]
    public async Task AfterATargetCloses_TheSameBuyerCanBeAddedAgain()
    {
        Fixture f = await SetUpAsync("m6-reapproach");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/stage",
            new MoveOpportunityTargetRequest("Passed", target.Version)));

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        await CreatedIdAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                opportunity.Opportunity.Version, CompanyId: f.StudioId)));

        OpportunityTargetResponse[] targets = await GetAsync<OpportunityTargetResponse[]>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}/targets");

        Assert.Equal(2, targets.Length);
        Assert.Single(targets, x => x.IsOpen);
    }

    /// <summary>Market activity is refused against a pursuit that is not being worked.</summary>
    [Fact]
    public async Task ActivityAgainstAClosedPursuit_IsRefused()
    {
        Fixture f = await SetUpAsync("m6-closed-pursuit");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/status",
            new ChangeOpportunityStatusRequest(
                "Closed", opportunity.Opportunity.Version, Outcome: "NoInterest")));

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/submissions",
            new RecordSubmissionRequest("Email", target.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>A target cannot be another tenant's company.</summary>
    [Fact]
    public async Task ATarget_CannotReachIntoAnotherTenant()
    {
        Fixture mine = await SetUpAsync("m6-tenant-mine");
        Fixture theirs = await SetUpAsync("m6-tenant-theirs");

        await ActivateAsync(mine);

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            mine.Client, $"{mine.Root}/opportunities/{mine.OpportunityId}");

        using HttpResponseMessage refused = await mine.Client.PostAsJsonAsync(
            $"{mine.Root}/opportunities/{mine.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                opportunity.Opportunity.Version, CompanyId: theirs.StudioId));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>A subject cannot be another tenant's project.</summary>
    [Fact]
    public async Task ASubject_CannotReachIntoAnotherTenant()
    {
        Fixture mine = await SetUpAsync("m6-subject-mine");
        Fixture theirs = await SetUpAsync("m6-subject-theirs");

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            mine.Client, $"{mine.Root}/opportunities/{mine.OpportunityId}");

        using HttpResponseMessage refused = await mine.Client.PostAsJsonAsync(
            $"{mine.Root}/opportunities/{mine.OpportunityId}/subjects",
            new AddOpportunitySubjectRequest(
                "Project", theirs.ProjectId, opportunity.Opportunity.Version));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>A contact is refused on a person target.</summary>
    [Fact]
    public async Task AContactOnAPersonTarget_IsRefused()
    {
        Fixture f = await SetUpAsync("m6-person-contact");

        await ActivateAsync(f);

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                opportunity.Opportunity.Version,
                PersonId: f.ContactId,
                ContactPersonId: f.ContactId));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>
    /// Awaiting a response is derived, and nothing is written to represent silence.
    /// </summary>
    /// <remarks>
    /// A buyer who does not reply produces no row anywhere. The state comes from the
    /// submission's expected date and the absence of anything after it (ADR-0020).
    /// </remarks>
    [Fact]
    public async Task AwaitingAResponse_IsDerivedFromSilence()
    {
        Fixture f = await SetUpAsync("m6-awaiting");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        // Expected a reply a week ago, and nothing has been recorded since.
        RecordSubmissionResponse submitted = await CreatedAsync<RecordSubmissionResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{targetId}/submissions",
                new RecordSubmissionRequest(
                    "Email",
                    target.Version,
                    SentAt: DateTimeOffset.UtcNow.AddDays(-30),
                    ResponseExpectedBy: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7))));

        SubmissionResponse submission = await GetAsync<SubmissionResponse>(
            f.Client, $"{f.Root}/submissions/{submitted.SubmissionId}");

        Assert.True(submission.IsAwaitingResponse);
        Assert.Null(submission.LastResponseAt);

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        Assert.NotNull(target.AwaitingResponseSince);

        // Recording a response ends the wait, without any backdated row.
        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/responses",
            new RecordTargetResponseRequest(
                "Acknowledged", target.Version, SubmissionId: submitted.SubmissionId)));

        submission = await GetAsync<SubmissionResponse>(
            f.Client, $"{f.Root}/submissions/{submitted.SubmissionId}");

        Assert.False(submission.IsAwaitingResponse);
        Assert.NotNull(submission.LastResponseAt);
    }

    /// <summary>The command centre reports business facts, not scores.</summary>
    [Fact]
    public async Task TheCommandCentre_ReportsOverdueAndAwaiting()
    {
        Fixture f = await SetUpAsync("m6-command-centre");

        Guid targetId = await ActiveTargetAsync(f, "Contacted");

        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        await NoContentAsync(f.Client.PutAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}",
            new UpdateOpportunityTargetRequest(
                target.Version,
                NextActionOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3))));

        target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{targetId}");

        await CreatedAsync<RecordSubmissionResponse>(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/submissions",
            new RecordSubmissionRequest(
                "Email",
                target.Version,
                SentAt: DateTimeOffset.UtcNow.AddDays(-30),
                ResponseExpectedBy: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7))));

        OpportunityCommandCenterResponse centre = await GetAsync<OpportunityCommandCenterResponse>(
            f.Client, $"{f.Root}/opportunity-command-center");

        Assert.Single(centre.OverdueFollowUps);
        Assert.Single(centre.AwaitingResponse);
        Assert.Equal(1, centre.ActiveOpportunityCount);
    }

    /// <summary>The pipeline groups open targets by stage.</summary>
    [Fact]
    public async Task ThePipeline_GroupsOpenTargetsByStage()
    {
        Fixture f = await SetUpAsync("m6-pipeline");

        await ActiveTargetAsync(f, "Contacted");

        PipelineColumnResponse[] columns = await GetAsync<PipelineColumnResponse[]>(
            f.Client, $"{f.Root}/pipeline");

        // Only stages a target can still move from. A column of finished things is
        // a report, not a pipeline.
        Assert.DoesNotContain(columns, x => x.Stage == "Passed");

        PipelineColumnResponse contacted = Assert.Single(columns, x => x.Stage == "Contacted");

        Assert.Single(contacted.Targets);
    }

    /// <summary>Opportunities are findable, and strategy is not.</summary>
    [Fact]
    public async Task Search_FindsPursuitsButNeverTheirStrategy()
    {
        Fixture f = await SetUpAsync("m6-search");

        SearchResponse byName = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=undertow");

        Assert.Contains(byName.Hits, x => x.Type == "Opportunity");

        // The strategy note said "Northgate last; they owe us". Searching it must
        // not surface the pursuit, or the redaction would be defeated by a query.
        SearchResponse byStrategy = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=owe");

        Assert.DoesNotContain(byStrategy.Hits, x => x.Type == "Opportunity");
    }

    /// <summary>
    /// Strategy is redacted for an observer, by every route that returns it.
    /// </summary>
    /// <remarks>
    /// M4 shipped a redaction that one read path bypassed, and M5 confirmed the
    /// lesson. This checks the direct read, the saved view and the search index
    /// together (ADR-0020).
    /// </remarks>
    [Fact]
    public async Task Strategy_IsRedactedAcrossEveryProjection()
    {
        Fixture f = await SetUpAsync("m6-strategy");

        string observerSubject = $"m6-observer-{Guid.NewGuid():N}";
        Domain.Identity.User observer = await _fixture.SeedUserAsync(observerSubject, "Observer");

        await _fixture.SeedMembershipAsync(
            f.Actor.Organization.Id, observer.Id, AgencyRole.Observer, f.Actor.User.Id);

        // The member who wrote it can read it back.
        OpportunityDetailResponse mine = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Assert.NotNull(mine.StrategyNotes);

        using HttpClient observerClient = _fixture.CreateClient(observerSubject);

        // Direct read: present but absent.
        OpportunityDetailResponse observed = await GetAsync<OpportunityDetailResponse>(
            observerClient, $"{f.Root}/opportunities/{f.OpportunityId}");

        Assert.Equal(mine.Opportunity.Name, observed.Opportunity.Name);
        Assert.NotNull(observed.Description);
        Assert.Null(observed.StrategyNotes);

        // Saved view: the list carries summaries, which never hold strategy at all.
        Contracts.SavedViews.SavedViewResponse view =
            await CreatedAsync<Contracts.SavedViews.SavedViewResponse>(
            observerClient.PostAsJsonAsync(
                $"{f.Root}/saved-views",
                new Contracts.SavedViews.CreateSavedViewRequest(
                    "Active pursuits",
                    new Contracts.SavedViews.SavedViewDefinitionModel(
                        4,
                        "Opportunities",
                        new Contracts.SavedViews.SavedViewFiltersModel()))));

        Contracts.SavedViews.SavedViewResultsResponse results =
            await GetAsync<Contracts.SavedViews.SavedViewResultsResponse>(
                observerClient, $"{f.Root}/saved-views/{view.Id}/results");

        Assert.Single(results.Opportunities);

        // Search: the pursuit is findable by name, and by nothing from the note.
        SearchResponse hits = await GetAsync<SearchResponse>(
            observerClient, $"{f.Root}/search?q=owe");

        Assert.DoesNotContain(hits.Hits, x => x.Type == "Opportunity");
    }

    /// <summary>Every pursuit read is refused without authentication.</summary>
    [Fact]
    public async Task ThePipeline_RequiresAuthentication()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "m6-anon");

        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/opportunities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    /// <summary>A tenant with a project, a studio, a contact, material and a pursuit.</summary>
    private sealed record Fixture(
        SeededActor Actor,
        HttpClient Client,
        string Root,
        Guid ProjectId,
        Guid StudioId,
        Guid ContactId,
        Guid MaterialId,
        Guid OpportunityId);

    private async Task<Fixture> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/projects", new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        PersonDetailResponse contact = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync($"{root}/people", new CreatePersonRequest("Dana", "Okafor")));

        PersonDetailResponse writer = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync($"{root}/people", new CreatePersonRequest("Priya", "Raghunathan")));

        await CreatedAsync<TalentDetailResponse>(client.PostAsJsonAsync(
            $"{root}/talent",
            new CreateTalentProfileRequest(writer.Person.Id, "Established", Disciplines: ["Writer"])));

        Guid materialId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/materials",
            new AddMaterialRequest(
                writer.Person.Id, "The Undertow - draft 4", "Screenplay", VersionLabel: "Draft 4")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Description: "Take the feature out to studios.",
                    StrategyNotes: "Northgate last; they owe us and will wait.",
                    Subjects: [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        return new Fixture(
            actor,
            client,
            root,
            project.Project.Id,
            studio.Company.Id,
            contact.Person.Id,
            materialId,
            opportunity.Opportunity.Id);
    }

    private static async Task ActivateAsync(Fixture f)
    {
        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        if (opportunity.Opportunity.Status == "Active")
        {
            return;
        }

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));
    }

    /// <summary>Activates the pursuit and walks one company target to a stage.</summary>
    private static async Task<Guid> ActiveTargetAsync(Fixture f, string stage)
    {
        await ActivateAsync(f);

        OpportunityDetailResponse opportunity = await GetAsync<OpportunityDetailResponse>(
            f.Client, $"{f.Root}/opportunities/{f.OpportunityId}");

        Guid targetId = await CreatedIdAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunities/{f.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                opportunity.Opportunity.Version, CompanyId: f.StudioId)));

        string[] path = stage switch
        {
            "Approved" => ["Approved"],
            "Contacted" => ["Approved", "Contacted"],
            _ => [],
        };

        foreach (string step in path)
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                f.Client, $"{f.Root}/opportunity-targets/{targetId}");

            await MoveAsync(f, targetId, step, target.Version);
        }

        return targetId;
    }

    private static Task MoveAsync(Fixture f, Guid targetId, string stage, int version) =>
        NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/opportunity-targets/{targetId}/stage",
            new MoveOpportunityTargetRequest(stage, version)));

    private static async Task<RecordSubmissionResponse> SubmitWithKeyAsync(
        Fixture f,
        Guid targetId,
        RecordSubmissionRequest request,
        string key)
    {
        using HttpRequestMessage message = new(
            HttpMethod.Post, $"{f.Root}/opportunity-targets/{targetId}/submissions")
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordSubmissionResponse>())!;
    }

    private static async Task<RecordPitchResponse> PitchWithKeyAsync(
        Fixture f,
        Guid targetId,
        RecordPitchRequest request,
        string key)
    {
        using HttpRequestMessage message = new(
            HttpMethod.Post, $"{f.Root}/opportunity-targets/{targetId}/pitches")
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordPitchResponse>())!;
    }

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedId? created = await response.Content.ReadFromJsonAsync<CreatedId>();

        return created!.Id;
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

    private sealed record CreatedId(Guid Id);
}
