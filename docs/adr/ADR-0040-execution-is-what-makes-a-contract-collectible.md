# ADR-0040 — Execution is what makes a contract collectible

- **Status:** Accepted
- **Date:** 2026-09-19
- **Milestone:** Operational-alpha correctness repair (not a milestone)
- **Supersedes:** ADR-0023 §3 (only that decision; the rest of ADR-0023 stands)
- **Builds on:** ADR-0022 (contract model, recorded rather than performed),
  ADR-0023 (finance: money, commission and the ledger)

## Context

The operational-alpha product-hypothesis evaluation exercised the whole chain
from a pursuit to an executed contract on ALPHA 0.1.0 (`d8a8bc56`) with synthetic
data, and demonstrated two things the model permitted that it should not.

**A contract could be signed with no paper.** `ContractSignature` carries no
version of its own, and nothing required a `ContractVersion` to exist before a
signature was recorded. So a contract with zero versions accepted signatures,
derived `PartiallyExecuted` from the first required one and `Executed` from the
last, and then — because `acceptsNewVersions` is false for `Executed`, and the
only transitions out of `Executed` are to `Superseded` and `Terminated`, neither
of which accepts versions either — could never be given a version at all. A
monetary obligation must name a `ContractVersionId`, so such an instrument was
permanently unable to carry its own money. The recovery window closes at the
final signature: `PartiallyExecuted` can still return to drafting.

**An effective date alone made a draft collectible.** ADR-0023 §3 defined
operative as "executed, **or** carrying a recorded effective date, and not
abandoned or superseded". The evaluation recorded a 450,000 USD obligation and an
open receivable against a contract in `Draft` status that nobody had signed,
because somebody had typed an effective date on it.

Both are correctness defects rather than product gaps: the finance chain itself
is implemented and works, and the evaluation proved it end to end once the
contract was executed properly.

## Decisions

### 1. A signature requires recorded paper

An execution-driving signature is refused unless the contract has at least one
recorded `ContractVersion`. The refusal happens in the handler, before the
aggregate is touched, so a refused signature leaves no state behind.

The signature model does not associate a signature with a particular version, and
this decision does not add one. The check is that paper exists, not which sheet
was signed. If a future requirement needs a signature to name its version, that
is a model change with its own decision.

> "This contract has no recorded version to sign. Record the contract version
> before recording signatures."

### 2. Operative means executed

A collectible `MonetaryObligation` requires:

- a valid `ContractVersion`, **and**
- contract status `Executed`.

An effective date is neither necessary nor sufficient for collectibility.

### 3. An effective date keeps its own meaning

An effective date answers *from what date do the agreement's terms apply*. It
does not answer *is there operative paper to collect under*. The two remain
separate and independently recorded:

- effective dates may still precede execution, follow it, or be set
  retroactively;
- nothing forces `EffectiveOn` to equal `ExecutedOn`;
- legal enforceability is never inferred from a date.

ADR-0022's position that a contract is *recorded rather than performed* is
unchanged: AgencyOS still describes what happened elsewhere.

### 4. Agreed terms remain representable before execution

This decision narrows what makes money collectible. It does not restrict what may
be recorded before execution. Draft offers, accepted offers, contract versions,
contract terms and effective dates all keep their existing semantics and may
exist on an unexecuted contract.

The distinction the model now holds is the one the domain always meant:

> agreed terms ≠ a collectible legal obligation

No "planned obligation" concept is introduced. M9's `ObligationAmountKind` already
carries `Unknown` for an amount nobody can value, and that is a different problem.

### 5. Unsigned-but-binding is not modelled, and is not proxied

Real transactions can be legally operative without full execution — a deal memo,
a letter agreement, performance under an unsigned draft. AgencyOS does not model
that today, and this decision deliberately does not invent it.

If it is needed, it must be modelled explicitly, as its own basis for
operativeness with its own recorded evidence. It must not be expressed by putting
an effective date on a draft, which is exactly the proxy this ADR removes. For
the present ALPHA, execution is the only accepted basis.

## Consequences

- An executed contract can no longer exist without paper, so the stranded
  instrument the evaluation produced is unreachable.
- A draft with an effective date now refuses obligations. Any workflow that
  relied on that path must execute the contract first, or record the obligation
  against the instrument that is actually operative.
- Contracts that were once executed and are now `Terminated` no longer accept
  **new** obligations. Obligations already recorded under them are untouched.
- The two invariants are pinned by `ContractOperativenessTests`, which proves the
  refusals happen before state changes and that the lawful lifecycle still
  succeeds end to end into a receivable.

## Alternatives considered

**Associate each signature with a contract version.** More faithful — a signature
really is against one sheet of paper — and it would make "which version was
executed" answerable. Rejected for now as larger than the defect: it changes the
signature model and the contract schema, and the demonstrated defect is fully
closed by requiring that paper exist.

**Keep effective date as a sufficient basis and warn.** Rejected. A warning that
does not refuse is a number in a total that nobody checked, which is precisely
what ADR-0023 exists to prevent.

**Treat an effective date as operative only when the contract is at least
`ApprovedForExecution`.** Rejected as a half-rule: it would still let money attach
to an instrument nobody had signed, and it invents a threshold the domain does not
otherwise use.
