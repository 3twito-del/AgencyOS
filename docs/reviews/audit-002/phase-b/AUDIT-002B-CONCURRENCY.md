# Audit 002 Phase B — stale version and conflict

§12, previously `NOT_REVIEWED`.

Two controlled callers against one aggregate. A reads version N, B writes and
moves it to N+1, A submits its stale N. What matters is not that the server
refuses — that is what optimistic concurrency is for — but what it says, and
whether a client could turn that into something a person can act on.

---

## Results

| Aggregate | A read | B wrote | A's stale submit | Verdict |
| --- | ---: | ---: | --- | --- |
| Contract effective date | v9 | v10 | **409** | refused correctly |
| Project stage | v2 | v3 | **409** | refused correctly |

Both messages, verbatim:

```
Contract '01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9' has changed since you last saw it
(you had version 9, it is now 10).

Project '01a0a1fe-3d6d-7135-bf69-f49a32b98025' has changed since you last saw it
(you had version 2, it is now 3).
```

## What is right about this

- **The status code is correct.** 409, not 400 and not 500.
- **The sentence is English.** "has changed since you last saw it" is what
  happened, said the way a person would say it. It is not `DbUpdateConcurrencyException`
  and it is not "optimistic concurrency violation".
- **No stale write was accepted.** In neither case did the server take the older
  version.
- **The concurrency check is what stops a double submission** on a versioned
  aggregate — see the idempotency report.

## What a repair wave should look at

1. **The identifier is in the message and means nothing to the reader.** "Contract
   '01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9'" — the reader is looking at that
   contract; it has a title. Recorded as `AOS-R002-007`.
2. **The version numbers are internal.** "you had version 9, it is now 10" is
   honest and is also the kind of detail §13 asks whether it leaks awkwardly. It
   is borderline: it explains *why* without saying *what to do*.
3. **No message says what to do next.** None of them says "reload and try again",
   and the audit did not establish whether the client offers a reload path,
   because that needs the dialog, which needs §6's VALID-state work.

## What was not established

- **Whether a dialog surfaces the 409 usefully.** No dialog was submitted with a
  stale version, because no dialog was submitted at all — see the coverage report.
  So "does the dialog imply success", "do local edits survive" and "is there a
  reload path" are **NOT_REVIEWED**.
- **A third aggregate.** A thesis revision was attempted and the setup write was
  refused for an unrelated reason (a missing `confidence`), so that case produced
  no conflict evidence and is not counted.

Two aggregates is a sample, not coverage. It is enough to say the server side of
§12 behaves, and not enough to say anything about the client side.
