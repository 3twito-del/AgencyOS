using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A money summary may state a total only when it knows one.
/// </summary>
/// <remarks>
/// <para>
/// Every Finance summary was built from whatever the local collection happened to
/// hold, so a failed load printed <c>0 receivable(s) … Outstanding:</c> nothing —
/// a confident financial zero asserted from an empty list, directly beneath the
/// error bar saying the load had failed. An operator reading it concludes a client
/// owes nothing and does not chase a real debt.
/// </para>
/// <para>
/// These drive the <em>real</em> view models against a failing and then a
/// succeeding API, and compose the page's four operator-visible outputs together —
/// error, summary, empty state and rows — because a formatter tested alone cannot
/// show that a screen contradicts itself.
/// </para>
/// </remarks>
public sealed class MoneyAuthorityTests
{
    private static ReceivableResponse Receivable(decimal amount, string currency, bool overdue = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Halloway / Corvid Ash",
            Guid.NewGuid(), "Corvid Ash Pictures", "Client", Guid.NewGuid(), "Perrin Halloway",
            new MoneyResponse(amount, currency), new MoneyResponse(0m, currency),
            new MoneyResponse(0m, currency), new MoneyResponse(amount, currency),
            new DateOnly(2026, 8, 1), "Open", overdue, "CIN-0001", null, null,
            DateTimeOffset.UtcNow, 1);

    /// <summary>What the operator can see at once, as the page composes it.</summary>
    private sealed record Surface(bool ErrorShown, string Summary, bool EmptyShown, int Rows)
    {
        /// <summary>Whether the screen asserts a population while also disclaiming it.</summary>
        public bool Contradicts =>
            ErrorShown
            && (Summary.Contains("receivable(s)", StringComparison.Ordinal)
                || Summary.Contains("Outstanding", StringComparison.Ordinal)
                || EmptyShown);
    }

    /// <summary>
    /// The four things the Finance page shows at once, composed exactly as it
    /// composes them. <c>FinanceSurfaceTests</c> pins the page to this shape.
    /// </summary>
    private static Surface Read(ReceivableListViewModel model) =>
        new(
            model.HasError,
            SummaryAuthority.Of(
                () => $"{model.Receivables.Count} receivable(s). Outstanding: {model.OutstandingSummary}.",
                model),
            SummaryAuthority.Knows(model) && model.IsEmpty,
            model.Receivables.Count);

    // ------------------------------------------------------------- knowledge

    /// <summary>
    /// A load in flight states no total.
    /// </summary>
    /// <remarks>
    /// The first render always sees this state: <c>RunAsync</c> sets
    /// <c>IsLoading</c> before it awaits, and that is what raises the change
    /// notification the page renders from.
    /// </remarks>
    [Fact]
    public void WhileLoadingTheSummaryStatesNoTotal()
    {
        Assert.Equal(
            SummaryAuthority.Loading,
            SummaryAuthority.Of(() => "0 receivable(s).", Population(loading: true)));
    }

