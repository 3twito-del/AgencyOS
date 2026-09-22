using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A deadline count may claim nothing is late only when it counted.
/// </summary>
/// <remarks>
/// <para>
/// The four HIGH instances of F-01 all say the same kind of thing: how much work
/// is past its date, lapsing this week, or needing a person. Each was built from
/// whatever the local collection held, so a failed load said <c>0 overdue</c> and
/// an operator stopped looking for late work.
/// </para>
/// <para>
/// Wave 1 repaired the money totals. These are the operational ones, and the
/// consequence differs: money misread is a wrong figure, a deadline misread is a
/// missed date. Pipeline stands for the family here because its view model is the
/// same shape as the other two count sites; the Communications desk is tested
/// separately because its claim is a boolean, not a total.
/// </para>
/// </remarks>
public sealed class OperationalAuthorityTests
{
    private static OpportunitySummaryResponse Pursuit(string name, DateOnly? nextAction = null) =>
        new(Guid.NewGuid(), name, "TalentEngagement", "Active", "High",
            Guid.NewGuid(), "Review Owner", new DateOnly(2026, 6, 1), null, null, null,
            0, 0, 0, 0, nextAction, null, DateTimeOffset.UtcNow, 1);

    /// <summary>What the operator can see at once, as the page composes it.</summary>
    private sealed record Surface(bool ErrorShown, string Summary, bool EmptyShown, int Rows)
    {
        public bool Contradicts =>
            ErrorShown
            && (Summary.Contains("pursuit(s)", StringComparison.Ordinal)
                || Summary.Contains("overdue", StringComparison.Ordinal)
                || EmptyShown);
    }

    private static Surface Read(OpportunityListViewModel model) =>
        new(
            model.HasError,
            SummaryAuthority.Of(
                () => $"{model.Opportunities.Count} pursuit(s); {model.Overdue} overdue, "
                    + $"{model.Waiting} awaiting a reply.",
                model),
            SummaryAuthority.Knows(model) && model.IsEmpty,
            model.Opportunities.Count);

    // ------------------------------------------------------------ knowledge

    /// <summary>A load in flight counts nothing late.</summary>
    [Fact]
    public void WhileLoadingNoDeadlineCountIsStated()
    {
        Assert.Equal(
            SummaryAuthority.Loading,
            SummaryAuthority.Of(() => "0 pursuit(s); 0 overdue.", Stub(loading: true)));
    }

    /// <summary>A failed load does not report an empty diary.</summary>
    [Fact]
    public async Task AFailedLoadDoesNotSayNothingIsOverdue()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        OpportunityListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.True(surface.ErrorShown);
        Assert.Equal(SummaryAuthority.Unavailable, surface.Summary);
        Assert.DoesNotContain("overdue", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.Contradicts);
    }

    // --------------------------------------------------------------- results

    /// <summary>An empty slate that loaded is allowed to say so.</summary>
    [Fact]
    public async Task AnEmptySlateThatLoadedMaySayNothingIsOverdue()
    {
        FakeAgencyOsApi api = new();
        OpportunityListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.False(surface.ErrorShown);
        Assert.Contains("0 pursuit(s)", surface.Summary, StringComparison.Ordinal);
        Assert.Contains("0 overdue", surface.Summary, StringComparison.Ordinal);
        Assert.True(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    /// <summary>A loaded slate counts what it loaded.</summary>
    [Fact]
    public async Task ALoadedSlateCountsItsWork()
    {
        FakeAgencyOsApi api = new();
        api.Opportunities.Add(Pursuit("The Kestrel Tide - lead", new DateOnly(2020, 1, 1)));
        api.Opportunities.Add(Pursuit("Cinder And Salt - lead"));
        OpportunityListViewModel model = new(api);

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.Contains("2 pursuit(s)", surface.Summary, StringComparison.Ordinal);
        Assert.Contains("1 overdue", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.EmptyShown);
        Assert.False(surface.Contradicts);
    }

    // -------------------------------------------------------------- recovery

    /// <summary>A retry that succeeds restores the deadline count.</summary>
    [Fact]
    public async Task ARetryAfterFailureRestoresTheCount()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("network down") };
        OpportunityListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Equal(SummaryAuthority.Unavailable, Read(model).Summary);

        api.Opportunities.Add(Pursuit("The Kestrel Tide - lead", new DateOnly(2020, 1, 1)));

        await model.LoadAsync();

        Surface surface = Read(model);

        Assert.False(surface.ErrorShown);
        Assert.Contains("1 overdue", surface.Summary, StringComparison.Ordinal);
        Assert.False(surface.Contradicts);
    }

    /// <summary>
    /// Rows surviving a failed refresh are not recounted as current.
    /// </summary>
    /// <remarks>
    /// The rows stay, as they did before — they are last known, not current. What
    /// must not happen is the count restating them as though it had just looked.
    /// </remarks>
    [Fact]
    public async Task StaleRowsAfterAFailedRefreshAreNotRecounted()
    {
        FakeAgencyOsApi api = new();
        api.Opportunities.Add(Pursuit("The Kestrel Tide - lead", new DateOnly(2020, 1, 1)));
        OpportunityListViewModel model = new(api);

        await model.LoadAsync();
        Assert.Contains("1 overdue", Read(model).Summary, StringComparison.Ordinal);

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
    /// No arrangement lets the page disclaim the slate and count it.
    /// </summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    public async Task ThePageNeverDisclaimsTheSlateAndCountsIt(bool fails, int rows)
    {
        FakeAgencyOsApi api = new();

        for (int i = 0; i < rows; i++)
        {
            api.Opportunities.Add(Pursuit($"Pursuit {i}", new DateOnly(2020, 1, 1)));
        }

        OpportunityListViewModel model = new(api);
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

    // ------------------------------------------------------- the desk boolean

    /// <summary>
    /// The desk does not say nothing needs attention until it has looked.
    /// </summary>
    /// <remarks>
    /// The only one of the four that is not a total. Its own wording is used rather
    /// than "Totals unavailable.", which would answer a question nobody asked.
    /// </remarks>
    [Theory]
    [InlineData(false, false, false, "Whether anything needs attention is unavailable.")]
    [InlineData(true, true, false, "Whether anything needs attention is unavailable.")]
    [InlineData(true, false, true, "Whether anything needs attention is unavailable.")]
    [InlineData(true, false, false, "Nothing needs attention.")]
    public void TheDeskClaimsQuietOnlyWhenItHasRead(
        bool loaded, bool loading, bool failed, string expected)
    {
        IAuthoritativePopulation desk = Stub(loaded, loading, failed);

        string said = !SummaryAuthority.Knows(desk)
            ? "Whether anything needs attention is unavailable."
            : "Nothing needs attention.";

        Assert.Equal(expected, said);
    }

    private static IAuthoritativePopulation Stub(
        bool loaded = false, bool loading = false, bool failed = false) =>
        new State(loaded, loading, failed);

    private sealed record State(bool HasLoaded, bool IsLoading, bool HasError)
        : IAuthoritativePopulation;
}
