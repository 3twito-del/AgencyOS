using System.Globalization;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// What the Command Center's task lists cover, and what sits outside them.
/// </summary>
/// <remarks>
/// <para>
/// The numbers were never wrong. The headline counts every open task in the
/// agency; the three lists are windows onto it — overdue, due inside the horizon,
/// and undated — which are disjoint but deliberately <strong>not</strong>
/// exhaustive. An open task due further ahead than the horizon belongs to none of
/// them and is still counted above.
/// </para>
/// <para>
/// Nothing on the screen said so, so a headline of 46 above 22 rows read either
/// as a contradiction or as a complete picture. The build-82 blind operator took
/// it for the second, scanned the rows, and believed they had seen the work
/// (F-05). This is the sentence that makes the difference recoverable from the
/// screen, and both channels read it from here so the seen page and the spoken
/// one cannot say different things about what is covered.
/// </para>
/// <para>
/// <strong>Nothing here queries anything.</strong> The remainder is arithmetic
/// over two figures the page already holds. The populations are untouched: this
/// repairs what is said about them, not what they contain.
/// </para>
/// </remarks>
public static class WindowScope
{
    /// <summary>
    /// The three windows, in the words their own headings use.
    /// </summary>
    /// <remarks>
    /// Stated rather than derived, because a caption assembled from the list
    /// headings would go stale silently if a window were ever renamed, and this
    /// sentence is the one place an operator learns what the lists are.
    /// </remarks>
    public const string Covered = "Overdue, due within 7 days, or with no date set.";

    /// <summary>
    /// What the lists cover, and how much open work is not in them.
    /// </summary>
    /// <param name="total">The headline population: every open task.</param>
    /// <param name="shown">How many rows the three windows hold between them.</param>
    /// <param name="population">Whether the projection behind both has arrived.</param>
    /// <remarks>
    /// <para>
    /// Where the projection has not arrived the sentence says what the lists are
    /// and stops. A remainder is a count, and a count nobody has is not zero;
    /// "every open task is listed here" is a claim about completeness that an
    /// unloaded page has not established. That is the wave-6 rule applied to a
    /// figure this page computes rather than one it was given.
    /// </para>
    /// <para>
    /// A negative remainder would mean more rows than the count that is supposed
    /// to contain them. This page cannot explain that, so it does not try: it
    /// falls back to describing the windows and makes no claim about the rest.
    /// </para>
    /// </remarks>
    public static string For(
        Func<int> total,
        Func<int> shown,
        IAuthoritativePopulation population)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(shown);

        if (!SummaryAuthority.Knows(population))
        {
            return Covered;
        }

        return (total() - shown()) switch
        {
            > 0 and int later => string.Create(
                CultureInfo.CurrentCulture,
                $"{Covered} {later} more open tasks are scheduled further ahead and are not listed here."),

            // A real business result: every open task falls inside a window.
            0 => Covered + " Every open task is listed here.",

            _ => Covered,
        };
    }
}
