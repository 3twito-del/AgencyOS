namespace AgencyOS.Finance.Rules

open System

/// <summary>
/// Which side of a journal line an amount sits on.
/// </summary>
/// <remarks>
/// An explicit side with a positive amount, rather than a signed number whose
/// meaning depends on the account's normal balance. Sign-as-convention is
/// readable only to whoever wrote it, and every reader afterwards has to remember
/// which way round it went (ADR-0023).
/// </remarks>
type EntrySide =
    /// Left. Increases assets and expenses; decreases liabilities, equity and revenue.
    | Debit = 1
    /// Right. Increases liabilities, equity and revenue; decreases assets and expenses.
    | Credit = 2

/// <summary>
/// What kind of thing an account is.
/// </summary>
/// <remarks>
/// The five classical categories plus a clearing category for accounts that hold
/// money in transit between two known states. Deliberately neutral: AgencyOS has
/// not adopted an accounting framework and does not claim one (ADR-0023).
/// </remarks>
type AccountCategory =
    | Asset = 1
    | Liability = 2
    | Equity = 3
    | Revenue = 4
    | Expense = 5
    /// Money known to be somewhere between two accounts. Should trend to zero.
    | Clearing = 6

/// <summary>Where a journal entry stands.</summary>
type JournalState =
    /// Being assembled. Not part of the ledger and not visible in any balance.
    | JournalDraft = 1
    /// Balanced and committed. Immutable from here.
    | Posted = 2
    /// Posted, then undone by a reversing entry. The original still says what it said.
    | Reversed = 3

/// <summary>What causes a journal entry to change state.</summary>
type JournalTrigger =
    /// The entry was checked and committed to the ledger.
    | Post = 1
    /// A reversing entry was posted against it.
    | Reverse = 2
    /// A draft was abandoned before posting. Only legal while it is a draft.
    | DiscardDraft = 3

/// <summary>Why a posting was refused.</summary>
type PostingError =
    /// Debits and credits did not agree.
    | Unbalanced of debits: decimal * credits: decimal * currency: string
    /// An entry with no lines, which asserts an event and records nothing about it.
    | NoLines
    /// A line of nothing.
    | ZeroLine of accountId: Guid
    /// Lines in more than one currency, which cannot be balanced against each other.
    | MixedCurrency of first: string * second: string
    /// An entry that is only one-sided.
    | SingleSided of side: EntrySide
    /// The entry is not in a state that can be posted.
    | NotPostable of state: JournalState
    /// A reversal was asked for against something that was never posted.
    | NotReversible of state: JournalState

/// <summary>
/// One journal line as it crosses the language boundary.
/// </summary>
[<CLIMutable>]
type JournalLineInput =
    { /// Which account the line touches.
      AccountId: Guid
      /// Debit or credit, as the enum's integer value.
      Side: int
      /// The figure. Always positive; the side carries the direction.
      Amount: decimal
      /// What the figure is denominated in.
      Currency: string }

