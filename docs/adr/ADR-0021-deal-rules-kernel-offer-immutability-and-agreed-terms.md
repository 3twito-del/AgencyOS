# ADR-0021: An offer is an immutable record, terms agreed is not a contract, and the rules are F#

Status: Accepted
Date: 2026-09-07

## Context

M7 is where money enters the system. That changes the failure modes: every earlier
milestone recorded facts an agency could correct by editing a row, and this one
records what the parties proposed to each other, which is a thing that happened and
cannot be corrected by pretending it happened differently.

Four ways to get it wrong, all common:

- treating a counter as an edit, so the negotiation's history becomes only its
  latest state;
- letting "terms agreed" and "contract signed" blur, so the system claims a
  document nobody has;
- storing the economics as prose, so nothing can compare two offers or reconcile
  them against a contract later;
- using binary floating point, or a bare number without a currency, so the figures
  are subtly wrong before anybody reads them.

M6 stopped deliberately short of all of this. `OpportunityTargetStage.Advanced`
exists precisely as the boundary, and no M6 record models terms.

## Decision

### The negotiation is the chain of offers, and no link is ever rewritten

An `Offer` is one concrete proposal at one moment. A counter is **another offer**
with `RespondsToOfferId` pointing at the one it answers, never an edit of it.

`OfferStatus` is `Draft → Open → Accepted | Rejected | Withdrawn | Expired |
Superseded`. A draft is a working document whose terms may be edited; everything
else is a record of something that happened, and its terms are frozen from that
moment.

Rejected, Withdrawn and Expired are three different facts about different actors -
the recipient said no, the proposer pulled it, a stated lapse arrived - and none of
them is "nobody replied". Nothing expires because time passed: an offer must have
carried an expiration, and that expiration must have arrived, before it can be
recorded as expired. M6 established that silence is derived and never stored, and
the same holds here.

A correction to a mis-recorded offer is another offer superseding it, so both what
was written and what it was corrected to survive.

### Immutability is enforced twice, and it is not the audit trail

Two different things, deliberately not conflated. The audit log is append-only and
answers who did what under which permission. Offer immutability stops a recorded
commercial snapshot from being rewritten at all.

- The domain refuses to add, change or remove a term unless the offer is a draft,
  and `OfferTerm` has no post-construction setters.
- PostgreSQL refuses independently: a `BEFORE UPDATE OR DELETE` trigger on
  `offer_terms` and a `BEFORE UPDATE` trigger on the frozen `offers` columns raise
  once the offer has left draft. Status, version and timestamps stay writable,
  because moving through the lifecycle is the point.

The triggers are deliberately dumb. They encode no rule beyond "a recorded offer is
frozen", which the domain states first and more legibly; they exist so a future
code path that forgets fails loudly rather than quietly changing what the agency is
recorded as having proposed. Where the milestone brief asks not to put business
logic in opaque SQL and also asks for database-level protection, this is the line:
the rule lives in the domain, the guardrail lives in the database.

There is consequently **no factory that produces an already-frozen offer**. Every
offer starts as a draft and is recorded, including one written down after the call,
because terms can only be added while it is editable and a second entry point would
have to duplicate the term rules or bypass them.

### One canonical thread per deal

At most one standing offer and at most one accepted offer per negotiation, both
enforced by partial unique indexes:

```sql
CREATE UNIQUE INDEX ux_offers_one_open_per_deal ON offers (deal_id) WHERE status = 2;
CREATE UNIQUE INDEX ux_offers_one_accepted_per_deal ON offers (deal_id) WHERE status = 3;
```

Recording an offer supersedes whatever was on the table, which is what a counter
does in the world. Branching is not supported and is not silently tolerated: two
standing offers is a refusal, not a shape the reader has to interpret. Order is a
canonical `Sequence`, never inferred from identifiers - version 7 GUIDs happen to
sort by creation time, and a thread that leaned on that would reorder the day the
identifier scheme changed.

"An offer answers one in the same negotiation" is a foreign key over the triple
(organization, deal, answered offer) rather than a check, because a check cannot
see another row.

### TermsAgreed is a commercial fact and says nothing about a contract

