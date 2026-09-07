using System.Reflection;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Releases;
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
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<PersonDetailResponse> UpdatePersonAsync(
        Guid personId,
        UpdatePersonRequest request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
        CancellationToken cancellationToken = default)
    {
        Throw();
        LastInteraction = request;

        return Task.FromResult(new RecordInteractionResponse(
            Guid.NewGuid(),
            request.FollowUp is null ? null : Guid.NewGuid()));
    }

    public Task<IReadOnlyList<TaskResponse>> ListTasksAsync(
        bool openOnly = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskResponse>>([]);

    public Task<Guid> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Guid.NewGuid());

    public Task CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        CompletedTasks++;
        return Task.CompletedTask;
    }

    public Task ReopenTaskAsync(Guid taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CommandCenterResponse> GetCommandCenterAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        return Task.FromResult(CommandCenter);
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
        new(Guid.NewGuid(), name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow);

    private static TaskResponse Task(string title) =>
        new(Guid.NewGuid(), title, "Open", "Normal", null, null, null, DateTimeOffset.UtcNow, null);
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

    [Fact]
    public void ClientDependsOnTheVersionedContracts()
    {
        IEnumerable<string?> referenced = typeof(AgencyOsApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.Contains("AgencyOS.Contracts", referenced);
    }
}
