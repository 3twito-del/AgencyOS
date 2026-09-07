using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.Sync;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>Global search, and what it says when it could not reach the server.</summary>
public sealed class SearchViewModelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agencyos-search-vm", Guid.NewGuid().ToString("N"));

    private static readonly ExplicitCacheKeyProvider Key =
        new("1122334455667788990011223344556677889900112233445566778899001122");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
    }

    [Fact]
    public async Task Search_ReturnsRankedServerResults()
    {
        FakeAgencyOsApi api = new();
        api.SearchHits.Add(new SearchHit("Person", Guid.NewGuid(), "Sarah Klein", "Literary Agent", "Active", 1.0, "Exact"));

        SearchViewModel viewModel = new(api) { Query = "Sarah Klein" };
        await viewModel.SearchAsync();

        Assert.Single(viewModel.Results);
        Assert.Equal(SearchSource.Server, viewModel.Source);
        Assert.False(viewModel.IsOffline);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>An empty box is a state, not a query for everything.</summary>
    [Fact]
    public async Task EmptyQuery_ReturnsNothingAndIsNotAnError()
    {
        FakeAgencyOsApi api = new();
        api.SearchHits.Add(new SearchHit("Person", Guid.NewGuid(), "Sarah Klein", null, "Active", 1.0, "Exact"));

        SearchViewModel viewModel = new(api) { Query = "   " };
        await viewModel.SearchAsync();

        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task NoMatches_ReportsEmptyRatherThanFailure()
    {
        SearchViewModel viewModel = new(new FakeAgencyOsApi()) { Query = "nobody" };
        await viewModel.SearchAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    /// Offline results are served from the cache and labelled as such.
    /// </summary>
    /// <remarks>
    /// Presenting a stale substring match as the server's ranking would be the
    /// quiet lie this system is built to avoid. The user is told which they got.
    /// </remarks>
    [Fact]
    public async Task WhenTheServerIsUnreachable_ResultsComeFromTheCacheAndSaySo()
    {
        using LocalCache cache = LocalCache.Open(
            _root,
            new LocalCacheIdentity("LAB", Guid.NewGuid(), "agent@example.invalid"),
            Key);

        cache.ApplyPage(
            1,
            [new PersonSummaryResponse(
                Guid.NewGuid(), "Sarah Klein", "Literary Agent", null, null, "Active", null, null,
                DateTimeOffset.UtcNow, 1)],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);

        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("Unreachable.") };

        SearchViewModel viewModel = new(api, cache) { Query = "Klein" };
        await viewModel.SearchAsync();

        SearchHit hit = Assert.Single(viewModel.Results);

        Assert.Equal(SearchSource.Cache, viewModel.Source);
        Assert.True(viewModel.IsOffline);
        Assert.Equal("Cache", hit.MatchedOn);

        // No invented relevance: a cached match is not a ranked one.
        Assert.Equal(0, hit.Score);
    }

    /// <summary>With no cache to fall back to, the failure is reported rather than hidden.</summary>
    [Fact]
    public async Task WithoutACache_AnUnreachableServerIsReported()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("Unreachable.") };

        SearchViewModel viewModel = new(api) { Query = "Klein" };
        await viewModel.SearchAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal(SearchSource.Server, viewModel.Source);
    }
}

