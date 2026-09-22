namespace AgencyOS.Client.Presentation;

/// <summary>
/// A population a summary is entitled to describe, and whether it knows yet.
/// </summary>
/// <remarks>
/// Three facts rather than one, because "nothing here" and "I could not find out"
/// and "I have not looked yet" are different answers and only the first is a
/// business result. <see cref="ViewModels.ViewModelBase"/> already models the last
/// two; <see cref="HasLoaded"/> is the one a totalling line also needs, because a
/// collection that is empty because nobody asked looks exactly like a collection
/// that is empty because there is nothing in it.
/// </remarks>
public interface IAuthoritativePopulation
{
    /// <summary>Whether a load has ever completed successfully.</summary>
    bool HasLoaded { get; }

    /// <summary>Whether a load is in flight now.</summary>
    bool IsLoading { get; }

    /// <summary>Whether the last load failed.</summary>
    bool HasError { get; }
}

/// <summary>
/// What a summary line may claim, given whether the product actually knows.
/// </summary>
/// <remarks>
/// <para>
/// A counted or totalled sentence is an assertion about a whole population:
/// "6 receivable(s), 1 overdue. Outstanding: 1,255,000.00 USD." It is only true if
/// the population it counted is the real one. Every Finance summary was built from
/// whatever the local collection happened to hold, so a failed load produced
/// <c>0 receivable(s) … Outstanding:</c> nothing — a confident financial zero
/// asserted from an empty list, printed directly beneath the error bar that said
/// the load had failed.
/// </para>
/// <para>
/// That is the case CLAUDE.md §1 principle 11 forbids: a surface may state that
/// something does not exist only when the projection it reads is authoritative for
/// the question. An empty collection after a failed request is not authoritative
/// for anything.
/// </para>
/// <para>
/// <strong>Silence rather than a smaller lie.</strong> The wording is deliberately
/// thin. The error bar beside it already carries the server's own explanation, so
/// restating it here would be noise, and inventing a number with a caveat attached
/// would still be inventing a number.
/// </para>
/// </remarks>
public static class SummaryAuthority
{
    /// <summary>Said while the answer is still being fetched.</summary>
    public const string Loading = "Still loading.";

    /// <summary>
    /// Said when the product cannot establish the total.
    /// </summary>
    /// <remarks>
    /// Covers the failed load and the load that never happened. Both mean the same
    /// thing to an operator — this figure is not available — and neither entitles
    /// the screen to a number.
    /// </remarks>
    public const string Unavailable = "Totals unavailable.";

    /// <summary>
    /// Whether the product currently knows this population.
    /// </summary>
    /// <remarks>
    /// The same question a total asks, for the statements beside it. An empty-state
    /// notice is an assertion too — "there are none" — and a load that succeeded
    /// once and then failed leaves a collection that is still empty and no longer
    /// known, which would otherwise open the notice beside the error bar.
    /// </remarks>
    public static bool Knows(IAuthoritativePopulation population)
    {
        ArgumentNullException.ThrowIfNull(population);

        return population.HasLoaded && !population.IsLoading && !population.HasError;
    }

    /// <summary>
    /// The summary, or why there is not one.
    /// </summary>
    /// <param name="total">
    /// Builds the sentence. Called only when every population is authoritative, so
    /// it never computes a total out of a collection that failed to arrive.
    /// </param>
    /// <param name="populations">
    /// Every population the sentence draws on. A summary spanning two lists is
    /// authoritative only when both are: an outstanding balance built from loaded
    /// receivables and failed payments is as wrong as one built from neither.
    /// </param>
    public static string Of(Func<string> total, params IAuthoritativePopulation[] populations)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(populations);

        // A failure anywhere settles it, even if another population is still
        // arriving: the sentence needs all of them and one of them is not coming.
        foreach (IAuthoritativePopulation population in populations)
        {
            if (population.HasError)
            {
                return Unavailable;
            }
        }

        foreach (IAuthoritativePopulation population in populations)
        {
            if (population.IsLoading)
            {
                return Loading;
            }
        }

        foreach (IAuthoritativePopulation population in populations)
        {
            if (!population.HasLoaded)
            {
                return Unavailable;
            }
        }

        return total();
    }
}
