namespace AgencyOS.Finance.Rules

open System

/// <summary>
/// Why an allocation was refused.
/// </summary>
type AllocationError =
    /// The allocation is denominated differently from the payment or the receivable.
    | AllocationCurrencyMismatch of allocation: string * target: string
    /// An allocation of nothing, which claims something happened when nothing did.
    | AllocationNotPositive
    /// The payment does not have this much left to give.
    | ExceedsPaymentFunds of requested: decimal * available: decimal
    /// The receivable does not owe this much.
    | ExceedsReceivableBalance of requested: decimal * outstanding: decimal
    /// The payment or the receivable is no longer in a state that accepts allocation.
    | TargetNotAllocatable of reason: string

/// <summary>
/// Where a receivable stands, derived from arithmetic rather than stored.
/// </summary>
/// <remarks>
/// Deliberately a function of the allocations and adjustments against it, never a
/// column somebody sets. A status column beside the numbers is a second fact that
/// can disagree with the first, and when it does, the one people read is the wrong
/// one (ADR-0023).
/// </remarks>
type ReceivableState =
    /// Nothing has been applied to it.
    | ReceivableOpen = 1
    /// Some has been applied, and something remains.
    | PartiallyPaid = 2
    /// Nothing remains.
    | Paid = 3
    /// Withdrawn before collection. Terminal.
    | ReceivableCancelled = 4
    /// Given up as uncollectable, deliberately and with a reason. Terminal.
    | WrittenOff = 5

/// <summary>
/// One allocation as it crosses the language boundary.
/// </summary>
[<CLIMutable>]
type AllocationInput =
    { /// The receivable this line is applied to.
      ReceivableId: Guid
      /// How much of the payment is applied.
      Amount: decimal
      /// What that amount is denominated in.
      Currency: string
      /// Whether the line still counts. A reversed line is history, not money.
      IsApplied: bool }

/// <summary>
/// One receivable as the kernel needs to see it.
/// </summary>
[<CLIMutable>]
type ReceivableInput =
    { Id: Guid
      /// What was originally expected.
      OriginalAmount: decimal
      Currency: string
      /// Amounts already applied by valid allocations.
      AllocatedAmount: decimal
      /// Deductions and write-offs recorded against it, which reduce what is owed.
      AdjustedAmount: decimal
      /// Whether it has been cancelled or written off, which no arithmetic can undo.
      IsClosedByAct: bool
      /// True for a write-off, false for a cancellation. Meaningless unless closed.
      IsWriteOff: bool
      /// When it falls due, or null when the contract states no date this build can work out.
      DueOn: Nullable<DateOnly> }

/// <summary>
/// Applying money to what is owed.
/// </summary>
/// <remarks>
/// <para>
/// The invariant this module exists to hold is conservation: every unit of a
/// payment is either applied to something or explicitly unapplied, and nothing
/// disappears between the two. A residual that is quietly dropped is cash the
/// agency received and cannot account for.
/// </para>
/// <para>
/// Nothing here decides *which* receivable a payment should go to. That is a
/// judgement about somebody's intent, and the system does not have it (ADR-0023).
/// </para>
/// </remarks>
module Allocation =

    /// <summary>What a receivable still owes.</summary>
    /// <remarks>
    /// Original less what has been applied and less what has been adjusted away.
    /// Floored at zero rather than allowed to go negative: a negative outstanding
    /// is an over-allocation, and the constraint that prevents it lives below.
    /// </remarks>
    let outstanding (receivable: ReceivableInput) =
        let remaining =
            receivable.OriginalAmount - receivable.AllocatedAmount - receivable.AdjustedAmount

        if remaining < 0m then 0m else remaining

    /// <summary>Where a receivable stands.</summary>
    let stateOf (receivable: ReceivableInput) =
        if receivable.IsClosedByAct then
            if receivable.IsWriteOff then
                ReceivableState.WrittenOff
            else
                ReceivableState.ReceivableCancelled
        elif outstanding receivable = 0m && receivable.OriginalAmount > 0m then
            ReceivableState.Paid
        elif receivable.AllocatedAmount > 0m || receivable.AdjustedAmount > 0m then
            ReceivableState.PartiallyPaid
        else
            ReceivableState.ReceivableOpen

    /// <summary>Whether a receivable can still take money.</summary>
    let acceptsAllocation (receivable: ReceivableInput) =
        not receivable.IsClosedByAct && outstanding receivable > 0m

    /// <summary>
    /// Whether a receivable is past its date with something still owed.
    /// </summary>
    /// <remarks>
    /// Derived from the date and the balance, never stored. A receivable with no
    /// resolvable due date is **not** overdue: the contract did not say when, and
    /// asserting lateness against a date nobody agreed would be an invention.
    /// </remarks>
    let isOverdue (today: DateOnly) (receivable: ReceivableInput) =
        receivable.DueOn.HasValue
        && receivable.DueOn.Value < today
        && acceptsAllocation receivable

    /// <summary>How much of a payment is still free to allocate.</summary>
    let unapplied (paymentAmount: decimal) (lines: AllocationInput seq) =
        let applied =
            lines
            |> Seq.filter (fun line -> line.IsApplied)
            |> Seq.sumBy (fun line -> line.Amount)

        paymentAmount - applied

    /// <summary>
    /// Whether one more allocation is legal.
    /// </summary>
    /// <remarks>
    /// Four questions, each of which has produced a real accounting error somewhere:
    /// is it the same money, is it more than nothing, does the payment still have
    /// it, and does the receivable still want it.
    /// </remarks>
    let validate
        (paymentAmount: decimal)
        (paymentCurrency: string)
        (existing: AllocationInput seq)
        (receivable: ReceivableInput)
        (requested: decimal)
        =
        if paymentCurrency <> receivable.Currency then
            Error(AllocationCurrencyMismatch(paymentCurrency, receivable.Currency))
        elif requested <= 0m then
            Error AllocationNotPositive
        elif receivable.IsClosedByAct then
            Error(
                TargetNotAllocatable
                    "this receivable has been cancelled or written off, so it can no longer take money"
            )
        else
            let free = unapplied paymentAmount existing
            let owed = outstanding receivable

            if requested > free then Error(ExceedsPaymentFunds(requested, free))
            elif requested > owed then Error(ExceedsReceivableBalance(requested, owed))
            else Ok requested

    /// <summary>
    /// Splits a figure across parts in proportion, losing nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used where a collected sum has to be attributed proportionally - commission
    /// earned on a partial payment across several obligations, for instance. Each
    /// part is rounded to the currency's minor units, and the residual left by
    /// rounding is given to the last part, so the parts sum to the whole exactly.
    /// </para>
    /// <para>
    /// The alternative, rounding each share independently and accepting whatever
    /// total emerges, is how a ledger ends up a cent short of the cash that
    /// produced it. A property test asserts conservation for generated inputs.
    /// </para>
    /// </remarks>
    let apportion (currency: string) (total: decimal) (weights: decimal seq) =
        let weights = weights |> Array.ofSeq
        let sum = weights |> Array.sum

        if weights.Length = 0 then
            Ok [||]
        elif sum <= 0m then
            Error(NegativeAmount sum)
        elif total < 0m then
            Error(NegativeAmount total)
        else
            let shares =
                weights
                |> Array.map (fun weight ->
                    Rounding.toMinorUnits currency (Rounding.toIntermediate (total * weight / sum)))

            // Everything but the last, then the remainder. The last share absorbs
            // the rounding so the parts add up to what was actually received.
            let allButLast = shares.[.. shares.Length - 2] |> Array.sum

            shares.[shares.Length - 1] <- total - allButLast

            Ok shares
