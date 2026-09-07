using System.Reflection;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Releases;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using AgencyOS.Contracts.Sync;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// An in-memory API, so view models can be tested without a server.
/// </summary>
/// <remarks>
/// The client is a real seam rather than a mock-shaped one: everything the Windows
/// UI does goes through <see cref="IAgencyOsApi"/>, which is why substituting it
/// exercises the actual workflow.
/// </remarks>
internal sealed class FakeAgencyOsApi : IAgencyOsApi
{
    public List<PersonSummaryResponse> People { get; } = [];

    public List<CompanySummaryResponse> Companies { get; } = [];

    public CommandCenterResponse CommandCenter { get; set; } =
        new([], [], [], [], 0, 0, 0);

    public RecordInteractionRequest? LastInteraction { get; private set; }

    public Exception? NextFailure { get; set; }

    public int CompletedTasks { get; private set; }

    /// <summary>Search results the fake returns, in the order given.</summary>
    public List<SearchHit> SearchHits { get; } = [];

    public List<SavedViewResponse> SavedViews { get; } = [];

    /// <summary>Change-feed pages the fake serves, one per call.</summary>
    public Queue<SyncChangesResponse> SyncPages { get; } = [];

    /// <summary>Idempotency keys presented on every mutating call, in order.</summary>
    public List<string?> IdempotencyKeys { get; } = [];

    /// <summary>Failures queued per call, so a retry can be made to behave differently.</summary>
    public Queue<Exception> Failures { get; } = [];

    /// <summary>Mutating calls that actually reached the fake, by idempotency key.</summary>
    public Dictionary<string, int> Effects { get; } = [];

    public Task<HandshakeResponse> HandshakeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PersonSummaryResponse>> ListPeopleAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        IEnumerable<PersonSummaryResponse> matches = string.IsNullOrWhiteSpace(search)
            ? People
            : People.Where(p => p.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<PersonSummaryResponse>>([.. matches]);
    }

    public Task<PersonDetailResponse> GetPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Throw();

        PersonSummaryResponse summary = People.First(p => p.Id == personId);