    /// <summary>A failed load states no total, and no zero.</summary>
    [Fact]
    public async Task AFailedLoadStatesNoTotalAndNoZero()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.True(surface.ErrorShown);
        Assert.Equal(SummaryAuthority.Unavailable, surface.Summary);
        Assert.DoesNotContain("0", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.Contradicts);
    }

    /// <summary>A population that was never asked for states no total.</summary>
    [Fact]
    public void AnUnaskedPopulationStatesNoTotal()
    {
        Assert.Equal(
            SummaryAuthority.Unavailable,
            SummaryAuthority.Of(() => "0 receivable(s).", Population()));
    }

    // -------------------------------------------------------------- results

    /// <summary>A successful empty load is entitled to its zero.</summary>
    [Fact]
    public async Task AnEmptyBookIsAllowedToSayZero()
    {
        FakeAgencyOsApi api = new();
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.False(surface.ErrorShown);
        Assert.Contains("0 receivable(s)", surface.Summary, StringComparison.Ordinal);
        Assert.True(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    /// <summary>A successful load with money states the money.</summary>
    [Fact]
    public async Task ALoadedBookStatesItsTotalWithCurrency()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(Receivable(240000m, "USD"));
        api.Receivables.Add(Receivable(140000m, "GBP"));
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.Contains("2 receivable(s)", surface.Summary, StringComparison.Ordinal);
        Assert.Contains("USD", surface.Summary, StringComparison.Ordinal);
        Assert.Contains("GBP", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    /// <summary>
    /// Money keeps its currency wherever a summary states an amount.
    /// </summary>
    /// <remarks>
    /// AgencyOS holds no exchange rate, so two currencies are reported side by
    /// side and never added (ADR-0023). A bare number here would be a figure that
    /// does not exist.
    /// </remarks>
    [Fact]
    public async Task EveryAmountInASummaryCarriesItsCurrency()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(Receivable(240000m, "USD"));
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        string summary = Read(model).Summary;

        Assert.Contains("240,000.00 USD", summary, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- recovery

    /// <summary>A retry that succeeds restores the total.</summary>
    [Fact]
    public async Task ARetryAfterFailureStatesTheTotal()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Equal(SummaryAuthority.Unavailable, Read(model).Summary);

        api.Receivables.Add(Receivable(240000m, "USD"));

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.False(surface.ErrorShown);
        Assert.Contains("1 receivable(s)", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.Contradicts);
    }

    /// <summary>
    /// Rows that survive a failed refresh are not presented as the current total.
    /// </summary>
    /// <remarks>
    /// <c>RunAsync</c> replaces the collection only after its await returns, so a
    /// failed refresh leaves the previous rows on screen. They are last known, not
    /// current, and the summary must not restate them as though the product had
    /// just counted them.
    /// </remarks>
    [Fact]
    public async Task StaleRowsAfterAFailedRefreshAreNotStatedAsTheTotal()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(Receivable(240000m, "USD"));
        ReceivableListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Contains("1 receivable(s)", Read(model).Summary, StringComparison.Ordinal);

        api.NextFailure = new HttpRequestException("network down");

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.True(surface.ErrorShown);
        Assert.Equal(1, surface.Rows);
        Assert.Equal(SummaryAuthority.Unavailable, surface.Summary);
        Assert.False(surface.Contradicts);
    }

    // --------------------------------------------------------- whole surface

    /// <summary>
    /// No arrangement of the page lets it disclaim a population and total it.
    /// </summary>
    /// <remarks>
    /// The invariant CLAUDE.md §7 states: a screen must not carry mutually
    /// incompatible statements about the same domain truth.
    /// </remarks>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    public async Task ThePageNeverDisclaimsAPopulationAndTotalsIt(bool fails, int rows)
    {
        FakeAgencyOsApi api = new();

        for (int i = 0; i < rows; i++)
        {
            api.Receivables.Add(Receivable(1000m * (i + 1), "USD"));
        }

        ReceivableListViewModel model = new(api);
        await model.LoadAsync();

        if (fails)
        {
            api.NextFailure = new HttpRequestException("network down");
            await model.LoadAsync();
        }

        Surface surface = Read(model);

        Assert.False(
            surface.Contradicts,
            $"error={surface.ErrorShown} empty={surface.EmptyShown} summary='{surface.Summary}'");
    }

    /// <summary>
    /// A summary spanning two populations waits for both.
    /// </summary>
    /// <remarks>
    /// The page-wide line adds receivables to payments. One of each arriving is not
    /// enough: an outstanding balance built from loaded receivables and failed
    /// payments is as wrong as one built from neither.
    /// </remarks>
    [Fact]
    public void ASummaryOverTwoPopulationsIsAuthoritativeOnlyWhenBothAre()
    {
        Assert.Equal(
            SummaryAuthority.Unavailable,
            SummaryAuthority.Of(() => "total", Population(loaded: true), Population(failed: true)));

        Assert.Equal(
            SummaryAuthority.Loading,
            SummaryAuthority.Of(() => "total", Population(loaded: true), Population(loading: true)));

        Assert.Equal(
            "total",
            SummaryAuthority.Of(() => "total", Population(loaded: true), Population(loaded: true)));
    }

    /// <summary>The sentence is never built from a population that is not there.</summary>
    [Fact]
    public void TheTotalIsNotEvenComputedWhenItCannotBeStated()
    {
        bool computed = false;

        SummaryAuthority.Of(
            () => { computed = true; return "total"; },
            Population(failed: true));

        Assert.False(computed);
    }

    private static IAuthoritativePopulation Population(
        bool loaded = false, bool loading = false, bool failed = false) =>
        new Stub(loaded, loading, failed);

    /// <summary>A population in an exact state, for the arrangements a fake API cannot reach.</summary>
    private sealed record Stub(bool HasLoaded, bool IsLoading, bool HasError)
        : IAuthoritativePopulation;
}
