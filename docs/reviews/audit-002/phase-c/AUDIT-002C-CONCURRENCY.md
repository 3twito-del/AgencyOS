# Audit 002 Phase C — stale version and conflict

§10 of the Phase C brief, §14 of the original §30 gate. Cumulative across phases.

Two callers against one aggregate: A reads version *n*, B writes and moves it to
*n+1*, A submits its stale *n*. What matters is not that the server refuses —
that is what optimistic concurrency is for — but what it says, and whether a
client could turn that into something a person can act on.

---

## Results

| Aggregate | Phase | Both read | B wrote | A's stale submit |
| --- | :-: | ---: | ---: | --- |
| Contract effective date | B | v9 | 204 | **409** |
| Project stage | B | v2 | 204 | **409** |
| Project stage | C | v1 | 204 | **409** |

Verbatim:

```
Contract '01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9' has changed since you last saw it
(you had version 9, it is now 10).

Project '01a0a2c2-0471-7156-a822-fac9ecc8645c' has changed since you last saw it
(you had version 1, it is now 2).
```

**The message names both versions.** A client has everything it needs to offer
"reload and try again" without guessing, which is the property that makes ADR-0014
usable rather than merely correct.

---

## A refusal is not memoised

The case that matters most and is easiest to get wrong: a conflict replayed under
the same idempotency key.

```
POST projects/{id}/stage   expectedVersion 0, key K   ->  409
POST projects/{id}/stage   expectedVersion 0, key K   ->  409
```

**Assessed against the idempotency contract, not against intuition.** ADR-0014
scopes idempotency to *effects*: a key exists so that a command which succeeded
is not applied twice. A refusal applied nothing, so there is nothing to
deduplicate, and the second request is evaluated against the aggregate's current
state rather than against a cached answer.

That is the correct behaviour and the safe one. If a refusal were memoised, a
client that corrected its version and retried under the same key would be told it
had failed when it would now succeed — the system would be lying about the
present to be consistent about the past.

**NO_DEFECT.** Recorded as a positive observation rather than a finding.

---

## The identifier in the message

The 409 names an aggregate by an identifier the operator has never seen anywhere
in the client. That is `AOS-R002-007`, filed in Phase B, **CONFIRMED** and
unchanged. It is a copy problem, not a concurrency problem, and it is the same
shape as `AOS-R002-008` and `AOS-R002-014`.

---

## What was not covered

Honest limits rather than implied completeness:

- **No test forced two writes into the same millisecond.** What was observed is
  optimistic concurrency doing its job, not proof of behaviour under genuine
  contention. That needs a load harness, which this is not.
- **Three aggregates, not all of them.** Contract effective date and project
  stage. Both refuse correctly and both name the versions; nothing suggests the
  mechanism differs elsewhere, and nothing here proves it does not.
- **Deletes and revocations were not conflict-tested.** Membership revocation is
  the one that matters and is covered by `MembershipAdministrationTests` at the
  integration level, including the last-owner invariant.

## Findings

None.

## Evidence

- `artifacts/reviewer/run-002-phase-c/idempotency-conflict.json`
- `docs/reviews/audit-002/phase-b/AUDIT-002B-CONCURRENCY.md`