        return Task.FromResult(new PersonDetailResponse(
            summary, summary.DisplayName, null, null, null, null, DateTimeOffset.UtcNow, []));
    }

    public Task<PersonDetailResponse> CreatePersonAsync(
        CreatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        PersonSummaryResponse summary = new(
            Guid.NewGuid(),
            request.DisplayName ?? request.FirstName,
            request.Title,
            request.Email,
            request.Phone,
            "Active",
            request.PrimaryCompanyId,
            null,
            DateTimeOffset.UtcNow,
            1);

        People.Add(summary);

        return Task.FromResult(new PersonDetailResponse(
            summary, request.FirstName, null, request.LastName, null, request.Notes, DateTimeOffset.UtcNow, []));
    }

    public Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        PersonSummaryResponse summary = new(
            personId,
            request.DisplayName ?? request.FirstName,
            request.Title,
            request.Email,
            request.Phone,
            "Active",
            request.PrimaryCompanyId,
            null,
            DateTimeOffset.UtcNow,
            request.ExpectedVersion + 1);

        return Task.FromResult(new PersonDetailResponse(
            summary, request.FirstName, null, request.LastName, null, request.Notes, DateTimeOffset.UtcNow, []));
    }

    public Task<IReadOnlyList<TimelineEntryResponse>> GetPersonTimelineAsync(
        Guid personId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimelineEntryResponse>>([]);

    public Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<CompanySummaryResponse>>([.. Companies]);
    }

    public Task<CompanyDetailResponse> GetCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CompanyDetailResponse> CreateCompanyAsync(
        CreateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        CompanySummaryResponse summary = new(
            Guid.NewGuid(),
            request.Name,
            request.LegalName,
            request.Type,
            "Active",
            request.Website,
            DateTimeOffset.UtcNow,
            1);

        Companies.Add(summary);

        return Task.FromResult(new CompanyDetailResponse(summary, request.Notes, DateTimeOffset.UtcNow, [], []));
    }

    public Task<CompanyDetailResponse> UpdateCompanyAsync(
        Guid companyId,
        UpdateCompanyRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);

        CompanySummaryResponse summary = new(
            companyId,
            request.Name,
            request.LegalName,
            request.Type,
            "Active",
            request.Website,
            DateTimeOffset.UtcNow,
            request.ExpectedVersion + 1);

        return Task.FromResult(new CompanyDetailResponse(summary, request.Notes, DateTimeOffset.UtcNow, [], []));
    }

    public Task<IReadOnlyList<TimelineEntryResponse>> GetCompanyTimelineAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimelineEntryResponse>>([]);

    public Task<Guid> CreateRelationshipAsync(
        CreateRelationshipRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());

    public Task EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<RecordInteractionResponse> RecordInteractionAsync(
        RecordInteractionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        LastInteraction = request;

        return Task.FromResult(new RecordInteractionResponse(
            Guid.NewGuid(),
            request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskResponse>>([]);

    public Task<Guid> CreateTaskAsync(
        CreateTaskRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task CompleteTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        CompletedTasks++;
        return Task.CompletedTask;
    }

    public Task ReopenTaskAsync(
        Guid taskId,
        TaskTransitionRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Submit(idempotencyKey);
        return Task.CompletedTask;
    }

    public Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(CommandCenter);
    }

    // ---- Search, saved views and synchronization (M3) ----

    public Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string>? types = null,
        bool includeArchived = false,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(new SearchResponse(query, [.. SearchHits], skip, take, HasMore: false));
    }

    public Task<IReadOnlyList<SavedViewResponse>> ListSavedViewsAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult<IReadOnlyList<SavedViewResponse>>([.. SavedViews]);
    }

    public Task<SavedViewResponse> CreateSavedViewAsync(
        CreateSavedViewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        SavedViewResponse created = new(
            Guid.NewGuid(),
            request.Name,
            request.Definition.Target,
            request.Definition,
            request.Definition.DefinitionVersion,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        SavedViews.Add(created);

        return Task.FromResult(created);
    }

    public Task<SavedViewResponse> UpdateSavedViewAsync(
        Guid savedViewId,
        UpdateSavedViewRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw();

        int index = SavedViews.FindIndex(x => x.Id == savedViewId);
        SavedViewResponse existing = SavedViews[index];

        if (existing.Version != request.ExpectedVersion)
        {
            throw new AgencyOsApiException(
                System.Net.HttpStatusCode.Conflict,
                "Version conflict",
                "The saved view changed.",
                "version_conflict",
                request.ExpectedVersion,
                existing.Version);
        }

        SavedViewResponse updated = existing with
        {
            Name = request.Name,
            Definition = request.Definition,
            Target = request.Definition.Target,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        SavedViews[index] = updated;

        return Task.FromResult(updated);
    }

    public Task DeleteSavedViewAsync(Guid savedViewId, CancellationToken cancellationToken = default)
    {
        Throw();
        SavedViews.RemoveAll(x => x.Id == savedViewId);
        return Task.CompletedTask;
    }

    public Task<SavedViewResultsResponse> RunSavedViewAsync(
        Guid savedViewId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        SavedViewResponse view = SavedViews.Single(x => x.Id == savedViewId);

        return Task.FromResult(new SavedViewResultsResponse(view.Target, [.. People], [.. Companies], []));
    }

    public Task<SyncChangesResponse> ReadSyncChangesAsync(
        long cursor,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        Throw();

        if (SyncPages.Count == 0)
        {
            return Task.FromResult(new SyncChangesResponse(cursor, false, [], [], [], []));
        }

        return Task.FromResult(SyncPages.Dequeue());
    }

    /// <summary>
    /// Records a mutating call and applies the next queued failure, if any.
    /// </summary>
    /// <remarks>
    /// Effects are counted per idempotency key, which is what lets a test assert
    /// the property the model checks: a command retried after a lost response
    /// takes effect once, not twice.
    /// </remarks>
    private void Submit(string? idempotencyKey)
    {
        IdempotencyKeys.Add(idempotencyKey);

        if (Failures.Count > 0)
        {
            throw Failures.Dequeue();
        }

        Throw();

        if (idempotencyKey is not null)
        {
            Effects[idempotencyKey] = Effects.GetValueOrDefault(idempotencyKey) + 1;
        }
    }

    private void Throw()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }
}

