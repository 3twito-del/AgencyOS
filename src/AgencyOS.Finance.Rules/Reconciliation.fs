namespace AgencyOS.Finance.Rules

open System

/// <summary>
/// How what arrived compares with what was expected.
/// </summary>
/// <remarks>
/// Four factual outcomes and no judgement. There is deliberately no
/// <c>Withheld</c>, <c>Disputed</c>, <c>Error</c> or <c>Suspicious</c>: a payment
/// five thousand short of a receivable is a variance the system can state
/// precisely, and why it is short is something a person finds out (ADR-0023).
/// </remarks>
type ReconciliationOutcome =
    /// Nothing has been applied to it yet.
    | NotStarted = 0
    /// What arrived, plus what was deducted, accounts for what was expected.
    | Reconciled = 1
    /// Less arrived than was expected, and the difference is not explained.
    | Shortfall = 2
    /// More arrived than was expected.
    | Excess = 3

/// <summary>
/// One reconciliation as it crosses the language boundary.
/// </summary>
[<CLIMutable>]
type ReconciliationInput =
    { /// What the receivable said was owed.
      Expected: decimal
      /// What has been applied to it by valid allocations.
      Allocated: decimal
      /// Deductions somebody recorded as facts: withholding, bank charges, fees.
      Deductions: decimal
      /// Amounts given up deliberately, such as a write-off.
      WrittenOff: decimal
      /// What all four figures are denominated in.
      Currency: string }

/// <summary>
/// Explaining the difference between what was owed and what arrived.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic arithmetic over recorded facts, with no inference anywhere. A
/// deduction counts towards reconciliation only because somebody recorded it as a
/// deduction; an unexplained gap stays unexplained rather than being attributed to
/// tax, to a bank, or to anything else the system was not told about.
/// </para>
/// <para>
/// That refusal is the whole point of the module. Inferring "the missing 2,500 was
/// probably withholding" produces a reconciled receivable and a wrong tax record,
/// and nobody looks at it again (ADR-0023).
/// </para>
/// </remarks>
module Reconciliation =

    /// <summary>What has been accounted for, by any means.</summary>
    let accounted (input: ReconciliationInput) =
        input.Allocated + input.Deductions + input.WrittenOff

    /// <summary>
    /// The gap between what was expected and what has been accounted for.
    /// </summary>
    /// <remarks>
    /// Returned as a magnitude and a direction rather than a signed number, so no
    /// caller has to know whether positive means short or over.
    /// </remarks>
    let variance (input: ReconciliationInput) =
        let delta = input.Expected - accounted input
        abs delta, sign delta

    /// <summary>Where a reconciliation stands.</summary>
    let outcome (input: ReconciliationInput) =
        let magnitude, direction = variance input

        if input.Allocated = 0m && input.Deductions = 0m && input.WrittenOff = 0m then
            ReconciliationOutcome.NotStarted
        elif magnitude = 0m then
            ReconciliationOutcome.Reconciled
        elif direction > 0 then
            ReconciliationOutcome.Shortfall
        else
            ReconciliationOutcome.Excess

    /// <summary>Whether everything is explained.</summary>
    let isReconciled (input: ReconciliationInput) =
        outcome input = ReconciliationOutcome.Reconciled

    /// <summary>
    /// A sentence stating the arithmetic, for a person to read.
    /// </summary>
    /// <remarks>
    /// Names the figures and stops. It does not say what the variance is, because
    /// the system does not know, and a sentence that guessed would be quoted back
    /// as though the system did.
    /// </remarks>
    let describe (input: ReconciliationInput) =
        let magnitude, direction = variance input

        let currency = input.Currency

        match outcome input with
        | ReconciliationOutcome.NotStarted -> "Nothing has been applied to this receivable yet."
        | ReconciliationOutcome.Reconciled ->
            String.Format(
                Globalization.CultureInfo.InvariantCulture,
                "Expected {0:N2} {2}; {1:N2} {2} accounted for. Fully reconciled.",
                input.Expected,
                accounted input,
                currency
            )
        | _ ->
            let word = if direction > 0 then "short of" else "more than"

            String.Format(
                Globalization.CultureInfo.InvariantCulture,
                "Expected {0:N2} {3}; allocated {1:N2}, deductions {2:N2}. "
                + "That is {4:N2} {3} {5} what was expected, and the difference is not explained.",
                input.Expected,
                input.Allocated,
                input.Deductions,
                currency,
                magnitude,
                word
            )
