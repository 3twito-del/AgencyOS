using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>The pipeline list: filters, derived counts and states.</summary>
public sealed class OpportunityListViewModelTests
{
    [Fact]
    public async Task TheList_LoadsAndReportsWhatItFound()
    {
        FakeAgencyOsApi api = new();

        api.Opportunities.Add(FakeAgencyOsApi.Opportunity("The Undertow - take out"));
        api.Opportunities.Add(FakeAgencyOsApi.Opportunity("Salt Road - staffing", "Staffing"));

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Opportunities.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>The list opens on what is being worked, not on everything ever closed.</summary>
    [Fact]
    public async Task TheList_DefaultsToActivePursuits()
    {
        FakeAgencyOsApi api = new();

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal("Active", api.LastOpportunityFilter.Status);
    }

    [Fact]
    public async Task EveryFilter_ReachesTheServerRatherThanBeingAppliedLocally()
    {
        FakeAgencyOsApi api = new();

        OpportunityListViewModel viewModel = new(api)
        {
            Status = "Paused",
            Kind = "PackageMarket",
            AwaitingResponse = true,
            Search = "  undertow  ",
        };

        await viewModel.LoadAsync();

        Assert.Equal(("Paused", "PackageMarket", true, "undertow"), api.LastOpportunityFilter);
    }

    /// <summary>
    /// Silence is counted, never stored. The awaiting figure comes from the rows
    /// the server derived, and nothing in the client invents a non-response.
    /// </summary>
    [Fact]
    public async Task WaitingAndOverdue_AreDerivedFromWhatTheServerReturned()
    {
        DateOnly yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        DateOnly nextWeek = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        FakeAgencyOsApi api = new();

        api.Opportunities.Add(FakeAgencyOsApi.Opportunity(
            "Late", awaitingResponseCount: 2, nextActionOn: yesterday));
        api.Opportunities.Add(FakeAgencyOsApi.Opportunity(
            "On time", nextActionOn: nextWeek));
        api.Opportunities.Add(FakeAgencyOsApi.Opportunity("No action yet"));

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(1, viewModel.Waiting);
        Assert.Equal(1, viewModel.Overdue);
    }

    [Fact]
    public async Task AnEmptyPipeline_IsAStateRatherThanAnError()
    {
        FakeAgencyOsApi api = new();

        OpportunityListViewModel viewModel = new(api);

        Assert.False(viewModel.IsEmpty);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>A refusal shows the server's own sentence, not a generic failure.</summary>
    [Fact]
    public async Task ARefusal_SurfacesTheServersExplanation()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'opportunities.read' is required."),
        };

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("Permission 'opportunities.read' is required.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task AnUnreachableServer_IsNotAnEmptyPipeline()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("no route to host") };

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>An out-of-date build is told so, rather than shown a bare 426.</summary>
    [Fact]
    public async Task AnOutOfDateBuild_IsToldToUpdate()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.UpgradeRequired,
                "Client update required",
                "Version 0.5.0 is below the minimum supported version 0.6.0."),
        };

        OpportunityListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Contains("must be updated", viewModel.ErrorMessage, StringComparison.Ordinal);
    }
}

/// <summary>One pursuit's working surface.</summary>
public sealed class OpportunityDetailViewModelTests
{
    [Fact]
    public async Task TheDetail_LoadsTargetsSubmissionsPitchesAndHistoryTogether()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        api.OpportunityHistory.Add(new OpportunityHistoryEntryResponse(
            DateTimeOffset.UtcNow, "TargetMoved", "Northgate Pictures moved to Contacted",
            null, "Northgate Pictures", "Dana Ruiz"));

        OpportunityDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.NotNull(viewModel.Opportunity);
        Assert.Single(viewModel.Targets);
        Assert.Single(viewModel.Submissions);
        Assert.Single(viewModel.Pitches);
        Assert.Single(viewModel.History);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// Strategy is absent rather than refused when the caller may not read it, and
    /// the screen shows nothing at all. A "hidden" placeholder would leak the fact
    /// that there is something to hide.
    /// </summary>
    [Fact]
    public async Task StrategyIsShownOnlyWhenTheServerReturnedIt()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        OpportunityDetailViewModel redacted = new(api);
        await redacted.LoadAsync(id);

        Assert.False(redacted.HasStrategy);
        Assert.Null(redacted.Opportunity!.StrategyNotes);

        api.OpportunityStrategy = "Go to Northgate first; they owe us a read.";

        OpportunityDetailViewModel permitted = new(api);
        await permitted.LoadAsync(id);

