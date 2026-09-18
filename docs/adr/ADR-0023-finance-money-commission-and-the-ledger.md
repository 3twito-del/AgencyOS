# ADR-0023 — Finance: money, commission, collection and the ledger

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M9 — Finance, Commissions, Receivables, Payments & Ledger
- **Supersedes:** nothing
- **Builds on:** ADR-0007 (authorization), ADR-0011 (tenant isolation),
  ADR-0012 (curated history vs audit), ADR-0014 (concurrency and idempotency),
  ADR-0021 (deal rules kernel, economics behind their own grant),
  ADR-0022 (contract model, recorded rather than performed)

## Context

M8 ends with an instrument: a contract that exists, that says things, and that
somebody has signed. M9 is what happens to the money that instrument describes.

Finance is where a system's small dishonesties stop being embarrassing and start
being expensive. A balance that is a stored column drifts from the rows beneath
it. A total that adds dollars to euros looks authoritative and means nothing. A
commission calculated from today's rate quietly restates history. A shortfall
attributed to withholding produces a reconciled receivable and a tax record nobody
checked. Each of these reads correctly on a screen and is wrong in a place nobody
looks until an argument.

Every decision below is a decision about what AgencyOS is entitled to assert.

## Decisions

### 1. Money is an amount and a currency, everywhere, and never a float

There is no `double`, `float` or `Half` anywhere in the milestone. Amounts are
`decimal` in C#, `numeric` in PostgreSQL, and `Amount` in the F# kernel — a record
carrying the currency *inside the value*, so cross-currency arithmetic is
unrepresentable rather than merely discouraged.

No monetary value crosses the wire as a bare number. Every request and response
uses `MoneyRequest` / `MoneyResponse`, and `OpenApiContractTests` fails the build
if a schema grows an `amount` that carries no currency beside it.

**Two currencies are never added.** Balances, outstanding totals and commission
figures are reported per currency, as lists. There is no exchange rate in
AgencyOS, no FX table, and no conversion, so a single "total outstanding" over a
mixed book would be a number that does not exist. The Windows surface groups by
currency for the same reason.

### 2. One rounding policy, applied once, at a stated boundary

`AgencyOS.Finance.Rules.Rounding` is the only place rounding happens.

- Intermediate arithmetic runs at **six decimal places**.
- The result is rounded **exactly once**, at the point of storage, to the
  currency's own minor units — two for most, zero for JPY, KRW, ISK, CLP and VND,
  three for KWD, BHD, OMR, JOD and TND.
- The mode is banker's rounding (`MidpointRounding.ToEven`), stated once rather
  than chosen per handler.

Apportioning a sum across shares gives the residual to the last share, so the
parts always add back to the whole. A handler cannot round differently, because
handlers do not round.

### 3. Only an operative contract creates a collectible amount

> **Superseded by ADR-0040 (2026-09-19).** The definition of *operative* below —
> "executed, **or** carrying a recorded effective date" — let a draft nobody had
> signed carry a collectible amount as soon as somebody typed a date on it, which
> the operational-alpha evaluation demonstrated. Operative now means **executed**.
> The rest of this ADR stands, and so does the principle immediately below.

A `MonetaryObligation` may be recorded only against a contract that is
**operative**: executed, or carrying a recorded effective date, and not abandoned
or superseded.

M6 interest, an M7 offer and an M7 deal at *TermsAgreed* are none of them a source
of money anybody can be asked for. Agreed commercial terms say what the parties
intend; the contract says what is owed. M9 will not silently turn the first into
the second, and there is no flag that overrides the gate.

### 4. Unknown is a real amount, and zero is not a synonym for it

`ObligationAmountKind` has four members: `Fixed`, `Formula`, `Contingent`,
`Unknown`. A backend participation nobody can value is an obligation with no
figure. Recording it as zero would put a false number into every total that
touched it, and a commission calculated on it would be a false number with a
decimal point.

An obligation with no calculable amount produces no receivable and no commission,
and the refusal says why. It can be quantified later, by somebody who knows, and
that is a recorded act with its own reason.

There is no speculative participation model here: no waterfall, no breakeven, no
Hollywood accounting. When statement ingestion arrives, the figures it produces
will be quantifications of obligations that already exist.

### 5. Entitlement, collection and outstanding are three numbers

An agency entitled to a hundred thousand against a million-dollar contract that
has paid four hundred thousand has **collected forty thousand**. All three figures
are kept apart in the domain, the projection, the API and the UI, and no surface
reports one as another.

- `Entitled` is the rule applied to the whole obligation.
- `Collected` is the same snapshotted rate applied to what has actually arrived
  against the client receivable.