`DealStatus` is `Draft → Negotiating → TermsAgreed | NoDeal | Cancelled`, with
reopening from `TermsAgreed` and `NoDeal`. There is deliberately no Signed,
Executed, Paid or Commissioned: M7 has no way to substantiate any of them, and a
status that claimed one would be read as the agency saying so.

Two transitions are **not requestable**. A deal reaches `Negotiating` because an
offer was recorded, and `TermsAgreed` because one was accepted. The status command
accepts only `NoDeal` and `Cancelled`, so a deal cannot claim agreed terms with no
accepted offer behind it - the single invariant this milestone exists to protect.

Reopening supersedes the accepted offer rather than editing it. What was agreed in
March survives exactly, and the timeline records both the acceptance and the
unwinding.

### The accepted offer is derived, not stored

`Deal` carries no `accepted_offer_id`. The agreement is the offer whose status says
so, unique by index. A column would be the same fact written twice, and M6's rule
holds: two facts that can disagree are one fact too many. ADR-0019 accepted a
denormalization only because PostgreSQL forbade the alternative; here it does not.

The counterparty is likewise read through the M6 target rather than copied, and the
subject through the opportunity. M7 proved no need for a frozen deal-specific
snapshot, so it does not build one.

### Commercial terms are structured, with one controlled vocabulary

`OfferTerm` is a term code, a value kind, and an exclusive arc of typed columns,
with a CHECK asserting the shape matches the kind and that money carries a
currency. Narrative notes stay on the offer; an offer stored as prose is an offer
nobody can diff, and M8 could not reconcile it against a contract.

`DealTermCatalog` is the single authoritative description of every supported term -
display name, value kind, allowed units, applicable deal kinds, bounds and
sensitivity. Completeness is tested structurally rather than reviewed: every code
has a definition, every definition names a real code, every value kind is one the
rules kernel can parse, and every money or percentage term is classified economic.
This is the defence M5's saved-view filters lacked when they lost seven fields.

The vocabulary stops where administration begins.
`OptionPeriodCompensation` records what an option period was proposed to pay,
because that is a negotiated number; nothing here says when an option must be
exercised, by whom, on what notice, in which territory, or with what exclusivity.
Those are obligations and rights, and they are M8's.

### Money is an amount and a currency, in decimal

No binary floating point anywhere, and a test walks `Money`'s public surface to
prove there is no `double` on it. Amounts are held to the currency's own minor
units, so a JPY figure is not rounded to two places and a KWD figure is not
rounded to two either.

`CurrencyCode` validates against a curated ISO 4217 table carrying minor units. It
is a working set covering the currencies a film and television agency plausibly
transacts in, not all of ISO 4217: an unknown code is refused rather than stored,
and adding one is a row. Cross-currency amounts are never converted, and a
comparison across currencies reports no direction, because M7 holds no exchange
rates and inventing one would produce a difference somebody acts on.

### The rules are an F# kernel behind a narrow boundary

`AgencyOS.Deals.Rules` is a pure F# library with no package references but
FSharp.Core - no EF, no HTTP, no logging, no clock, no filesystem. It owns deal and
offer transition legality, negotiation-chain validation, term-value parsing, and
offer comparison.

This is the workload the language was reserved for, and it earns its place here
specifically because discriminated unions remove states rather than merely
describing them. `TermValue` cannot be constructed as money without a currency;
transitions are named by their cause, so the two that must not be requestable
cannot be requested; and FS0025 makes an added state a build failure rather than a
rule somebody forgot to update.

The boundary is one class. Everything crossing it is a primitive, a plain array or
a `[<CLIMutable>]` record; discriminated unions, options and F# lists stay inside.
States cross as their persisted integers, and C# owns its own enums - the kernel
decides what is legal, not what things are called. Fifteen tests walk every state,
trigger, direction and value kind in both directions, so a value added on either
side fails the build rather than silently losing a rule.

Two integration costs are worth recording:

- The SDK's implicit FSharp.Core reference resolves to the compiler's own copy
  inside the SDK directory. It compiles, and then every consuming project fails at
  run time with a missing assembly, because the file is never copied. The fix is
  `DisableImplicitFSharpCoreReference` plus an ordinary package reference.
- `LangVersion` had to be scoped to `.csproj`: FSC rejects a langversion of 14.0
  outright.