        Assert.True(permitted.HasStrategy);
    }

    [Fact]
    public async Task OpenAndOverdueTargets_AreDerivedFromTheTargetRows()
    {
        DateOnly yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();

        api.Opportunities.Add(FakeAgencyOsApi.Opportunity("The Undertow - take out", id: id));

        api.Targets[id] =
        [
            FakeAgencyOsApi.Target(Guid.NewGuid(), "Contacted", yesterday),
            FakeAgencyOsApi.Target(Guid.NewGuid(), "Engaged"),
            FakeAgencyOsApi.Target(Guid.NewGuid(), "Passed", yesterday),
        ];

        OpportunityDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.Equal(2, viewModel.OpenTargets.Count);

        // The passed target is late on paper and irrelevant in fact.
        Assert.Single(viewModel.Overdue);
    }

    [Fact]
    public async Task Standing_SaysWhereThePursuitActuallyStands()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        OpportunityDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.Contains("1 of 1 targets open", viewModel.Standing, StringComparison.Ordinal);
    }

    [Fact]
    public void BeforeLoading_TheDetailIsNeitherEmptyNorFailed()
    {
        OpportunityDetailViewModel viewModel = new(new FakeAgencyOsApi());

        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.Equal(string.Empty, viewModel.Standing);
    }

    [Fact]
    public async Task ARefusedDetail_KeepsThePreviousScreenRatherThanShowingAnEmptyOne()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        OpportunityDetailViewModel viewModel = new(api);

        api.NextFailure = new AgencyOsApiException(
            HttpStatusCode.Forbidden, "Permission denied", "Permission 'opportunities.read' is required.");

        await viewModel.LoadAsync(id);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.Empty(viewModel.Targets);
    }

    /// <summary>A pursuit with one target that has had one submission and one pitch.</summary>
    internal static async Task<Guid> SeedAsync(FakeAgencyOsApi api)
    {
        Guid id = Guid.NewGuid();

        api.Opportunities.Add(FakeAgencyOsApi.Opportunity("The Undertow - take out", id: id));

        Guid target = await api.AddOpportunityTargetAsync(
            id, new AddOpportunityTargetRequest(1, CompanyId: Guid.NewGuid()), Guid.NewGuid().ToString("N"));

        await api.RecordSubmissionAsync(
            target,
            new RecordSubmissionRequest("Email", 1, Subject: "The Undertow - draft 4"),
            Guid.NewGuid().ToString("N"));

        await api.RecordPitchAsync(
            target,
            new RecordPitchRequest(
                "Meeting",
                "Walked them through the package",
                [new PitchParticipantRequest("Company", Guid.NewGuid(), "Buyer")],
                "Formal",
                "Interested",
                1),
            Guid.NewGuid().ToString("N"));

        return id;
    }
}

/// <summary>One target's workspace, and the silence derived from it.</summary>
public sealed class OpportunityTargetViewModelTests
{
    [Fact]
    public async Task TheTarget_LoadsWithItsSubmissions()
    {
        FakeAgencyOsApi api = new();

        Guid id = await OpportunityDetailViewModelTests.SeedAsync(api);
        Guid target = api.TargetsOf(id)[0].Id;

        OpportunityTargetViewModel viewModel = new(api);

        await viewModel.LoadAsync(target);

        Assert.NotNull(viewModel.Target);
        Assert.Single(viewModel.Submissions);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// Awaiting a reply is read off the submissions the server returned. No row
    /// anywhere represents a buyer's silence, so nothing here can be stale.
    /// </summary>
    [Fact]
    public async Task AwaitingResponse_IsDerivedFromTheSubmissionRows()
    {
        FakeAgencyOsApi api = new();

        Guid id = await OpportunityDetailViewModelTests.SeedAsync(api);
        Guid target = api.TargetsOf(id)[0].Id;

        OpportunityTargetViewModel viewModel = new(api);

        await viewModel.LoadAsync(target);
        Assert.Empty(viewModel.AwaitingResponse);

        api.Submissions[0] = api.Submissions[0] with { IsAwaitingResponse = true };

        await viewModel.LoadAsync(target);
        Assert.Single(viewModel.AwaitingResponse);
    }

    [Fact]
    public async Task Standing_NamesTheStageAndWhatHasGone()
    {
        FakeAgencyOsApi api = new();

        Guid opportunity = Guid.NewGuid();
        Guid target = Guid.NewGuid();

        api.Targets[opportunity] =
        [
            FakeAgencyOsApi.Target(
                target, "Engaged", submissionCount: 2, awaitingSince: new DateOnly(2026, 8, 30)),
        ];

        OpportunityTargetViewModel viewModel = new(api);

        await viewModel.LoadAsync(target);

        Assert.Equal(
            "Engaged - 2 submitted, 0 pitched, awaiting a reply since 2026-08-30",
            viewModel.Standing);
    }

    [Fact]
    public async Task ARefusedTarget_ReportsTheRefusal()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.Forbidden, "Permission denied", "Permission 'submissions.read' is required."),
        };

        OpportunityTargetViewModel viewModel = new(api);

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.True(viewModel.HasError);
        Assert.Equal(string.Empty, viewModel.Standing);
    }
}

/// <summary>The board view, offered alongside the list and never instead of it.</summary>
public sealed class PipelineViewModelTests
{
    [Fact]
    public async Task ThePipeline_GroupsOpenTargetsByStage()
    {
        FakeAgencyOsApi api = new();

        api.Pipeline.Add(Column("Contacted", 2));
        api.Pipeline.Add(Column("Engaged", 1));

        PipelineViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Columns.Count);
        Assert.Equal(3, viewModel.TargetCount);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>Stage columns with nothing in them are still an empty pipeline.</summary>
    [Fact]
    public async Task ColumnsWithNoTargets_ReadAsEmpty()
    {
        FakeAgencyOsApi api = new();

        api.Pipeline.Add(Column("Identified", 0));
        api.Pipeline.Add(Column("Contacted", 0));

        PipelineViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.Equal(0, viewModel.TargetCount);
    }

