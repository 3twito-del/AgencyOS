using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>The talent and client list surface.</summary>
public sealed class TalentListViewModelTests
{
    [Fact]
    public async Task Load_ReportsEmptyWhenNobodyMatches()
    {
        TalentListViewModel viewModel = new(new FakeAgencyOsApi());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Load_CountsClientsSeparatelyFromTalent()
    {
        FakeAgencyOsApi api = new();

        api.Talent.Add(Talent("Ada Reyes", isClient: true, "Active"));
        api.Talent.Add(Talent("Bo Ferreira", isClient: false, null));

        TalentListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Talent.Count);
        Assert.Equal(1, viewModel.ClientCount);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// The two client filters are contradictory and cannot both be on.
    /// </summary>
    /// <remarks>
    /// Asking for people who are both current and former clients returns nothing,
    /// which reads as a broken screen rather than an impossible question. The view
    /// model resolves it instead of showing an empty list.
    /// </remarks>
    [Fact]
    public void TheTwoClientFilters_AreMutuallyExclusive()
    {
        TalentListViewModel viewModel = new(new FakeAgencyOsApi()) { ClientsOnly = true };

        Assert.True(viewModel.ClientsOnly);
        Assert.False(viewModel.FormerClientsOnly);

        viewModel.FormerClientsOnly = true;

        Assert.False(viewModel.ClientsOnly);
        Assert.True(viewModel.FormerClientsOnly);
    }

    [Fact]
    public async Task Filters_ReachTheServerRatherThanBeingAppliedLocally()
    {
        FakeAgencyOsApi api = new();

        api.Talent.Add(Talent("Ada Reyes", isClient: true, "Active", "Writer"));
        api.Talent.Add(Talent("Bo Ferreira", isClient: false, null, "Actor"));

        TalentListViewModel viewModel = new(api)
        {
            ClientsOnly = true,
            Discipline = "Writer",
            Search = " Ada ",
        };

        await viewModel.LoadAsync();

        Assert.Equal((true, false, "Writer", "Ada"), api.LastTalentFilter);
        Assert.Equal("Ada Reyes", Assert.Single(viewModel.Talent).DisplayName);
    }

    [Fact]
    public async Task ARefusal_SurfacesTheServersExplanation()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOS.Client.AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'talent.read' is required."),
        };

        TalentListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("Permission 'talent.read' is required.", viewModel.ErrorMessage);
    }

    private static TalentSummaryResponse Talent(
        string name,
        bool isClient,
        string? status,
        string discipline = "Actor") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            "Established",
            [discipline],
            status,
            isClient,
            null,
            null,
            isClient ? ["Acting"] : [],
            DateTimeOffset.UtcNow,
            1);
}