- `Outstanding` is what is left.

`Collected` is computed from applied allocations on every read. Where an
entitlement names a client receivable, that one is used; where it does not — the
ordinary case, because a commission is calculated against an obligation before the
receivable is raised — every client receivable raised from that obligation counts,
which is precisely what the posting path already does when it moves earned
commission into revenue. The projection and the ledger agree because they are
computed the same way.

### 6. Commission comes from a dated rule, and there is no default rate

`CommissionRule` is effective-dated and hangs off a representation, because a rate
is a term of a relationship and relationships are renegotiated. The kernel picks
the governing rule by the date the obligation **fell due**, not by what is current:
recalculating a 2027 commission in 2029 gives the 2027 answer.

- Contract-specific beats general.
- Two rules in force at once is refused rather than resolved, and the refusal
  happens when the overlap is *created* so the person who made it hears about it.
- No governing rule produces **no entitlement** and an explanation. There is no
  default rate anywhere in AgencyOS, because a default would be a number the
  agency never agreed with anybody.
- Rates above fifty per cent are refused as data entry errors. There is no
  universal entertainment-industry formula and M9 invents none: the three shapes
  offered are a percentage of gross, a percentage of one named term, and a fixed
  sum.

The rate is **snapshotted** onto the entitlement at calculation. A later
correction to the rule does not restate what was already acted on; recalculating
supersedes the earlier entitlement and leaves it readable.

### 7. Client funds are not agency revenue

`ReceivableBeneficiary` is `Client` or `Agency`, and it is the most consequential
field in the milestone.

Money collected against a **client** receivable is held and owed onward. It posts
to `ClientFundsPayable`, a liability. Only the commission the agency earned on it
moves to `CommissionRevenue`, and only in proportion to what actually arrived.
Recognising the gross receipt as revenue would overstate agency income by an order
of magnitude on a typical deal.

### 8. Double entry, with sides and positive amounts

The ledger is `Account` / `JournalEntry` / `JournalLine`. Every line carries a
`Side` — `Debit` or `Credit` — and a **positive** amount.

There is no signed-number folklore. A minus sign means one thing on an asset and
the opposite on a liability, and encoding direction that way is how a ledger
becomes a table of numbers nobody can reason about. There is no single-amount
"transactions" table called a ledger.

The balance invariant is enforced three times, deliberately: the aggregate refuses
to post unbalanced lines, the F# kernel computes the totals, and PostgreSQL checks
the same arithmetic again at commit through a **deferred constraint trigger** —
deferred because the invariant spans rows that arrive within one transaction, and
a row-level check could not see them. An invariant this consequential should not
rest on one code path.

`JournalEntry` and `JournalLine` carry `BEFORE UPDATE OR DELETE` triggers refusing
changes to a posted entry, and `payments` carries one freezing amount, currency and
received date. A correction is a reversing entry beside the original, and both
stay readable for ever.

### 9. Balances are projections, never columns

No balance, status, overdue flag, unapplied residual or collected figure is
stored. `Receivable.Outstanding`, `Receivable.Status`, `Receivable.IsOverdue`,
`Payment.Unapplied`, `CommissionEntitlement.Collected` and every account balance
are computed from the rows that recorded the events, through the F# kernel.

Two facts that can disagree do not exist. A stored `IsOverdue` would be wrong
every midnight; a stored balance would drift from its allocations under exactly
the concurrency the system is most likely to see.

### 10. Nothing is auto-matched, and residuals stay residual

A payment records what somebody observed. Anything not allocated stays
**unapplied**, is reported back in the response, and appears on the finance work
queue.

There is no auto-matching. Assigning a residual to whichever receivable looks
closest would be the system guessing at a payer's intent and then acting on the
guess, and the guess is invisible afterwards. Unapplied cash is posted to an
`UnappliedCash` clearing account and stays there until a person says what it is
for.

External references are **not assumed unique**. Two genuinely different payments
can carry the same remittance text, so a match is reported as a possible duplicate
and never used to refuse a record.

### 11. An unexplained gap stays unexplained

Reconciliation compares what was expected against allocations, recorded deductions
and write-offs, and reports one of four outcomes: `NotStarted`, `Reconciled`,
`Shortfall`, `Excess`.

It states the arithmetic and stops. Whether a shortfall is withholding, a bank
charge, a dispute or a mistake is something a person finds out. Inferring a tax
would produce a reconciled receivable and a fabricated tax record, and nobody
would look at either again. Nothing is labelled fraud, error or withholding
without a recorded deduction saying so, and nothing is ever silently marked
reconciled.

