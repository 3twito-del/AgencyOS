# Audit 002 Phase B — double submission

§11, previously `NOT_REVIEWED`.

Two different questions, kept apart, because conflating them is how a system gets
called safe for the wrong reason.

1. **A retry.** The same logical command arrives twice carrying the same
   idempotency key, because the first response was lost.
2. **A double-click.** The same request arrives twice with a *fresh* key each
   time, because the client mints one per press.

Persisted records were counted after each pair. Two 201s are not evidence of
safety.

---

## A harness error, corrected first

The first pass sent `Idempotency-Key`. The product's header is
`X-AgencyOS-Idempotency-Key` (`ClientHeaders.IdempotencyKey`). With the wrong
name the filter never saw a key, both submissions created records, and the
result looked like a product defect. It was mine.

**Nothing was reported from that pass.** It is recorded here because §22 requires
it, and because it is exactly the class of error that would have produced a
confident, wrong S2.

---

## Results

| Case | Keys | HTTP | Records added | Same id returned | Verdict |
| --- | --- | --- | ---: | --- | --- |
| Unversioned create, retried | same key twice | 201 / 201 | **1** | **yes** | **SAFE** |
| Unversioned create, two presses | fresh key each | 201 / 201 | **2** | no | correct, and see below |
| Versioned create, retried | same key twice | 201 / **409** | 1 | n/a | **SAFE** |
| Versioned create, two presses | fresh key each | 201 / **409** | 1 | n/a | **SERVER_PROTECTED_BUT_UI_CONFUSING** |

### The retry case is properly handled

Sending the same key twice to `POST intelligence/sources` created **one** record
and returned **the same identifier** both times. The idempotency filter replays
rather than duplicating. That is the behaviour ADR-0014 describes and it works.

### Two presses with fresh keys create two records — and that is correct

A fresh key means a new logical command. The client's own contract says so:

> Stable across every retry of this command … *Null for an interactive request
> the user can simply repeat.*

The Windows client mints `Guid.NewGuid().ToString("N")` at each call site, inside
the click handler. The audit found **no retry policy** in the client — no Polly,
no automatic resubmission — so a fresh key per press is consistent: every press is
a user repeating themselves deliberately.

A `ContentDialog` also closes on its primary button, so the same dialog cannot be
double-clicked into two submissions.

### The confusing case

On a **versioned** aggregate the second submission is refused by optimistic
concurrency, not by idempotency, and the reader is told:

```
Project '…' has changed since you last saw it (you had version 2, it is now 3).
```

That sentence is true and it is not what happened from the user's point of view.
They submitted the same thing twice; they are told the record changed underneath
them. Recorded as `AOS-R002-007` together with the identifier-in-message problem.

---

## What was not established

- **Rapid double-click, Enter twice, and submit-while-in-flight at the UI.** These
  need a dialog filled in and submitted, which this audit did not do. The
  button's state during a request in flight is **NOT_REVIEWED**.
- **A delayed response followed by a retry.** Not exercised; it would need the
  server held open deliberately.
- **Whether a dialog that closes before a failure arrives can be reopened and
  resubmitted.** **NOT_REVIEWED**.

So §11 is sampled at the API boundary, where the mechanism lives, and is not
answered at the dialog, where the confusion would be felt.