/// <summary>The client working surface.</summary>
public sealed class ClientOverviewViewModelTests
{
    [Fact]
    public async Task Load_StatesWhereTheRelationshipStands()
    {
        FakeAgencyOsApi api = new();
        Guid personId = Guid.NewGuid();

        api.Overview = Overview(personId, "Ada Reyes", isClient: true, "Active", "Marcus Reid");

        ClientOverviewViewModel viewModel = new(api);

        await viewModel.LoadAsync(personId);

        Assert.Equal("Ada Reyes", viewModel.DisplayName);
        Assert.True(viewModel.IsClient);

        // The status line has to be actionable, not just a status word.
        Assert.Contains("Client", viewModel.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Acting", viewModel.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Marcus Reid", viewModel.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SomebodyNotRepresented_IsDescribedAsSuch()
    {
        FakeAgencyOsApi api = new();
        Guid personId = Guid.NewGuid();

        api.Overview = Overview(personId, "Bo Ferreira", isClient: false, null, null);

        ClientOverviewViewModel viewModel = new(api);

        await viewModel.LoadAsync(personId);

        Assert.False(viewModel.IsClient);
        Assert.Contains("Not represented", viewModel.StatusLine, StringComparison.Ordinal);
        Assert.Contains("no lead assigned", viewModel.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// Withheld positioning notes are indistinguishable from absent ones.
    /// </summary>
    /// <remarks>
    /// A caller without <c>talent.notes.read</c> receives the field null. The screen
    /// must not hint that something was withheld, or the redaction leaks the fact
    /// that a note exists.
    /// </remarks>
    [Fact]
    public async Task RedactedPositioning_LooksExactlyLikeNoPositioning()
    {
        FakeAgencyOsApi api = new();
        Guid personId = Guid.NewGuid();

        api.Overview = Overview(personId, "Ada Reyes", isClient: true, "Active", "Marcus Reid");

        ClientOverviewViewModel viewModel = new(api);

        await viewModel.LoadAsync(personId);

        Assert.False(viewModel.HasPositioningNotes);
    }

    [Fact]
    public async Task Load_SurfacesCreditsMaterialsAndHistory()
    {
        FakeAgencyOsApi api = new();
        Guid personId = Guid.NewGuid();

        api.Overview = Overview(personId, "Ada Reyes", isClient: true, "Active", "Marcus Reid") with
        {
            Credits = [new CreditResponse(Guid.NewGuid(), personId, "The Long Field", null, "Writing", "Released", 2025, null, null, null, null, null, 1)],
            Materials = [new MaterialResponse(Guid.NewGuid(), personId, "Meridian", "Pilot", "Ready", "Draft 4", null, null, null, null, 1)],
            RecentHistory = [new RepresentationHistoryEntryResponse(new DateOnly(2026, 3, 1), "representation.status", "Signed", null)],
        };

        ClientOverviewViewModel viewModel = new(api);

        await viewModel.LoadAsync(personId);

        Assert.Single(viewModel.Credits);
        Assert.Single(viewModel.Materials);
        Assert.Single(viewModel.History);
        Assert.Single(viewModel.Team);
    }

    private static ClientOverviewResponse Overview(
        Guid personId,
        string name,
        bool isClient,
        string? status,
        string? leadName)
    {
        TalentSummaryResponse summary = new(
            Guid.NewGuid(),
            personId,
            name,
            "Established",
            ["Actor"],
            status,
            isClient,
            leadName is null ? null : Guid.NewGuid(),
            leadName,
            isClient ? ["Acting"] : [],
            DateTimeOffset.UtcNow,
            1);

        RepresentationResponse? representation = isClient
            ? new RepresentationResponse(
                Guid.NewGuid(),
                personId,
                name,
                "Active",
                new DateOnly(2026, 3, 1),
                null,
                true,
                "Worldwide",
                null,
                [new RepresentationScopeResponse("Acting", new DateOnly(2026, 3, 1), null)],
                [new RepresentationTeamMemberResponse(Guid.NewGuid(), leadName ?? "Lead", "Lead", new DateOnly(2026, 3, 1), null)],
                DateTimeOffset.UtcNow,
                1)
            : null;

        return new ClientOverviewResponse(
            new TalentDetailResponse(summary, "Summary.", PositioningNotes: null, "London", null, DateTimeOffset.UtcNow),
            representation,
            [],
            [],
            [],
            [],
            []);
    }
}

/// <summary>The prospect pipeline and conversion workflow.</summary>
public sealed class ProspectsViewModelTests
{
    [Fact]
    public async Task Load_ShowsOnlyLivePursuitsByDefault()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Courting"));
        api.Prospects.Add(Prospect("Bo Ferreira", "Declined"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal("Ada Reyes", Assert.Single(viewModel.Prospects).DisplayName);
    }

    [Fact]
    public async Task AClosedPursuit_CanNeitherAdvanceNorConvert()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Bo Ferreira", "Declined"));

        ProspectsViewModel viewModel = new(api) { OpenOnly = false };

        await viewModel.LoadAsync();

        viewModel.Selected = viewModel.Prospects[0];

        Assert.False(viewModel.CanAdvance);
        Assert.False(viewModel.CanConvert);
    }

    [Fact]
    public async Task Advance_SendsTheVersionTheUserWasShown()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Identified"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        ProspectResponse original = viewModel.Prospects[0];

        await viewModel.AdvanceAsync(original, "Contacted", new DateOnly(2026, 2, 3));

        Assert.Equal("Contacted", viewModel.Prospects[0].Stage);
        Assert.Equal(original.Version + 1, viewModel.Prospects[0].Version);
    }

    /// <summary>Advancing from a stale copy is refused rather than silently applied.</summary>
    [Fact]
    public async Task Advance_FromAStaleCopy_IsRefused()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Identified"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        ProspectResponse stale = viewModel.Prospects[0];

        await viewModel.AdvanceAsync(stale, "Contacted", new DateOnly(2026, 2, 3));

        // The caller still holds the original version.
        await viewModel.AdvanceAsync(stale, "Courting", new DateOnly(2026, 2, 5));

        Assert.True(viewModel.HasError);
        Assert.Equal("Contacted", viewModel.Prospects[0].Stage);
    }

    /// <summary>
    /// Conversion carries an idempotency key, so a retry does not sign twice.
    /// </summary>
    /// <remarks>
    /// The key is generated before the first attempt and reused on the retry, which
    /// is what lets the server recognize the second submission as a replay rather
    /// than a second client (ADR-0014).
    /// </remarks>
    [Fact]
    public async Task Convert_CarriesAnIdempotencyKey()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Ada Reyes", "Courting"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        RepresentationResponse? representation = await viewModel.ConvertAsync(
            viewModel.Prospects[0],
            new DateOnly(2026, 3, 1),
            Guid.NewGuid(),
            ["Acting"]);

        Assert.NotNull(representation);
        Assert.Equal("Active", representation!.Status);

        // Every mutating call the conversion made presented a key.
        Assert.All(api.IdempotencyKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    /// <summary>Follow-ups due today or earlier are what the pipeline surfaces first.</summary>
    [Fact]
    public async Task DueNow_SelectsPursuitsNeedingAttention()
    {
        FakeAgencyOsApi api = new();

        api.Prospects.Add(Prospect("Overdue", "Courting", new DateOnly(2026, 1, 1)));
        api.Prospects.Add(Prospect("Later", "Courting", new DateOnly(2026, 12, 1)));
        api.Prospects.Add(Prospect("Undated", "Courting"));

        ProspectsViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(
            "Overdue",
            Assert.Single(viewModel.DueNow(new DateOnly(2026, 6, 1))).DisplayName);
    }

    private static ProspectResponse Prospect(string name, string stage, DateOnly? followUp = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            stage,
            Guid.NewGuid(),
            "Owner",
            null,
            null,
            new DateOnly(2026, 1, 1),
            followUp,
            null,
            DateTimeOffset.UtcNow,
            1);
}

/// <summary>The palette offers the M4 commands.</summary>
public sealed class M4PaletteTests
{
    [Theory]
    [InlineData("go.talent")]
    [InlineData("go.prospects")]
    [InlineData("prospect.create")]
    [InlineData("prospect.convert")]
    [InlineData("credit.add")]
    [InlineData("material.add")]
    public void Palette_OffersTheM4Commands(string commandId)
    {
        Assert.Contains(CommandPaletteViewModel.DefaultCommands(), c => c.Id == commandId);
    }

    /// <summary>Every navigation shortcut is distinct, or one of them silently loses.</summary>
    [Fact]
    public void NavigationShortcuts_AreUnique()
    {
        string[] shortcuts =
        [
            .. CommandPaletteViewModel.DefaultCommands()
                .Where(c => c.Category == "Navigate" && c.Shortcut is not null)
                .Select(c => c.Shortcut!),
        ];

        Assert.Equal(shortcuts.Length, shortcuts.Distinct(StringComparer.Ordinal).Count());
    }
}