**There is no tax engine.** Deductions are facts somebody entered.

### 12. Truthful verbs, everywhere

AgencyOS **records** an invoice. It does not send one: there is no document store,
no transport, no email and no attachment anywhere in M9, and `HoldsDocument` is
returned as `false` rather than omitted.

The vocabulary is enforced by the palette test: no finance command may say send,
collect, chase or match. Record Invoice, Record Payment, Allocate Payment,
Reconcile Receivable, Write Off.

**AgencyOS assigns no invoice numbers.** Invoice numbering carries statutory
weight that varies by jurisdiction, and the system is in no position to claim
compliance with any of them. The operator supplies the number they actually used;
it is unique per organization and required before an invoice can be marked issued.

### 13. An invoice is optional, and is not a payment

Four things are kept apart that systems routinely conflate:

- A contract obligation is not an invoice. Not every receivable is billed; a payer
  settling on a schedule the contract sets may never see one.
- An invoice is not a payment. `InvoiceStatus` is `Draft`, `Issued` or `Void`, and
  says nothing about money. There is no "Paid" status, because an invoice does not
  know.
- A payment is not revenue.
- A receivable outstanding is not cash.

Payment is inferred from a payment and from nothing else.

### 14. Financial history is immutable, and a write-off is not a deletion

Once recorded, a payment's amount, currency and received date are frozen: they are
what somebody observed on a statement, and observations are not edited. A typo is
corrected by reversing the payment and recording the right one, so both survive
with their own dates and actors.

**Write-off is a financial act**, with a reason and a posting to
`WriteOffExpense`. The receivable keeps its original amount and stays on the books
marked written off. Deleting it would destroy the evidence that the agency was
ever owed the money.

**Cancellation is a different act.** A receivable raised in error means the money
was never owed, so cancelling reverses the *original recognition entry* rather
than posting a loss. Treating the two the same would report a bad debt where there
was a typing mistake.

No posted financial fact is deleted through any application path.

### 15. Finance refuses rather than redacts

This is the one place M9 departs from the M7 and M8 pattern.

Removing rows from a list of contract terms leaves a shorter list, which is
honest. Removing rows from an arithmetic report leaves a **wrong answer presented
as a right one**: a balance with three allocations hidden is not a partial view of
the balance, it is a different number, and somebody will act on it.

So every finance read authorizes and returns everything, or authorizes and returns
nothing at all. There is no partially-visible receivable, no half-redacted ledger,
and no commission figure with the rate removed.

The one exception is the finance command centre, which drops the whole commission
**section** for a reader without `finance.commissions.read` rather than refusing
the page. That removes a section, not rows inside an arithmetic.

### 16. Finance permissions are disjoint from commercial ones

Nine new grants: `finance.read`, `finance.write`, `finance.payments.read`,
`finance.payments.write`, `finance.commissions.read`, `finance.commissions.write`,
`finance.ledger.read`, `finance.ledger.post`, `finance.adjustments.write`.

Holding `deals.economics.read` — which lets somebody see what a deal pays —
confers **no** finance permission whatsoever. Knowing what was negotiated and
knowing what the agency has collected are different disclosures with different
readerships. Owning a record, sitting on a team or being named on a representation
grants no implicit finance access either.

`finance.ledger.post` is deliberately narrow and absent from the Member role.
Every other posting in AgencyOS is a consequence of an act already authorized and
is made by the system inside the same transaction; writing an entry nothing else
produced is the one act that needs its own grant.

### 17. No revenue recognition policy is implemented

M9 records cash movements and commission earned on collected funds. It does **not**
implement GAAP or IFRS revenue recognition, and it does not claim to. Cash
collected is not silently equated with recognized revenue: the accounts are named
neutrally (`CommissionRevenue`, `ClientFundsPayable`) and the milestone asserts
only what it can defend.

Having double entry is not compliance with anything. A recognition policy, if one
is ever needed, is a later decision with an accountant in the room.

### 18. Three kinds of immutability, for three different reasons

Kept distinct on purpose, because they are answers to different questions:

- **Append-only audit** (ADR-0006) answers a security question in a security
  vocabulary: who did what, under which permission.
- **M7 offer immutability** (ADR-0021) freezes what was proposed, because somebody
  read it and formed a view on it.
- **M8 contract version immutability** (ADR-0022) freezes what a draft said,
  because a later draft is a new fact rather than a correction.
- **M9 posted-entry immutability** freezes what the books say, because the books
  are what somebody relied on when they reported a figure.