    [Fact]
    public async Task ARefusedPipeline_IsNotAnEmptyBoard()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.Forbidden, "Permission denied", "Permission 'opportunities.read' is required."),
        };

        PipelineViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
    }

    private static PipelineColumnResponse Column(string stage, int count) =>
        new(
            stage,
            [.. Enumerable.Range(0, count).Select(_ => new PipelineEntryResponse(
                Guid.NewGuid(),
                "The Undertow - take out",
                FakeAgencyOsApi.Target(Guid.NewGuid(), stage)))]);
}

/// <summary>
/// The market commands the palette offers, and what recording actually does.
/// </summary>
public sealed class MarketActivityClientTests
{
    /// <summary>
    /// Every market command the pipeline screen handles is reachable from the
    /// keyboard. A command the palette names but nothing implements is worse than
    /// no command at all.
    /// </summary>
    [Theory]
    [InlineData("opportunity.create")]
    [InlineData("opportunity.target.add")]
    [InlineData("submission.record")]
    [InlineData("pitch.record")]
    [InlineData("target.response.record")]
    [InlineData("go.overdue")]
    [InlineData("go.pipeline")]
    public void ThePalette_OffersTheMarketCommands(string commandId)
    {
        CommandPaletteViewModel palette = new();

        Assert.Contains(palette.AllCommands, c => c.Id == commandId);
    }

    [Fact]
    public void ThePalette_FindsMarketCommandsByCategory()
    {
        CommandPaletteViewModel palette = new() { Query = "Market" };

        Assert.NotEmpty(palette.Results);
        Assert.All(palette.Results, c => Assert.Equal("Market", c.Category));
    }

    /// <summary>
    /// Every market write carries an idempotency key. M6 writes are online-only, so
    /// the key is the client's whole half of the at-most-one-effect guarantee: a
    /// keyless write the server cannot recognize as a replay is a duplicate
    /// submission waiting to happen.
    /// </summary>
    [Fact]
    public async Task EveryMarketWrite_CarriesAnIdempotencyKey()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        Guid target = Guid.NewGuid();

        api.Targets[id] = [FakeAgencyOsApi.Target(target, "Contacted")];

        await api.RecordSubmissionAsync(
            target,
            new RecordSubmissionRequest("Email", 1, Subject: "The Undertow - draft 4"),
            Guid.NewGuid().ToString("N"));

        await api.MoveOpportunityTargetAsync(
            target, new MoveOpportunityTargetRequest("Engaged", 1), Guid.NewGuid().ToString("N"));

        await api.RecordTargetResponseAsync(
            target, new RecordTargetResponseRequest("Acknowledged", 2), Guid.NewGuid().ToString("N"));

        Assert.Equal(3, api.IdempotencyKeys.Count);
        Assert.All(api.IdempotencyKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(3, api.IdempotencyKeys.Distinct().Count());
        Assert.All(api.Effects.Values, count => Assert.Equal(1, count));
    }

    /// <summary>
    /// A pitch produces one interaction, not a second record of the same meeting.
    /// </summary>
    [Fact]
    public async Task ARecordedPitch_CarriesTheOneInteractionItIsTheReadingOf()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        Guid target = Guid.NewGuid();

        api.Targets[id] = [FakeAgencyOsApi.Target(target, "Contacted")];

        RecordPitchResponse recorded = await api.RecordPitchAsync(
            target,
            new RecordPitchRequest(
                "Meeting",
                "Walked them through the package",
                [new PitchParticipantRequest("Company", Guid.NewGuid(), "Buyer")],
                "Formal",
                "Interested",
                1),
            Guid.NewGuid().ToString("N"));

        Assert.NotEqual(Guid.Empty, recorded.InteractionId);
        Assert.Equal(recorded.InteractionId, api.Pitches.Single().InteractionId);
    }

    /// <summary>
    /// A follow-up asked for on the dialog comes back as one task, created with the
    /// activity rather than left to the user to remember.
    /// </summary>
    [Fact]
    public async Task AFollowUpAskedFor_ComesBackAsATask()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        Guid target = Guid.NewGuid();

        api.Targets[id] = [FakeAgencyOsApi.Target(target, "Contacted")];

        RecordSubmissionResponse without = await api.RecordSubmissionAsync(
            target, new RecordSubmissionRequest("Email", 1), Guid.NewGuid().ToString("N"));

        Assert.Null(without.FollowUpTaskId);

        RecordSubmissionResponse with = await api.RecordSubmissionAsync(
            target,
            new RecordSubmissionRequest(
                "Email", 1, FollowUp: new OpportunityFollowUpRequest("Chase Northgate")),
            Guid.NewGuid().ToString("N"));

        Assert.NotNull(with.FollowUpTaskId);
    }
}
