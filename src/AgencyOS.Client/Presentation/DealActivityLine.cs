using System.Globalization;
using System.Text.RegularExpressions;
using AgencyOS.Contracts.Deals;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// The secondary line of a Deal Activity row, as an operator reads it.
/// </summary>
/// <remarks>
/// <para>
/// When a counter answers an offer, the domain records the reason "Answered by offer
/// {id}." on the superseded offer's history. That is the canonical record and it
/// stays as it is. Shown on the row it put a raw identifier beneath "Inbound offer
/// superseded by a later offer": the C10 blind operator read GUIDs as the
/// explanation of what changed (BF-03).
/// </para>
/// <para>
/// The answering offer is one the deal already carries, so the row names it the way
/// the Offers tab does, by its sequence and direction. Where it is not among the
/// offers loaded, the identifier is not shown at all rather than shown raw. Only that
/// exact recorded form, on that one kind of entry, is touched: every other detail,
/// including a reason somebody wrote, is returned unchanged.
/// </para>
/// </remarks>
public static partial class DealActivityLine
{
    /// <summary>The history kind the read side writes for a counter answering an offer.</summary>
    public const string AnsweredByCounter = "OfferAnsweredByCounter";

    /// <summary>What the row shows beneath the entry's summary, or null for nothing.</summary>
    /// <param name="entry">The history entry as the server returned it.</param>
    /// <param name="offers">The deal's offers, as the same page loaded them.</param>
    public static string? Detail(DealHistoryEntryResponse entry, IEnumerable<OfferResponse> offers)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(offers);

        if (!string.Equals(entry.Kind, AnsweredByCounter, StringComparison.Ordinal)
            || entry.Detail is not { } detail
            || RecordedAnswer().Match(detail) is not { Success: true } match)
        {
            return entry.Detail;
        }

        Guid answeredBy = Guid.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);

        return offers.FirstOrDefault(x => x.Id == answeredBy) is { } offer
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Answered by offer {offer.Sequence} ({offer.Direction.ToLowerInvariant()}).")
            : null;
    }

    [GeneratedRegex(
        @"^Answered by offer (?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RecordedAnswer();
}