/// <summary>Loading, empty and error states are distinct and observable.</summary>
public sealed class ViewModelStateTests
{
    [Fact]
    public async Task PeopleList_LoadsAndReportsNotEmpty()
    {
        FakeAgencyOsApi api = new();
        api.People.Add(Person("Sarah Okonkwo"));

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.People);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    /// A successful load with no rows is not an error, and must not look like one.
    /// </summary>
    [Fact]
    public async Task PeopleList_ReportsEmptyWhenThereIsNothingToShow()
    {
        PeopleListViewModel viewModel = new(new FakeAgencyOsApi());
        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>A refusal surfaces the server's own words, not an invented message.</summary>
    [Fact]
    public async Task PeopleList_SurfacesTheServersExplanation()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'people.read' is required."),
        };

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("Permission 'people.read' is required.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>An out-of-date build is explained as such rather than as a failure.</summary>
    [Fact]
    public async Task ViewModel_ExplainsWhenTheBuildMustBeUpdated()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.UpgradeRequired,
                "Client update required",
                "Version 0.1.0 is below the minimum supported version 0.3.0."),
        };

        PeopleListViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Contains("must be updated", viewModel.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandCenter_ProjectsTheServersBuckets()
    {
        FakeAgencyOsApi api = new()
        {
            CommandCenter = new CommandCenterResponse(
                [Task("Overdue thing")],
                [Task("Soon thing")],
                [Task("Someday thing")],
                [],
                3,
                12,
                4),
        };

        CommandCenterViewModel viewModel = new(api);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Overdue);
        Assert.Single(viewModel.DueSoon);
        Assert.Single(viewModel.Unscheduled);
        Assert.Equal(3, viewModel.OpenTaskCount);
        Assert.Equal(12, viewModel.PeopleCount);
        Assert.Equal(4, viewModel.CompanyCount);
        Assert.False(viewModel.IsEmpty);
    }

    private static PersonSummaryResponse Person(string name) =>
        new(Guid.NewGuid(), name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow, 1);

    private static TaskResponse Task(string title) =>
        new(Guid.NewGuid(), title, "Open", "Normal", null, null, null, DateTimeOffset.UtcNow, null, 1);
}

/// <summary>The interaction-plus-follow-up capture workflow.</summary>
public sealed class RecordInteractionViewModelTests
{
    [Fact]
    public void CannotSubmit_WithoutASummaryOrAParticipant()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi());

        Assert.False(viewModel.CanSubmit);

        viewModel.Summary = "Met Sarah at dinner";
        Assert.False(viewModel.CanSubmit);

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));
        Assert.True(viewModel.CanSubmit);
    }

    /// <summary>Asking for a follow-up without saying what it is cannot be submitted.</summary>
    [Fact]
    public void CannotSubmit_WithAFollowUpThatHasNoTitle()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi())
        {
            Summary = "Met Sarah at dinner",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));
        viewModel.CreateFollowUp = true;

        Assert.False(viewModel.CanSubmit);

        viewModel.FollowUpTitle = "Send Sarah the screenplay";
        Assert.True(viewModel.CanSubmit);
    }

    [Fact]
    public void AddParticipant_IgnoresDuplicates()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi());
        PartyRefRequest sarah = new("Person", Guid.NewGuid());

        viewModel.AddParticipant(sarah);
        viewModel.AddParticipant(new PartyRefRequest("Person", sarah.Id));

        Assert.Single(viewModel.Participants);
    }

    /// <summary>
    /// The whole point of the screen: one submission carries both the memory and
    /// the commitment.
    /// </summary>
    [Fact]
    public async Task Submit_SendsTheInteractionAndItsFollowUpTogether()
    {
        FakeAgencyOsApi api = new();
        Guid sarah = Guid.NewGuid();

        RecordInteractionViewModel viewModel = new(api)
        {
            InteractionType = "Meeting",
            Summary = "Met Sarah at dinner",
            CreateFollowUp = true,
            FollowUpTitle = "Send Sarah the screenplay Monday",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", sarah));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.FollowUpTaskId);

        RecordInteractionRequest sent = api.LastInteraction!;
        Assert.Equal("Meeting", sent.Type);
        Assert.Equal("Met Sarah at dinner", sent.Summary);
        Assert.Single(sent.Participants);
        Assert.Equal(sarah, sent.Participants[0].Party.Id);
        Assert.Equal("Send Sarah the screenplay Monday", sent.FollowUp!.Title);
    }

    [Fact]
    public async Task Submit_OmitsTheFollowUpWhenNoneWasAskedFor()
    {
        FakeAgencyOsApi api = new();

        RecordInteractionViewModel viewModel = new(api) { Summary = "Call with studio executive" };
        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.NotNull(result);
        Assert.Null(result.FollowUpTaskId);
        Assert.Null(api.LastInteraction!.FollowUp);
    }

    [Fact]
    public async Task Submit_ReportsFailureWithoutThrowing()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'interactions.record' is required."),
        };

        RecordInteractionViewModel viewModel = new(api) { Summary = "Met Sarah" };
        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        RecordInteractionResponse? result = await viewModel.SubmitAsync();

        Assert.Null(result);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public void Reset_ClearsTheFormForTheNextCapture()
    {
        RecordInteractionViewModel viewModel = new(new FakeAgencyOsApi())
        {
            Summary = "Met Sarah",
            CreateFollowUp = true,
            FollowUpTitle = "Send screenplay",
        };

        viewModel.AddParticipant(new PartyRefRequest("Person", Guid.NewGuid()));

        viewModel.Reset();

        Assert.Empty(viewModel.Summary);
        Assert.Empty(viewModel.Participants);
        Assert.False(viewModel.CreateFollowUp);
        Assert.False(viewModel.CanSubmit);
    }
}

