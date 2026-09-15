# Audit 002 Phase C — double submission and stale version, across domain shapes

§9 and §10. Phase B answered both on two aggregates and said so. That showed the
mechanisms exist; it could not show they are applied consistently, which is the
question this phase was opened for.

---

## §9 — the same command arriving twice

Two different things, kept apart, because conflating them is how a system gets
called safe for the wrong reason.

1. **A retry.** The same logical command, the same idempotency key, because the
   first response was lost.
2. **A double-press.** The same content, a *fresh* key each time, because the
   client mints one per press.

Records were counted before and after each pair, by a marker unique to the probe.

| Domain | Route | Retry (same key) | Records added | Same id returned | Double-press (fresh keys) | Records added |
| --- | --- | --- | ---: | --- | --- | ---: |
| People | `people` | 201 / 201 | **1** | **yes** | 201 / 201 | 2 |
| People/Companies | `companies` | 201 / 201 | **1** | **yes** | 201 / 201 | 2 |
| Projects/Packages | `projects` | 201 / 201 | **1** | **yes** | 201 / 201 | 2 |
| Relationships/Tasks | `tasks` | 201 / 201 | **1** | **yes** | 201 / 201 | 2 |
| Intelligence | `intelligence/sources` | 201 / 201 | **1** | **yes** | 201 / 201 | 2 |

**Five of five safe**, and the mechanism is the same one in each: the second
request returns the first request's response, including its identifier, and
nothing is written twice.

The double-press column creating two records is **correct**. A fresh key is a
statement that this is a new intent. The client's behaviour — minting one key per
press — is what makes that true, and is where a double-click would have to be
handled if it should be. That is not a server defect and is not filed as one.

### A counting error, corrected

The first reading counted the whole listing and reported `records added: 0` for
People after two `201`s. People returns at most a page; the create had worked and
the page was already full. Corrected by counting only records carrying the
probe's own marker. Nothing was reported from the first reading.

---

## §10 — a stale version

Two callers against one aggregate: A reads version *n*, B writes and moves it to
*n+1*, A submits its stale *n*.

| Aggregate | Both read | B wrote | A's stale submit |
| --- | ---: | ---: | --- |
| Project stage | v1 | 204 | **409** |
| Contract effective date (Phase B) | v9 | 204 | **409** |
| Project stage (Phase B) | v2 | 204 | **409** |

Verbatim:

```
Project '01a0a2c2-0471-7156-a822-fac9ecc8645c' has changed since you last saw it
(you had version 1, it is now 2).
```

The message names both versions. A client has everything it needs to offer
"reload and try again" without guessing.

### A refusal is not memoised

The case that matters most and is easiest to get wrong: a conflict replayed under
the same idempotency key.

```
POST projects/{id}/stage   expectedVersion 0, key K   ->  409
POST projects/{id}/stage   expectedVersion 0, key K   ->  409
```

**The second attempt is refused again rather than being served the first
refusal from a cache, and it is refused for the current reason.** If a refusal
were memoised, a client that fixed its version and retried under the same key
would be told it had failed when it would now succeed. It is not.

---

## What was not covered

Honest limits rather than implied completeness:

- The retry test covers **creates**. Versioned state transitions were tested for
  conflict but not for retry in every domain; the one that was —
  `projects/{id}/stage` — behaves correctly.
- No test forced two writes to land in the same millisecond. The behaviour
  observed is optimistic concurrency doing its job, not proof of the isolation
  level under genuine contention. That needs a load harness, which this is not.
- Deletes and revocations were not double-submitted. Membership revocation is
  the one that matters and is covered by
  `MembershipAdministrationTests` at the integration level.

---

## Findings

None. Both mechanisms behave correctly on every shape sampled, and the one
plausible trap — a memoised refusal — is not present.

## Evidence

- `artifacts/reviewer/run-002-phase-c/idempotency-conflict.json`