/// <summary>
/// Balancing, posting and reversing.
/// </summary>
/// <remarks>
/// <para>
/// The one invariant that makes a ledger a ledger: within a posted entry, in one
/// currency, debits equal credits. Everything else in finance can be reconstructed
/// from source documents if it goes wrong; an unbalanced ledger cannot, because
/// there is no longer a statement of what the missing side was.
/// </para>
/// <para>
/// This module is where that is decided. The domain calls it before it writes, and
/// PostgreSQL checks the same arithmetic again with a deferred constraint trigger,
/// because a rule this important should not depend on one code path remembering.
/// </para>
/// </remarks>
module Journal =

    /// <summary>The side an integer denotes, or nothing when it denotes neither.</summary>
    let sideOf (value: int) =
        match value with
        | 1 -> Some EntrySide.Debit
        | 2 -> Some EntrySide.Credit
        | _ -> None

    /// <summary>Totals one side of an entry.</summary>
    let private totalFor (side: EntrySide) (lines: JournalLineInput seq) =
        lines
        |> Seq.filter (fun line -> line.Side = int side)
        |> Seq.sumBy (fun line -> line.Amount)

    /// <summary>What the debits come to.</summary>
    let debits (lines: JournalLineInput seq) = totalFor EntrySide.Debit lines

    /// <summary>What the credits come to.</summary>
    let credits (lines: JournalLineInput seq) = totalFor EntrySide.Credit lines

    /// <summary>
    /// Whether a set of lines is fit to post.
    /// </summary>
    /// <remarks>
    /// Checked in a deliberate order, so the message a person sees names the first
    /// thing actually wrong rather than a downstream consequence of it. An entry
    /// with no lines is not "unbalanced at zero"; it is empty, and saying so is
    /// more use.
    /// </remarks>
    let validate (lines: JournalLineInput seq) =
        let lines = lines |> Array.ofSeq

        if lines.Length = 0 then
            Error NoLines
        else
            let currency = lines.[0].Currency

            match lines |> Array.tryFind (fun line -> line.Currency <> currency) with
            | Some other -> Error(MixedCurrency(currency, other.Currency))
            | None ->
                match lines |> Array.tryFind (fun line -> line.Amount <= 0m) with
                | Some empty -> Error(ZeroLine empty.AccountId)
                | None ->
                    let debit = debits lines
                    let credit = credits lines

                    if debit = 0m then Error(SingleSided EntrySide.Credit)
                    elif credit = 0m then Error(SingleSided EntrySide.Debit)
                    elif debit <> credit then Error(Unbalanced(debit, credit, currency))
                    else Ok lines

    /// <summary>Whether a set of lines balances, as a plain answer.</summary>
    let isBalanced (lines: JournalLineInput seq) =
        match validate lines with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>
    /// The lines that undo a posted entry.
    /// </summary>
    /// <remarks>
    /// Every line mirrored to the opposite side, same account, same amount. The
    /// original is never touched: what the agency posted on the day it posted it
    /// remains readable, and the correction sits beside it with its own date and
    /// its own reason. That is the difference between a correction and a rewrite.
    /// </remarks>
    let reverseLines (lines: JournalLineInput seq) =
        lines
        |> Seq.map (fun line ->
            { line with
                Side =
                    if line.Side = int EntrySide.Debit then
                        int EntrySide.Credit
                    else
                        int EntrySide.Debit })
        |> Array.ofSeq

    /// <summary>The state a trigger leads to, or nothing when it is not permitted.</summary>
    let next (state: JournalState) (trigger: JournalTrigger) =
        match state, trigger with
        | JournalState.JournalDraft, JournalTrigger.Post -> Some JournalState.Posted
        | JournalState.Posted, JournalTrigger.Reverse -> Some JournalState.Reversed

        // Discarding a draft removes it rather than moving it, so there is no next
        // state to report. Every other pair is simply not permitted.
        | _ -> None

    /// <summary>Whether an entry can still be edited.</summary>
    /// <remarks>
    /// Draft only. Once posted, an entry is a statement the agency has made about
    /// its own books, and statements are corrected by making another one.
    /// </remarks>
    let isEditable (state: JournalState) = state = JournalState.JournalDraft

    /// <summary>Whether an entry counts towards a balance.</summary>
    /// <remarks>
    /// A reversed entry still counts, and so does the entry that reversed it. They
    /// net to nothing between them, which is the honest arithmetic: the money moved
    /// and then moved back, and both movements happened.
    /// </remarks>
    let affectsBalance (state: JournalState) =
        state = JournalState.Posted || state = JournalState.Reversed

    /// <summary>
    /// How a line moves an account's balance.
    /// </summary>
    /// <remarks>
    /// Assets and expenses increase on the debit side; liabilities, equity and
    /// revenue increase on the credit side. This is the only place that convention
    /// is written down, so a balance shown anywhere in AgencyOS is computed by this
    /// function or it is wrong.
    /// </remarks>
    let signedFor (category: AccountCategory) (side: EntrySide) (amount: decimal) =
        let increasesOnDebit =
            category = AccountCategory.Asset
            || category = AccountCategory.Expense
            || category = AccountCategory.Clearing

        match increasesOnDebit, side with
        | true, EntrySide.Debit -> amount
        | true, _ -> -amount
        | false, EntrySide.Credit -> amount
        | false, _ -> -amount