/// <summary>Saved views: the user's own, guarded by the version they were shown.</summary>
public sealed class SavedViewsViewModelTests
{
    [Fact]
    public async Task Load_ReportsEmptyWhenThereAreNone()
    {
        SavedViewsViewModel viewModel = new(new FakeAgencyOsApi());
        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Create_AddsAndSelectsTheView()
    {
        SavedViewsViewModel viewModel = new(new FakeAgencyOsApi());

        await viewModel.CreateAsync("Priority contacts", Definition("People"));

        Assert.Single(viewModel.Views);
        Assert.Equal("Priority contacts", viewModel.Selected?.Name);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Update_SendsTheVersionTheUserWasShown()
    {
        FakeAgencyOsApi api = new();
        SavedViewsViewModel viewModel = new(api);

        await viewModel.CreateAsync("Priority contacts", Definition("People"));

        SavedViewResponse original = viewModel.Views[0];

        await viewModel.UpdateAsync(original, "Key contacts", Definition("People"));

        Assert.Equal("Key contacts", viewModel.Views[0].Name);
        Assert.Equal(original.Version + 1, viewModel.Views[0].Version);
    }

    /// <summary>
    /// Editing from a stale copy is refused, not silently applied.
    /// </summary>
    /// <remarks>
    /// The user is holding version 1; the view has since become version 2. Sending
    /// 1 is exactly what makes the server able to refuse, and refusing is what
    /// stops the older definition winning by arriving last.
    /// </remarks>
    [Fact]
    public async Task Update_FromAStaleCopy_IsRefused()
    {
        FakeAgencyOsApi api = new();
        SavedViewsViewModel viewModel = new(api);

        await viewModel.CreateAsync("Priority contacts", Definition("People"));

        SavedViewResponse stale = viewModel.Views[0];

        // Somebody else edits it first.
        await viewModel.UpdateAsync(stale, "Renamed elsewhere", Definition("People"));

        // This caller still holds the original.
        await viewModel.UpdateAsync(stale, "Renamed here", Definition("People"));

        Assert.True(viewModel.HasError);
        Assert.Equal("Renamed elsewhere", viewModel.Views[0].Name);
    }

    [Fact]
    public async Task Delete_RemovesTheViewAndClearsTheSelection()
    {
        SavedViewsViewModel viewModel = new(new FakeAgencyOsApi());

        await viewModel.CreateAsync("Priority contacts", Definition("People"));

        await viewModel.DeleteAsync(viewModel.Views[0]);

        Assert.Empty(viewModel.Views);
        Assert.Null(viewModel.Selected);
        Assert.True(viewModel.IsEmpty);
    }

    /// <summary>Running a view surfaces its rows, flattened for one list.</summary>
    [Fact]
    public async Task Run_ShowsWhatTheViewReturned()
    {
        FakeAgencyOsApi api = new();

        api.People.Add(new PersonSummaryResponse(
            Guid.NewGuid(), "Rosalind Achebe", "Literary Agent", null, null, "Active", null, null,
            DateTimeOffset.UtcNow, 1));

        SavedViewsViewModel viewModel = new(api);

        await viewModel.CreateAsync("Agents", Definition("People"));
        await viewModel.RunAsync(viewModel.Views[0]);

        SavedViewRow row = Assert.Single(viewModel.Results);

        Assert.Equal("Rosalind Achebe", row.Title);
        Assert.Equal("Person", row.Kind);
        Assert.True(viewModel.HasRun);
        Assert.False(viewModel.HasNoResults);
    }

    /// <summary>
    /// A view that matches nothing is a correct answer, not an empty screen.
    /// </summary>
    /// <remarks>
    /// Distinct from having no saved views at all, which is what
    /// <c>IsEmpty</c> reports. Conflating the two would tell a user their view was
    /// missing when it simply found nothing.
    /// </remarks>
    [Fact]
    public async Task Run_WithNoMatches_SaysSoWithoutClaimingThereAreNoViews()
    {
        SavedViewsViewModel viewModel = new(new FakeAgencyOsApi());

        await viewModel.CreateAsync("Agents", Definition("People"));
        await viewModel.RunAsync(viewModel.Views[0]);

        Assert.Empty(viewModel.Results);
        Assert.True(viewModel.HasNoResults);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    private static SavedViewDefinitionModel Definition(string target) =>
        new(1, target, new SavedViewFiltersModel(Status: "Active"));
}

/// <summary>
/// The offline surface: it must state where the client stands rather than imply it.
/// </summary>
public sealed class SyncStatusViewModelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agencyos-status-vm", Guid.NewGuid().ToString("N"));

    private static readonly ExplicitCacheKeyProvider Key =
        new("99887766554433221100998877665544332211009988776655443322110099887");

    private readonly LocalCache _cache;

    public SyncStatusViewModelTests()
    {
        _cache = LocalCache.Open(
            _root,
            new LocalCacheIdentity("LAB", Guid.NewGuid(), "agent@example.invalid"),
            new ExplicitCacheKeyProvider("9988776655443322110099887766554433221100998877665544332211009988"));
    }

    public void Dispose()
    {
        _cache.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
    }

    [Fact]
    public async Task AfterASuccessfulSync_TheStatusLineSaysOnlineAndCurrent()
    {
        FakeAgencyOsApi api = new();
        SyncStatusViewModel viewModel = new(new SyncEngine(api, _cache), _cache);

        await viewModel.SynchronizeAsync();

        Assert.Equal(ConnectionState.Online, viewModel.Connection);
        Assert.False(viewModel.IsOffline);
        Assert.Contains("Online", viewModel.StatusLine, StringComparison.Ordinal);
        Assert.Contains("nothing waiting to send", viewModel.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unreachable server is reported as offline, not as an error.
    /// </summary>
    /// <remarks>
    /// The queue is durable, so being offline loses nothing. Saying so plainly
    /// beats an error message the user cannot act on.
    /// </remarks>
    [Fact]
    public async Task WhenTheServerIsUnreachable_TheClientSaysItIsOffline()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("Unreachable.") };

        SyncStatusViewModel viewModel = new(new SyncEngine(api, _cache), _cache);

        await viewModel.SynchronizeAsync();

        Assert.Equal(ConnectionState.Offline, viewModel.Connection);
        Assert.True(viewModel.IsOffline);
        Assert.Contains("Offline", viewModel.StatusLine, StringComparison.Ordinal);
        Assert.Contains("never synchronized", viewModel.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>The status line names the number of unsent changes, not just that there are some.</summary>
    [Fact]
    public void QueuedChanges_AreCountedInTheStatusLine()
    {
        _cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        SyncStatusViewModel viewModel = new(new SyncEngine(new FakeAgencyOsApi(), _cache), _cache);

        Assert.Equal(1, viewModel.OutstandingCount);
        Assert.True(viewModel.HasOutstanding);
        Assert.Contains("1 change waiting to send", viewModel.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>A conflict explains both versions, because that is what the user has to decide between.</summary>
    [Fact]
    public async Task AConflict_IsPresentedAsADecisionWithBothVersions()
    {
        FakeAgencyOsApi api = new();

        api.Failures.Enqueue(new AgencyOsApiException(
            HttpStatusCode.Conflict,
            "Version conflict",
            "The record changed.",
            "version_conflict",
            expectedVersion: 3,
            actualVersion: 7));

        _cache.Enqueue(
            QueuedOperation.UpdatePerson,
            "Rename Sarah Klein",
            new UpdatePersonRequest("Sarah", 3),
            DateTimeOffset.UtcNow,
            targetId: Guid.NewGuid(),
            expectedVersion: 3);

        SyncStatusViewModel viewModel = new(new SyncEngine(api, _cache), _cache);

        await viewModel.SynchronizeAsync();

        PendingChangeItem item = Assert.Single(viewModel.Pending);

        Assert.True(viewModel.HasConflicts);
        Assert.True(item.NeedsDecision);
        Assert.Contains("version 3", item.Explanation, StringComparison.Ordinal);
        Assert.Contains("version 7", item.Explanation, StringComparison.Ordinal);
    }

    /// <summary>Discarding is only ever at the user's request.</summary>
    [Fact]
    public void Discard_RemovesTheQueuedChange()
    {
        QueuedCommand queued = _cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        SyncStatusViewModel viewModel = new(new SyncEngine(new FakeAgencyOsApi(), _cache), _cache);

        viewModel.Discard(queued.Id);

        Assert.Empty(viewModel.Pending);
        Assert.Equal(0, viewModel.OutstandingCount);
    }
}

/// <summary>The palette lists the M3 commands, and only implemented ones.</summary>
public sealed class M3PaletteTests
{
    [Theory]
    [InlineData("search.open")]
    [InlineData("go.saved-views")]
    [InlineData("go.sync")]
    [InlineData("sync.now")]
    [InlineData("cache.reset")]
    [InlineData("view.save")]
    public void Palette_OffersTheM3Commands(string commandId)
    {
        Assert.Contains(CommandPaletteViewModel.DefaultCommands(), c => c.Id == commandId);
    }

    [Fact]
    public void SearchCommand_IsBoundToControlK()
    {
        Assert.Equal(
            "Ctrl+K",
            CommandPaletteViewModel.DefaultCommands().Single(c => c.Id == "search.open").Shortcut);
    }
}
