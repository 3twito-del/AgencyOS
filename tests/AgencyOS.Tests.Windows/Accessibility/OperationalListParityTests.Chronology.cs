using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

/// <summary>
/// BF-03: the three Activity histories the C10 blind operator read carried no dates.
/// </summary>
/// <remarks>
/// <para>
/// Deal, pursuit and contract history entries each hold the authoritative instant the
/// event occurred, and the read side already orders them by it. The row templates
/// bound the summary and the detail and never the instant, so an operator could
/// reconstruct what happened and not when (C10, 2026-09-28).
/// </para>
/// <para>
/// These run the row templates the product ships: the binding is read from the
/// markup, the visible text is evaluated the way the row evaluates it, and the
/// announcement is the one <see cref="RowLabel"/> writes under the template's own
/// profile. They compare instants, not strings, so a row that showed one time and
/// announced another would fail.
/// </para>
/// </remarks>
public sealed partial class OperationalListParityTests
{
    public static TheoryData<string, string> ActivityHistories { get; } = new()
    {
        { "Pages/DealsPage.xaml", "HistoryList" },
        { "Pages/PipelinePage.xaml", "HistoryList" },
        { "Pages/ContractsPage.xaml", "HistoryList" },
    };

    private static readonly DateTimeOffset Occurred = new(2026, 9, 11, 14, 25, 36, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(ActivityHistories))]
    public void AnActivityRowShowsWhenItsEventOccurred(string file, string template)
    {
        Entry entry = Find(file, template);
        object row = ChronologyRow(entry);

        Shown occurred = Assert.Single(Visible(entry), x => x.Path == "OccurredAt");

        Assert.True(
            Coverage.SameInstant(Occurred, Render(row, occurred)),
            $"{Key(file, template)} shows '{Render(row, occurred)}' for {Occurred:O}.");
    }

    [Theory]
    [MemberData(nameof(ActivityHistories))]
    public void AnActivityRowAnnouncesTheInstantItShows(string file, string template)
    {
        Entry entry = Find(file, template);
        object row = ChronologyRow(entry);
        string announced = Announce(entry, row);

        Assert.True(
            announced.Split(", ").Any(x => Coverage.SameInstant(Occurred, x)),
            $"{Key(file, template)} announces '{announced}', which does not state {Occurred:O}.");
    }

    /// <summary>The date joins the row; the summary and its human context stay on it.</summary>
    [Theory]
    [MemberData(nameof(ActivityHistories))]
    public void AnActivityRowKeepsItsSummaryAndContext(string file, string template)
    {
        Entry entry = Find(file, template);
        RowTemplate markup = Template(entry);
        object row = ChronologyRow(entry);
        string announced = Announce(entry, row);
        List<Shown> shown = [.. Visible(entry)];

        Assert.Contains(shown, x => x.Path == "Summary");
        Assert.StartsWith(Render(row, shown.Single(x => x.Path == "Summary")) + ", ", announced, StringComparison.Ordinal);

        if (RowType(entry).Name == "OpportunityHistoryEntryResponse")
        {
            // The pursuit's context is the target, shown and said in its role.
            Assert.Contains(shown, x => x.Path == "TargetDisplayName");
            Assert.Contains("Target: Cresswell Media", announced, StringComparison.Ordinal);
        }
        else
        {
            // The detail stays visible beneath the summary and on the row's own help text.
            Assert.Contains(shown, x => x.Path == "Detail");
            Assert.Equal(Render(row, shown.Single(x => x.Path == "Detail")), HelpText(markup, row));

            // Who recorded it was announced before the date joined the row, and still is.
            Assert.Contains("By: Review Owner", announced, StringComparison.Ordinal);
        }
    }

    /// <summary>A realistic row of the template's own type, occurring at <see cref="Occurred"/>.</summary>
    private static object ChronologyRow(Entry entry) => RowType(entry).Name switch
    {
        "DealHistoryEntryResponse" => new global::AgencyOS.Contracts.Deals.DealHistoryEntryResponse(
            Occurred, "OfferTransition", "Outbound offer recorded", "Counter on fee and first-position billing.", Guid.NewGuid(), "Review Owner"),
        "OpportunityHistoryEntryResponse" => new global::AgencyOS.Contracts.Opportunities.OpportunityHistoryEntryResponse(
            Occurred, "TargetStatusChanged", "Target moved to Passed", null, "Cresswell Media", "Review Owner"),
        "ContractHistoryEntryResponse" => new global::AgencyOS.Contracts.Legal.ContractHistoryEntryResponse(
            Occurred, "Signature", "Signature recorded", "Signed by Halvard Pictures.", "Review Owner"),
        _ => throw new InvalidOperationException($"No chronology row for {RowType(entry).Name}."),
    };
}
