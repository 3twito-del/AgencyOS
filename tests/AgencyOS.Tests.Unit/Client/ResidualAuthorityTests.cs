using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.PeopleSlice;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The last of F-01: counters that stated a population they had not counted.
/// </summary>
/// <remarks>
/// <para>
/// Waves 1 and 2 took the money totals and the deadline counts. What was left was
/// the plainer band — how many documents, how many projects, how many clients, how
/// many messages, and the three numbers across the top of the Command Center. The
/// consequence is milder than a wrong balance and the mechanism is identical: a
/// failed load leaves an empty collection, and a sentence built from it says
/// <c>0</c> with no idea whether that is true.
/// </para>
/// <para>
/// These drive the <em>real</em> view models through failure, empty, data and
/// retry and compose each page's operator-visible outputs together, because a
/// formatter tested alone cannot show that a screen contradicts itself. Three
/// mechanisms are represented rather than all twelve sites: a list population with
/// a loaded flag, a projection whose own arrival is the flag, and the desk panel
/// whose counts and whose sentence answer the same question.
/// </para>
/// </remarks>
public sealed class ResidualAuthorityTests
{
    private static DocumentSummaryResponse Document(bool holdsContent = true) =>
        new(Guid.NewGuid(), "Engagement letter", "Agreement", "Final", "Ordinary", "DOC-1",
            null, 1, 0, holdsContent, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Review Owner", 1);

    /// <summary>What a list page shows at once, as the page composes it.</summary>
    private sealed record Surface(bool ErrorShown, string Summary, bool EmptyShown, int Rows)
    {
        /// <summary>Whether the screen asserts a population while also disclaiming it.</summary>
        public bool Contradicts =>
            ErrorShown
            && (Summary.Contains("document", StringComparison.Ordinal) || EmptyShown);
    }

    /// <summary>Exactly what <c>DocumentsPage.Render</c> writes.</summary>
    private static Surface Read(DocumentListViewModel model) =>
        new(
            model.HasError,
            SummaryAuthority.Of(
                () => $"{model.Documents.Count} documents   {model.WithContent} with the file held",
                model),
            SummaryAuthority.Knows(model) && model.IsEmpty,
            model.Documents.Count);

    // ------------------------------------------------------- list population

    /// <summary>A failed load states no count, and no zero.</summary>
    [Fact]
    public async Task AFailedListLoadStatesNoCountAndNoZero()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        DocumentListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.True(surface.ErrorShown);
        Assert.Equal(SummaryAuthority.Unavailable, surface.Summary);
        Assert.DoesNotContain("0", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    /// <summary>A successful empty load is entitled to its zero, and to its notice.</summary>
    [Fact]
    public async Task AnEmptyShelfIsAllowedToSayZero()
    {
        FakeAgencyOsApi api = new();
        DocumentListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.False(surface.ErrorShown);
        Assert.Contains("0 documents", surface.Summary, StringComparison.Ordinal);
        Assert.True(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    /// <summary>A successful load counts what it loaded.</summary>
    [Fact]
    public async Task ALoadedShelfCountsWhatItLoaded()
    {
        FakeAgencyOsApi api = new();
        api.Documents.Add(Document());
        api.Documents.Add(Document(holdsContent: false));

        DocumentListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.Contains("2 documents", surface.Summary, StringComparison.Ordinal);
        Assert.Contains("1 with the file held", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.EmptyShown);
        Assert.Equal(2, surface.Rows);
    }

    /// <summary>
    /// A load that succeeded and then failed keeps its rows and drops its claim.
    /// </summary>
    /// <remarks>
    /// The case waves 1 and 2 established and this one must not regress. The rows
    /// were true when they arrived and throwing them away helps nobody, but the
    /// count stops describing the shelf, because this load did not find out what is
    /// on it.
    /// </remarks>
    [Fact]
    public async Task AStaleShelfKeepsItsRowsAndDropsItsCount()
    {
        FakeAgencyOsApi api = new();
        api.Documents.Add(Document());

        DocumentListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Contains("1 documents", Read(model).Summary, StringComparison.Ordinal);

        api.NextFailure = new HttpRequestException("network down");
        await model.LoadAsync();

        Surface stale = Read(model);

        Assert.True(stale.ErrorShown);
        Assert.Equal(1, stale.Rows);
        Assert.Equal(SummaryAuthority.Unavailable, stale.Summary);
        Assert.False(stale.Contradicts);
    }

    /// <summary>A retry that succeeds restores the count.</summary>
    [Fact]
    public async Task ARetryRestoresTheCount()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        DocumentListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Equal(SummaryAuthority.Unavailable, Read(model).Summary);

        api.Documents.Add(Document());
        await model.LoadAsync();

        Surface recovered = Read(model);

        Assert.False(recovered.ErrorShown);
        Assert.Contains("1 documents", recovered.Summary, StringComparison.Ordinal);
    }

    // ------------------------------------------------- command centre headline

    /// <summary>Exactly what <c>CommandCenterPage.Headline</c> writes.</summary>
    private static (string Seen, string Spoken) Headline(CommandCenterViewModel model) =>
        (SummaryAuthority.Figure(() => model.OpenTaskCount, model),
         SummaryAuthority.Spoken("open tasks", () => model.OpenTaskCount, model));

    /// <summary>
    /// A headline that does not know says so, in the slot and out loud.
    /// </summary>
    /// <remarks>
    /// The view model returned <c>_view?.OpenTaskCount ?? 0</c>, so a workspace that
    /// failed to load announced a confident <c>0</c> open tasks, <c>0</c> people and
    /// <c>0</c> companies across the top of the screen. The dash is the slot's own
    /// way of writing "no value"; the announcement has to say it in words, because a
    /// reader given a dash hears punctuation or nothing.
    /// </remarks>
    [Fact]
    public async Task AFailedCommandCentreShowsNoFigureAndSaysWhy()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        CommandCenterViewModel model = new(api);

        await model.LoadAsync();

        (string seen, string spoken) = Headline(model);

        Assert.Equal(SummaryAuthority.NoFigure, seen);
        Assert.Equal("open tasks unavailable", spoken);
        Assert.DoesNotContain("0", seen, StringComparison.Ordinal);
        Assert.DoesNotContain("0", spoken, StringComparison.Ordinal);
    }

    /// <summary>A loaded headline shows its figure and announces it with its caption.</summary>
    [Fact]
    public async Task ALoadedCommandCentreShowsItsFigure()
    {
        FakeAgencyOsApi api = new();
        api.CommandCenter = new CommandCenterResponse([], [], [], [], 46, 12, 5);

        CommandCenterViewModel model = new(api);

        await model.LoadAsync();

        (string seen, string spoken) = Headline(model);

        Assert.Equal("46", seen);
        Assert.Equal("46 open tasks", spoken);
    }

    /// <summary>
    /// A tenant with nothing open is entitled to say zero.
    /// </summary>
    /// <remarks>
    /// The distinction the whole family turns on. This zero is a business result
    /// and it reads as one; the zero in the failed case was an artefact of an empty
    /// field and could not be told apart from it before.
    /// </remarks>
    [Fact]
    public async Task AnEmptyCommandCentreIsAllowedToSayZero()
    {
        FakeAgencyOsApi api = new();
        api.CommandCenter = new CommandCenterResponse([], [], [], [], 0, 0, 0);

        CommandCenterViewModel model = new(api);

        await model.LoadAsync();

        (string seen, string spoken) = Headline(model);

        Assert.Equal("0", seen);
        Assert.Equal("0 open tasks", spoken);
    }

    /// <summary>A headline that has never been asked shows no figure.</summary>
    [Fact]
    public void AnUnaskedHeadlineShowsNoFigure()
    {
        CommandCenterViewModel model = new(new FakeAgencyOsApi());

        Assert.Equal(SummaryAuthority.NoFigure, SummaryAuthority.Figure(() => 7, model));
        Assert.Equal("people unavailable", SummaryAuthority.Spoken("people", () => 7, model));
    }

    // --------------------------------------------------------- desk agreement

    /// <summary>
    /// The desk counts and the desk sentence never disagree.
    /// </summary>
    /// <remarks>
    /// Wave 2 gated the sentence and left the three counts beside it ungated, so
    /// the same panel could read <c>0 unknown outcomes   0 failed sends</c> directly
    /// above <c>Whether anything needs attention is unavailable.</c> Both halves
    /// answer the same question from the same projection, and they now wait on the
    /// same authority.
    /// </remarks>
    [Fact]
    public async Task TheDeskCountsAndTheDeskSentenceAgree()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        CommunicationCommandCenterViewModel model = new(api);

        await model.LoadAsync();

        string counts = SummaryAuthority.Of(
            () => $"{model.UnknownOutcomeCount} unknown outcomes   {model.FailedSendCount} failed sends",
            model);

        string sentence = !SummaryAuthority.Knows(model)
            ? "Whether anything needs attention is unavailable."
            : model.NeedsAttention
                ? "Something needs a person. See the Desk tab."
                : "Nothing needs attention.";

        bool allClearShown = SummaryAuthority.Knows(model) && !model.NeedsAttention;

        Assert.Equal(SummaryAuthority.Unavailable, counts);
        Assert.Equal("Whether anything needs attention is unavailable.", sentence);
        Assert.False(allClearShown);
        Assert.DoesNotContain("0", counts, StringComparison.Ordinal);
    }

    /// <summary>A quiet desk is entitled to say so, in both halves.</summary>
    [Fact]
    public async Task AQuietDeskSaysSoInBothHalves()
    {
        FakeAgencyOsApi api = new();
        api.CommunicationCommandCenter = new CommunicationCommandCenterResponse([], [], [], 0, 0, 0);

        CommunicationCommandCenterViewModel model = new(api);

        await model.LoadAsync();

        string counts = SummaryAuthority.Of(
            () => $"{model.UnknownOutcomeCount} unknown outcomes   {model.FailedSendCount} failed sends",
            model);

        Assert.Contains("0 unknown outcomes", counts, StringComparison.Ordinal);
        Assert.True(SummaryAuthority.Knows(model) && !model.NeedsAttention);
    }

    // ------------------------------------------------------ the primitive itself

    /// <summary>Every state a headline can be in, and what it is allowed to say.</summary>
    public static TheoryData<bool, bool, bool, string, string> HeadlineStates => new()
    {
        // loaded, loading, error, seen, spoken
        { false, false, false, SummaryAuthority.NoFigure, "people unavailable" },
        { false, true, false, SummaryAuthority.NoFigure, "people still loading" },
        { false, false, true, SummaryAuthority.NoFigure, "people unavailable" },
        { true, false, true, SummaryAuthority.NoFigure, "people unavailable" },
        { true, true, false, SummaryAuthority.NoFigure, "people still loading" },
        { true, false, false, "9", "9 people" },
    };

    [Theory]
    [MemberData(nameof(HeadlineStates))]
    public void AHeadlineSaysOnlyWhatItKnows(
        bool loaded, bool loading, bool error, string seen, string spoken)
    {
        Population population = new(loaded, loading, error);

        Assert.Equal(seen, SummaryAuthority.Figure(() => 9, population));
        Assert.Equal(spoken, SummaryAuthority.Spoken("people", () => 9, population));
    }

    /// <summary>A population in an exact state, for the matrix above.</summary>
    private sealed record Population(bool HasLoaded, bool IsLoading, bool HasError)
        : IAuthoritativePopulation;
}
