namespace AgencyOS.Client.Presentation;

/// <summary>How loudly a standing should be said.</summary>
/// <remarks>
/// Deliberately not the WinUI severity type: this assembly does not reference the
/// UI, and the mapping to an <c>InfoBar</c> belongs at the surface. The point of
/// naming it here is that the choice between "this is fine" and "look at this" is a
/// judgement about the domain, not about a control.
/// </remarks>
public enum StandingSeverity
{
    /// <summary>A true statement of where things stand. No claim that it is good.</summary>
    Informational,

    /// <summary>Something finished the way it was supposed to.</summary>
    Success,

    /// <summary>Something ended without finishing.</summary>
    Warning,
}

/// <summary>What a negotiation's page may truthfully say about its paper.</summary>
/// <param name="Show">Whether to say anything at all.</param>
/// <param name="Title">The headline.</param>
/// <param name="Message">The sentence beneath it.</param>
/// <param name="Severity">How loudly.</param>
public sealed record ContractStanding(
    bool Show,
    string Title,
    string Message,
    StandingSeverity Severity)
{
    /// <summary>Say nothing.</summary>
    public static ContractStanding Silent { get; } =
        new(false, string.Empty, string.Empty, StandingSeverity.Informational);
}

/// <summary>
/// Decides what the Deals page may say about whether a contract exists.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 78 found the Deals page showing a green
/// success banner reading "No contract has been drafted, signed or executed, and
/// AgencyOS does not track that yet" on a negotiation whose contract had been
/// executed three weeks earlier.
/// </para>
/// <para>
/// The sentence was not careless. It was <em>true when M7 shipped</em>, and the
/// comment beside it said so: M7 had no way to know whether a contract existed
/// (ADR-0021). M8 then added contracts, and neither the sentence nor the deal read
/// model was revisited — <c>GET /deals/{id}</c> still carries no contract, so the
/// page genuinely could not know. A statement that stops being true is worse than a
/// gap, because it is said with confidence.
/// </para>
/// <para>
/// This type takes the contracts the server reports <em>for that deal</em> and says
/// only what they support. It invents no second contract taxonomy: the states are
/// M8's own, and the words are the ones the Contracts workspace already uses.
/// </para>
/// </remarks>
public static class ContractStandingFor
{
    /// <summary>
    /// The order paper advances in, most advanced first.
    /// </summary>
    /// <remarks>
    /// A negotiation can carry more than one instrument — an amendment is a separate
    /// contract pointing back at what it amends (ADR-0022). The banner reports the
    /// furthest one along, because that is what an operator means by "where is the
    /// paper". Instruments that ended without finishing rank last, so a live draft
    /// beside an abandoned one reports the draft.
    /// </remarks>
    private static readonly string[] Advancement =
    [
        "Executed",
        "PartiallyExecuted",
        "ApprovedForExecution",
        "UnderReview",
        "Draft",
        "Terminated",
        "Superseded",
        "Abandoned",
    ];

    /// <summary>What the page may say, given the deal and its contracts.</summary>
    /// <param name="dealStatus">The negotiation's own status.</param>
    /// <param name="contractStatuses">
    /// The status of every contract the server returns for this deal. Empty when
    /// there are none.
    /// </param>
    public static ContractStanding Of(
        string? dealStatus,
        IReadOnlyCollection<string>? contractStatuses)
    {
        string? furthest = Furthest(contractStatuses);

        if (furthest is null && contractStatuses is { Count: > 0 })
        {
            // Contracts exist, in a state this build has no word for. Saying nothing
            // is the only honest option: the one thing that must never happen here is
            // reporting an absence, which is the defect this type exists to prevent.
            return ContractStanding.Silent;
        }

        if (furthest is null)
        {
            // Nothing is papered. Worth saying only once terms are agreed, because
            // before that nobody expects paper. It is not a success — the absence of
            // an error is not an achievement, and rendering this green is what made
            // the old banner misread even when it was true.
            return string.Equals(dealStatus, "TermsAgreed", StringComparison.Ordinal)
                ? new ContractStanding(
                    true,
                    "Commercial terms agreed",
                    "The parties agreed terms. No contract has been recorded against "
                        + "this negotiation yet.",
                    StandingSeverity.Informational)
                : ContractStanding.Silent;
        }

        return furthest switch
        {
            "Executed" => new ContractStanding(
                true, "Contract executed",
                "A contract for this negotiation has been executed. The Contracts "
                    + "workspace holds the paper and its signatures.",
                StandingSeverity.Success),

            "PartiallyExecuted" => new ContractStanding(
                true, "Contract partially executed",
                "A contract for this negotiation is signed by some of the parties it "
                    + "requires, and not yet by all of them.",
                StandingSeverity.Informational),

            "ApprovedForExecution" => new ContractStanding(
                true, "Contract approved for signature",
                "A contract for this negotiation has been cleared and is waiting to "
                    + "be signed.",
                StandingSeverity.Informational),

            "UnderReview" => new ContractStanding(
                true, "Contract under review",
                "A contract for this negotiation is being reviewed.",
                StandingSeverity.Informational),

            "Draft" => new ContractStanding(
                true, "Contract in drafting",
                "A contract for this negotiation is being drafted.",
                StandingSeverity.Informational),

            "Terminated" => new ContractStanding(
                true, "Contract terminated",
                "The contract for this negotiation was executed and has since been "
                    + "terminated.",
                StandingSeverity.Warning),

            "Superseded" => new ContractStanding(
                true, "Contract superseded",
                "The contract for this negotiation has been replaced by another "
                    + "instrument.",
                StandingSeverity.Warning),

            "Abandoned" => new ContractStanding(
                true, "Contract drafting abandoned",
                "A contract for this negotiation was drafted and abandoned before "
                    + "execution.",
                StandingSeverity.Warning),

            // A status this build does not know. Saying nothing beats guessing, and
            // it is better than the previous behaviour either way.
            _ => ContractStanding.Silent,
        };
    }

    private static string? Furthest(IReadOnlyCollection<string>? statuses)
    {
        if (statuses is null || statuses.Count == 0)
        {
            return null;
        }

        foreach (string candidate in Advancement)
        {
            if (statuses.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