Both are one-line changes, and neither is a reason to keep the rules in C#.

### Comparison answers "what changed", never "is this good"

`Added | Removed | Changed | Unchanged` per term, with a direction of
`Increased | Decreased | Level | NotComparable` where the values are comparable at
all. Movement, never merit: higher compensation is good for the client and a longer
term usually is not, and which is which depends on the term, the side and the deal.
The vocabulary is asserted by test to contain no better, worse, improved,
favourable or score.

Terms are unique per offer, so the join is unambiguous. Two bonuses are two codes
or a labelled `OtherTerm`.

### Economics and strategy are separate grants, and comparison is the one refusal

Four permissions beyond read and write: `deals.read`, `deals.write`, `offers.read`,
`offers.write`, plus `deals.economics.read` and `deals.strategy.read`.

Strategy follows the established rule: absent rather than refused, and absent is
indistinguishable from empty. Economics is the sharper one - without it, every
money and percentage term is removed from every read. Structural terms survive,
because an assistant scheduling around a start date has no reason to see the fee,
and an agency forced to choose between showing them everything and showing them
nothing will show them everything. The catalog makes that split safe by classifying
every money-bearing term as economic, and a test enforces it.

**Comparison is refused rather than redacted.** A diff with the economic rows
stripped would report that nothing changed when the guarantee moved by a hundred
thousand, and a reader would act on it. An absence can be honest; a false answer
cannot.

Neither strategy nor any term value is indexed. The deal search vector is name,
reference and factual summary, so a compensation figure cannot be confirmed by
searching for it and watching a deal surface. Saved views carry no economic filter
and no economic sort, because a saved view is a query somebody else may run.

Telemetry counts identifiers, operation types and outcomes, and never an amount.
A counter tagged with the figure would be a second copy of the economics with none
of the permissions guarding the first.

### Quote is deferred, explicitly

No `TalentQuote` in M7. No workflow consumes one: offer terms are entered directly,
and a provenance-carrying historical quote ledger with no reader is exactly the
speculative infrastructure `CLAUDE.md` section 5 forbids. The condition for
revisiting is an offer-preparation surface that shows "their last quote was X",
together with a stated rule for where quotes come from.

### Everything is online-only

No M7 read is cached and no M7 write is queued; the local cache schema stays at
v2. A stale offer or acceptance replayed hours later causes commercial harm in the
world that no later synchronization repairs, and unlike a stale pipeline the user
has usually already acted on it.

## Consequences

- The negotiation's whole history survives, so "what did they actually offer in
  March" is answerable after any number of counters and one reopen.
- A deal cannot claim agreed terms without an accepted offer behind it, in the
  domain, in the rules kernel and in the database.
- Every figure is `decimal` with a currency, precise enough for M9 to consume
  without re-deriving anything.
- M8 attaches a contract to a deal and its accepted offer, and can compare
  negotiated terms against drafted ones, because the negotiated terms are typed
  rows rather than prose.
- The immutability triggers are the first business-adjacent logic in the database
  outside the audit guards. They are narrow and tested, and they are the thing to
  watch if the schema evolves.
- Term codes are unique per offer, so two bonuses need two codes. If that proves
  restrictive, the diff's join key is what has to change.
- The currency table is curated, so an agency transacting in something outside it
  is refused until a row is added. Refusing is the safe direction.
- An F# project is now in the build. It is 5 files and one boundary class, and the
  two integration costs above are recorded so the next person does not rediscover
  them.

## Evidence that would cause reconsideration

- A workflow where a recorded offer genuinely must be corrected in place - which
  would mean supersession is too heavy, not that immutability is wrong.
- Parallel proposals to one counterparty being ordinary rather than exceptional,
  which would mean the single-thread rule is drawn too tightly.
- Derived offer facts becoming a measurable read-path problem at real volume,
  which would justify a maintained projection - a projection, never a column on
  the aggregate.
- The F# boundary needing types richer than primitives and flat records, which
  would mean the kernel has grown past rules into architecture and should be
  reconsidered rather than widened.
- An M8 contract model that cannot reconcile against these terms, which would mean
  the term vocabulary was shaped for the wrong consumer.