/// <summary>The keyboard-first command palette.</summary>
public sealed class CommandPaletteTests
{
    [Fact]
    public void OffersEveryImplementedCommandByDefault()
    {
        CommandPaletteViewModel palette = new();

        Assert.NotEmpty(palette.Results);
        Assert.Equal(palette.AllCommands.Count, palette.Results.Count);
    }

    [Fact]
    public void FiltersOnTitleCategoryAndIdentifier()
    {
        CommandPaletteViewModel palette = new();

        palette.Query = "interaction";
        Assert.Contains(palette.Results, c => c.Id == "interaction.record");

        palette.Query = "Navigate";
        Assert.All(palette.Results, c => Assert.Equal("Navigate", c.Category));

        palette.Query = "person.create";
        Assert.Single(palette.Results);
    }

    [Fact]
    public void ReportsEmptyWhenNothingMatches()
    {
        CommandPaletteViewModel palette = new();
        palette.Query = "zzzz-not-a-command";

        Assert.Empty(palette.Results);
        Assert.True(palette.IsEmpty);
        Assert.Null(palette.Selected);
    }

    /// <summary>
    /// Selection wraps. On a keyboard surface, an arrow key that stops responding
    /// at the end of a short list reads as a bug.
    /// </summary>
    [Fact]
    public void SelectionWrapsAtBothEnds()
    {
        CommandPaletteViewModel palette = new(
        [
            new PaletteCommand("a", "Alpha", "Test"),
            new PaletteCommand("b", "Beta", "Test"),
        ]);

        Assert.Equal("a", palette.Selected!.Id);

        palette.MoveSelection(1);
        Assert.Equal("b", palette.Selected!.Id);

        palette.MoveSelection(1);
        Assert.Equal("a", palette.Selected!.Id);

        palette.MoveSelection(-1);
        Assert.Equal("b", palette.Selected!.Id);
    }
}

/// <summary>
/// The client's dependency boundary.
/// </summary>
/// <remarks>
/// M0 established that the Windows client cannot depend on persistence. This keeps
/// that true as the client grows: it is a compile-time fact, and this test makes it
/// a failing build rather than a discovery.
/// </remarks>
public sealed class ClientBoundaryTests
{
    [Theory]
    [InlineData("AgencyOS.Domain")]
    [InlineData("AgencyOS.Application")]
    [InlineData("AgencyOS.Infrastructure")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    public void ClientDoesNotReferenceServerOrPersistenceAssemblies(string assemblyName)
    {
        IEnumerable<string?> referenced = typeof(AgencyOsApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain(assemblyName, referenced);
    }

    /// <summary>
    /// The offline queue may only carry reversible, low-risk commands.
    /// </summary>
    /// <remarks>
    /// The allow-list is closed by construction, and this pins it. Queueing a
    /// privileged operation would mean deciding offline that it is permitted;
    /// possession of a cached record is not permission to change anything, and the
    /// server re-authorizes every queued command when it finally runs. Nothing
    /// touching authorization, release policy, bootstrap, deletion, deals or money
    /// belongs here - adding one should require editing this test and saying why.
    /// </remarks>
    [Fact]
    public void TheOfflineQueueCarriesOnlyReversibleLowRiskCommands()
    {
        string[] permitted =
        [
            nameof(QueuedOperation.CreatePerson),
            nameof(QueuedOperation.UpdatePerson),
            nameof(QueuedOperation.CreateCompany),
            nameof(QueuedOperation.UpdateCompany),
            nameof(QueuedOperation.CreateTask),
            nameof(QueuedOperation.CompleteTask),
            nameof(QueuedOperation.ReopenTask),
            nameof(QueuedOperation.RecordInteraction),
        ];

        Assert.Equal(permitted.Order(), Enum.GetNames<QueuedOperation>().Order());
    }

    /// <summary>
    /// The client cannot reach the audit trail, so it cannot record a cache
    /// operation as a business event.
    /// </summary>
    /// <remarks>
    /// Reading a record into a local cache is not a consequential business fact,
    /// and recording it would dilute the trail that matters. The commands the queue
    /// submits are audited by the server when they actually run. This is a
    /// compile-time fact rather than a convention.
    /// </remarks>
    [Fact]
    public void ClientCannotReachTheAuditTrail()
    {
        Assert.DoesNotContain(
            typeof(AgencyOS.Client.Cache.LocalCache).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "AgencyOS.Domain" || reference.Name == "AgencyOS.Application");
    }

    [Fact]
    public void ClientDependsOnTheVersionedContracts()
    {
        IEnumerable<string?> referenced = typeof(AgencyOsApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.Contains("AgencyOS.Contracts", referenced);
    }
}
