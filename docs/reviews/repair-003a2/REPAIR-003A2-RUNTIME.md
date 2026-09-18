# Repair Wave 003A.2 — runtime evidence

Both workflows were driven through the real Windows interface, on a machine that
is not on UTC, before and after the repair. The full run is under
`artifacts/reviewer/run-repair-003a2/` (untracked); the files cited are copies in
[`evidence/`](evidence/).

## Environment

| | |
| --- | --- |
| Host zone | **Israel Standard Time, UTC+03:00, daylight saving active** |
| Client | `AgencyOS.Windows.exe`, Debug, unchanged by this wave |
| API | local, Development, fake providers only |
| Database | local LAB cluster — runtime evidence only; integration is authoritative on CI `postgres:18.6` |
| Wire capture | a local recording proxy between the client and the API, so the exact JSON the client sent is evidence rather than inference |
| Client environment | minimal, as introduced after the 003A.1 dump incident |
| Secrets | no dump copied; no connection string or token appears in any captured file |

---

## 1. Before — `a728324`

Both saves were driven from the dialogs: material chosen with the keyboard, text
typed, Enter pressed.

| | `RecordPitchDialog` | `RecordSubmissionDialog` |
| --- | --- | --- |
| Date chosen | the dialog's default, today | today |
| Control | `DatePicker` "When" | `DatePicker` "Sent" |
| Value on the wire | `"occurredAt":"2026-09-18T03:13:39.1646065+03:00"` | `"sentAt":"2026-09-18T03:14:06.579828+03:00"` |
| Also sent | — | `"responseExpectedBy":"2026-10-02"`, `"followUp":{"dueAt":"2026-10-02T03:14:06.5801846+03:00"}` |
| HTTP | **500** | **500** |
| Server | `ArgumentException` from `Npgsql…DateTimeOffsetConverter.WriteCore`, wrapped in `DbUpdateException` | the same |
| Persisted | nothing | nothing |
| UI | "That did not happen — Invalid request" (the copy is `AOS-R002-024`, out of scope) | the same |
| Classification | **`REPRODUCED`** | **`REPRODUCED`** |

`evidence/baseline-wire-requests.jsonl`, `evidence/baseline-npgsql-refusal.txt`,
`evidence/baseline-ui-run.json`.

## 2. After — the same dialogs, same machine

| | `RecordPitchDialog` | `RecordSubmissionDialog` |
| --- | --- | --- |
| Value on the wire | `"occurredAt":"2026-09-18T03:30:28.0094673+03:00"` | `"sentAt":"2026-09-18T03:30:59.2745122+03:00"` |
| HTTP | **201** | **201** |
| Stored | `2026-09-18T00:30:28.009467+00:00` | `2026-09-18T00:30:59.274512+00:00` |
| Same instant | yes | yes |
| Rendered at UTC+3 | **2026-09-18 03:30:28 +0300** | **2026-09-18 03:30:59 +0300** |
| Operator's calendar date | **18 September, as chosen** | **18 September, as chosen** |
| `responseExpectedBy` | — | `2026-10-02`, unchanged |
| In the interface afterwards | the pursuit reads "2 submitted"; both rows visible, no error bar | same |

`evidence/repaired-wire-requests.jsonl`, `evidence/repaired-ui-run.json`.

**These are real saves through the interface**, not replayed or hand-edited
requests. The client was not changed in this wave; the same bytes it sent before
now succeed.

## 3. Offsets, live (§18)

Written straight to the repaired API with a database behind it
(`evidence/repaired-verification.json`):

| Offset | Submission | Pitch |
| --- | --- | --- |
| `+03:00` (east) | 201 | 201 |
| `-07:00` (west) | 201 | 201 |
| `Z` | 201 | 201 |

Deterministic offset, midnight and daylight-saving cases live in the test suite
rather than in a live run: [`REPAIR-003A2-EDGE-CASES.md`](REPAIR-003A2-EDGE-CASES.md).

## 4. Existing data and the read path (§19)

The records written before this wave — the two saved by UTC replay during
003A.1 — read back unchanged:

| Record | Stored | At UTC+3 |
| --- | --- | --- |
| 003A.1 pitch | `2026-09-16T22:45:48.125618+00:00` | 2026-09-17 01:45:48 |
| 003A.1 submission | `2026-09-16T22:46:15.775769+00:00` | 2026-09-17 01:46:15 |

Nothing was reinterpreted and no data was migrated. Every stored instant was
already UTC — the driver made any other value unwritable, which is the one
mercy of this defect: there is no mixed historical semantics to untangle.

Calendar dates read back as themselves: `2026-10-02`, `2026-10-01`.

## 5. Queries, filters and ordering (§20)

No query, projection or filter was touched. Checked after the repair:

- submissions come back **newest first** (`orderingIsNewestFirst: true`);
- the pursuit's derived counts still read "1 of 1 targets open, 2 submitted, 0 awaiting a reply";
- "awaiting a reply" is derived from `response_expected_by`, a `date`, which this wave does not touch.

## 6. Idempotency (§21)

One key, the same instant written two ways:

| Step | Result |
| --- | --- |
| `POST …/submissions` with `…+03:00` | `201`, submission `01a0b1ee-5bef-7342-85e6-b9415c71ba0a` |
| Retry, same key, same instant written as `…Z` | `201`, **the same identifier** |
| Records created | **one** |

The fingerprint is taken from the bound request, so normalizing on the way in is
what makes a retry that reformats its timestamp a retry instead of a conflict.
The idempotency contract itself was not changed.

## 7. Concurrency (§22)

`expectedVersion: 99` against a live target → **409**, with the server's own
explanation. Version checks are untouched; no ordering changed.

## 8. Security and tenant boundaries (§23)

Both endpoints, with a non-UTC body:

| Case | Submission | Pitch |
| --- | --- | --- |
| Unauthenticated | 401 | 401 |
| Read-only observer | 403 | 403 |
| Authenticated non-member | 403 | 403 |
| Cross-tenant route | 404 | 404 |

Nothing about authorization changed because the request shape did not change.