Audit is never used as a substitute for the journal, and raw audit is never
exposed as a finance timeline. `FinanceEvent` is the curated history: one business
act, one entry, however many rows it wrote (ADR-0012).

### 19. Search and telemetry carry no figures

No amount, balance, commission rate, bank reference or privileged finance note is
indexed. The invoice search vector covers the reference only. There is no
OpenSearch.

No telemetry counter carries a figure either. What is counted is that an act
happened and what shape it had: a direction, a method, a currency code, an outcome
category. Telemetry is exported to places that hold no finance permission, so a
counter tagged with the number would be the leak.

### 20. A second F# rules kernel, and why

`AgencyOS.Finance.Rules` is a new project rather than a module inside
`AgencyOS.Deals.Rules`. The two govern different things — deal comparison and
contract deadlines on one side, money arithmetic and double entry on the other —
and a change to commission rules should not force a rebuild of the reconciliation
kernel, nor share its vocabulary.

The choice of F# is the same one ADR-0021 made and for the same reason: total
functions over closed unions, arithmetic that a reviewer can read in one sitting,
and no partial cases. It is not language enthusiasm; the alternative was writing
the rounding policy five times in five handlers.

The boundary is unchanged: a single C# static facade over primitives and
`[<CLIMutable>]` records, so the domain never references FSharp.Core types.

### 21. All M9 writes and reads are ONLINE_ONLY

See `docs/13_OFFLINE_CLASSIFICATION.md`. Nothing is queued and nothing is cached,
**reads included**, which is stricter than every previous milestone.

A balance computed from a copy that is four hours stale is not a slightly old
balance. It is a different number, and the person reading it has no way to tell
which one is in front of them. There is no last-write-wins anywhere: every guarded
write carries the version the caller observed.

### 22. No broker, no Temporal, no vector store, no TLA+

The four questions, asked again:

1. **What durable, multi-step, long-running process exists?** None. Every finance
   operation is a single transaction.
2. **What is being retried or compensated?** Nothing. There is no bank to call, no
   invoice to send, no statement to fetch.
3. **What would fail without it?** Nothing in M9. Ageing and work queues are
   queries over dates and derived balances.
4. **What is the operational cost?** A second failure domain and a second source
   of truth about money.

**Not adopted.** The question reopens when AgencyOS must talk to a bank or a
payment processor.

No TLA+ specification either: M9 introduces no distributed protocol. Its
state machines are small and are proved by enumerating every state and trigger in
unit tests. `specs/OfflineWriteQueue.tla` continues to govern synchronization,
which M9 does not change.

## Consequences

**Good.**

- The question a finance desk actually asks — "how much of this is left, and what
  did we earn on it" — is answerable from the rows, and the answer cannot drift
  from them.
- Client money and agency money are separated all the way through the ledger, so
  agency revenue is agency revenue.
- History survives. Nothing posted is edited or deleted; corrections sit beside
  what they correct.
- What the system does not know, it says it does not know: unknown amounts,
  unresolvable dates, unexplained variances.

**Costs, accepted.**

- Balances, statuses and collected commission are recomputed on every read. Batch
  loaders keep this to a fixed number of queries per page, and correctness is
  worth more than the saving a stored column would give.
- Multi-currency reporting is a list rather than a figure, which is less
  comfortable and more honest.
- A person must explain every variance. The alternative is a system that explains
  them incorrectly.
- Finance refusals are blunt: a caller without the grant sees nothing rather than
  a partial view.

**Explicitly out of scope.** Document storage and invoice PDFs, sending anything,
Outlook and Gmail, statement and remittance ingestion — all M10. AI cash
forecasting, revenue prediction, agency valuation and deal-quality scoring — not
M11 or M12 either; they are claims about the future and M9 records what happened.
Tax computation, FX conversion, payroll, and GAAP or IFRS revenue recognition
remain unimplemented and unclaimed.

## References

- `prompts/007_M8_M9_CONTRACTS_FINANCE.md`
- `src/AgencyOS.Finance.Rules/Money.fs`, `Allocation.fs`, `Ledger.fs`,
  `Commission.fs`, `Reconciliation.fs`, `Rules.fs`
- `src/AgencyOS.Domain/Finance/`
- `src/AgencyOS.Application/Finance/LedgerPosting.cs`,
  `FinanceQueryService.cs`
- `src/AgencyOS.Infrastructure/Persistence/Migrations/20260908071739_FinanceLedgerAndCommissions.cs`
- `tests/AgencyOS.Tests.Unit/Finance/FinanceKernelTests.cs`
- `tests/AgencyOS.Tests.Integration/FinanceTests.cs`
