using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// That a correct total above partial lists cannot be read as a complete picture.
/// </summary>
/// <remarks>
/// <para>
/// F-05. The Command Center's headline counts every open task in the agency. The
/// three lists beneath it are windows — overdue, due inside the horizon, undated
/// — which are disjoint but deliberately not exhaustive, so an open task due
/// further ahead than the horizon is counted above and listed nowhere.
/// </para>
/// <para>
/// Neither number was ever wrong, and these tests do not make them agree. What
/// was missing was any statement that they count different populations: a
/// headline of 46 above 22 rows read either as a contradiction or as the whole
/// of the work, and the build-82 blind operator took it for the second and
/// answered 2 of 5.
/// </para>
/// <para>
/// These execute the shipped sentence rather than inspecting the page, because
/// what matters is the proposition it states, not that some words are present.
/// </para>
/// </remarks>
public sealed class ScopeDisclosureTests
{
    private static readonly Population Loaded = new(HasLoaded: true, IsLoading: false, HasError: false);

    /// <summary>
    /// The adversarial case: more open work than the windows hold.
    /// </summary>
    /// <remarks>
    /// The real shape of the finding, and the one a fixture must reproduce. 46
    /// open tasks, 22 of them inside a window, 24 due further ahead than the
    /// horizon and therefore on no list.
    /// </remarks>
    [Fact]
    public void WhereTheWindowsHoldLessThanTheTotalTheRestIsAccountedFor()
    {
        string scope = WindowScope.For(() => 46, () => 22, Loaded);

        Assert.Contains("24 more open tasks", scope, StringComparison.Ordinal);
        Assert.Contains("not listed here", scope, StringComparison.Ordinal);
    }

    /// <summary>The remainder is the difference, whatever the difference is.</summary>
    /// <remarks>
    /// Distinct numbers, so a sentence that echoed either figure instead of
    /// subtracting them fails here rather than passing on the presence of a
    /// digit.
    /// </remarks>
    [Theory]
    [InlineData(46, 22, 24)]
    [InlineData(9, 1, 8)]
    [InlineData(100, 3, 97)]
    public void TheRemainderIsWhatTheWindowsDoNotHold(int total, int shown, int expected)
    {
        Assert.Contains(
            $"{expected} more open tasks",
            WindowScope.For(() => total, () => shown, Loaded),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading the headline as the row count changes what the page says.
    /// </summary>
    /// <remarks>
    /// The assertion that makes the ones above mean something. If the two
    /// populations were ever confused for one another the sentence would invert,
    /// and a page that had 22 open tasks and 46 rows is not a page this one
    /// describes.
    /// </remarks>
    [Fact]
    public void SwappingTheTotalAndTheRowsChangesWhatThePageSays()
    {
        string right = WindowScope.For(() => 46, () => 22, Loaded);
        string swapped = WindowScope.For(() => 22, () => 46, Loaded);

        Assert.NotEqual(right, swapped);
        Assert.Contains("24 more open tasks", right, StringComparison.Ordinal);
        Assert.DoesNotContain("more open tasks", swapped, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where every open task is in a window, the page says so.
    /// </summary>
    /// <remarks>
    /// A real business result, not an absence: the operator is entitled to know
    /// that scanning the lists has shown them everything, on the days when it
    /// has.
    /// </remarks>
    [Fact]
    public void WhereTheWindowsHoldEverythingThePageSaysThatToo()
    {
        string scope = WindowScope.For(() => 22, () => 22, Loaded);

        Assert.Contains("Every open task is listed here", scope, StringComparison.Ordinal);
        Assert.DoesNotContain("more open tasks", scope, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lists are always described, whatever the figures are doing.
    /// </summary>
    /// <remarks>
    /// The windows are what they are whether or not anything loaded, and an
    /// operator who cannot see a count can still be told what the columns mean.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void TheWindowsAreNamedInEveryState(bool loaded, bool loading, bool error)
    {
        string scope = WindowScope.For(
            () => 46, () => 22, new Population(loaded, loading, error));

        Assert.StartsWith(
            "Overdue, due within 7 days, or with no date set.",
            scope,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A page that has not loaded claims nothing about how much is elsewhere.
    /// </summary>
    /// <remarks>
    /// The wave-6 rule applied to a figure this page computes rather than one it
    /// was handed. A remainder is a count; a count nobody has is not zero, and
    /// "every open task is listed here" is a completeness claim an unloaded page
    /// has not established. Silence, not a confident total and not a confident
    /// nothing.
    /// </remarks>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void APageThatDoesNotKnowMakesNoClaimAboutTheRest(bool loaded, bool loading, bool error)
    {
        string scope = WindowScope.For(
            () => 46, () => 22, new Population(loaded, loading, error));

        Assert.DoesNotContain("more open tasks", scope, StringComparison.Ordinal);
        Assert.DoesNotContain("Every open task", scope, StringComparison.Ordinal);
    }

    /// <summary>
    /// An error state does not become an authoritative zero remainder.
    /// </summary>
    /// <remarks>
    /// Pinned separately because this is the exact shape F-01 took: a figure the
    /// product did not have, rendered as a confident number.
    /// </remarks>
    [Fact]
    public void AFailedLoadDoesNotReportAZeroRemainder()
    {
        string scope = WindowScope.For(
            () => 0, () => 0, new Population(HasLoaded: false, IsLoading: false, HasError: true));

        Assert.Equal(WindowScope.Covered, scope);
    }

    /// <summary>
    /// More rows than the count that contains them is not explained away.
    /// </summary>
    /// <remarks>
    /// It should not happen, and if it ever does the page must not invent an
    /// account of it — least of all by claiming completeness.
    /// </remarks>
    [Fact]
    public void FewerCountedThanListedMakesNoClaim()
    {
        string scope = WindowScope.For(() => 5, () => 9, Loaded);

        Assert.Equal(WindowScope.Covered, scope);
    }

    /// <summary>
    /// The headline announces its own population, not the rows beneath it.
    /// </summary>
    /// <remarks>
    /// The other half of the disclosure. The figure is tenant-wide and the
    /// caption now says so, in the same words both channels use, so a
    /// screen-reader operator hears the scope rather than inferring it from a
    /// layout they cannot see.
    /// </remarks>
    [Fact]
    public void TheHeadlineAnnouncesThatItIsATotal()
    {
        string spoken = SummaryAuthority.Spoken("open tasks in total", () => 46, Loaded);

        Assert.Equal("46 open tasks in total", spoken);
    }

    /// <summary>A population in an exact state.</summary>
    private sealed record Population(bool HasLoaded, bool IsLoading, bool HasError)
        : IAuthoritativePopulation;
}
