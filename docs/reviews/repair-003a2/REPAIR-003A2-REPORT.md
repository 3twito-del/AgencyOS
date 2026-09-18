# Repair Wave 003A.2 — a timestamp from anywhere but UTC

**Wave:** AgencyOS Review Repair Wave 003A.2
**Scope:** `AOS-R002-001`. Nothing else.
**Explicitly out of scope:** general date/time cleanup, `AOS-R002-020`,
`AOS-R002-024`, 003B's six deferred identifier fields, the 218 fire-and-forget
sites, 003C–003F.
**Branch:** `repair-wave-001`
**Date:** 2026-09-18

The Windows date picker hands the client a timestamp in the operator's own
offset. Nothing on the way to the database looked at it, and Npgsql would not
write it. Recording a pitch or a submission was impossible from any machine that
is not on UTC.

Detail: [`REPAIR-003A2-TEMPORAL-MAP.md`](REPAIR-003A2-TEMPORAL-MAP.md) ·
[`REPAIR-003A2-ROOT-CAUSE.md`](REPAIR-003A2-ROOT-CAUSE.md) ·
[`REPAIR-003A2-RUNTIME.md`](REPAIR-003A2-RUNTIME.md) ·
[`REPAIR-003A2-EDGE-CASES.md`](REPAIR-003A2-EDGE-CASES.md) ·
[`REPAIR-003A2-FINDINGS.json`](REPAIR-003A2-FINDINGS.json) ·
[`evidence/`](evidence/)

---

## 1. Starting executable commit

`a728324` — "The pitch and submission material pickers ask for a person's
materials".

## 2. Starting repository tip

`824b55d`, documentation only. Authoritative CI `35242439491`, Nightly
`35242443163`, both green.

## 3. Finding identifier

`AOS-R002-001` — S1, `ConfirmedDefect`, `Always`. Filed by Audit 002 against
`CreatePredictionDialog`, `RecordSignalDialog`, `RecordSourceDialog`,
`RecordInteractionDialog` and `RecordPitchDialog`; 003A.1 added
`RecordSubmissionDialog`.

## 4. Affected workflows

`RecordPitchDialog` and `RecordSubmissionDialog`, as named. The root reaches
every endpoint that accepts a client-supplied `DateTimeOffset` (§11).

## 5. Pre-fix reproduction

Both saves driven through the real dialogs, on `a728324`:

| Workflow | Wire value | Result | Classification |
| --- | --- | --- | --- |
| Record pitch | `"occurredAt":"2026-09-18T03:13:39.1646065+03:00"` | **500** | **`REPRODUCED`** |
| Record submission | `"sentAt":"2026-09-18T03:14:06.579828+03:00"` | **500** | **`REPRODUCED`** |

Nothing persisted. The dialogs opened and their 003B pickers worked; only the
save failed.

## 6. Local zone used

**Israel Standard Time, UTC+03:00, daylight saving active.** The same machine
where 003A.1 met the defect. Live checks also cover **−07:00** and **Z**.

## 7. Temporal semantics

| Value | Class |
| --- | --- |
| `OccurredAt`, `SentAt`, `FollowUp.DueAt` | **`INSTANT`** |
| `ResponseExpectedBy` | **`CALENDAR_DATE`** — already correct, untouched |

Decided from the domain's own words, the contract, the server's default, the
read path and the schema — which already models both kinds in the same request
(`timestamptz` beside `date`). Nothing was `UNKNOWN`; §5's date-only branch
applies to no affected field.

## 8. The UI → database map

[`REPAIR-003A2-TEMPORAL-MAP.md`](REPAIR-003A2-TEMPORAL-MAP.md), field by field:
control, client type, JSON, DTO, binding, command, domain, EF property, column,
nullability, existing normalization (**none anywhere**), validation, other
writers and readers.

## 9. Actual failure boundary

**`EF/NPGSQL_TYPE_RULE`** — `Npgsql.Internal.Converters.DateTimeOffsetConverter.WriteCore`,
inside `SaveChangesAsync`. **PostgreSQL never received the statement**; the
driver refused to serialize the parameter.

## 10. Exact refusal

```
System.ArgumentException: Cannot write DateTimeOffset with Offset=03:00:00 to
PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.
(Parameter 'value')
```

wrapped in `DbUpdateException`, unhandled, surfaced as `500` with a trace id.
`evidence/baseline-npgsql-refusal.txt`.

## 11. Shared-root determination

**`SHARED_SERVER_MAPPING_ROOT`**, proved at the API:

| Endpoint | `+03:00` | `-07:00` | `Z` |
| --- | --- | --- | --- |
| `…/pitches` | 500 | 500 | 201 |
| `…/submissions` | 500 | 500 | 201 |
| `/interactions` (probe only) | 500 | 500 | 201 |

Repaired once, at a boundary all of them pass through. No call site, dialog or
command was edited, so this is not "date cleanup".

## 12. Working sibling comparison

The sibling that always worked is the **calendar date in the same request**:
`ResponseExpectedBy` is a `DateOnly` against a `date` column and never had the
problem. That comparison is what proves the instant fields are instants rather
than mistyped dates — and it is the reason the instant convention was not copied
onto it. Server-generated instants (`clock.UtcNow`) also always worked, which
locates the defect at values that come from outside.

## 13. Chosen canonical representation

The instant, in UTC, exactly as AgencyOS already stores every instant. The wire
type is unchanged: `DateTimeOffset` with any offset remains legal, and the whole
of that value space now works.

## 14. Chosen normalization boundary

**The API boundary** — one `JsonConverter<DateTimeOffset>` on the host's
serializer options. Narrowest point that covers every endpoint; leaves the
domain, EF and every query untouched; keeps the server robust against valid
clients in any zone. Alternatives and why not: root cause §4.

It came with one obligation. A custom converter is opaque to the OpenAPI schema
generator, which then published every timestamp as an untyped value; a schema
transformer restores what those properties always said, and the document is
byte-identical to the one built before this wave (§30). Caught by regenerating
and comparing rather than by the gate, which compares counts — filed as
`AOS-R002-027`.

## 15. Why business meaning is preserved

`ToUniversalTime` keeps the moment and changes only how it is written down. The
operator's date survives where it matters — the pitch saved at
`03:30:28+03:00` is stored as `00:30:28Z` and renders back as **18 September
03:30:28 +0300**. None of §11's pseudo-fixes was used: no string surgery, no
appended `Z`, no forced `DateTimeKind`, no date recovered from a converted
instant, no machine or CI timezone changed.

## 16. Midnight and date-shift tests

Instants, so the invariant asserted is the instant — and, separately, that the
writer's own calendar day still reads back. `00:30+03:00` → `21:30Z` the previous
day, still the 18th in Tel Aviv; `23:30-05:00` → `04:30Z` the next day, still the
17th in Chicago. Both directions, east and west.

## 17. Offset tests

UTC, `+00:00`, `+03:00`, `-07:00`, `+14:00`, `-12:00` — one instant, six
spellings. Live: `+03:00`, `-07:00`, `Z`, all `201`.

## 18. DST tests

Applicable, because these are instants. Israel's 2026 transitions: the ambiguous
01:30 on 25 October stays **two instants an hour apart** depending on the offset
written, and the skipped 02:30 on 27 March is read as the instant it denotes. No
DST complexity was invented for the calendar-date field.

## 19. Serialization tests

Round trip, absent values staying absent, malformed values refused, a whole
client-shaped pitch request, and a calendar date left alone beside a normalized
instant. Formatting is not over-asserted.

## 20. Server robustness tests

Canonical UTC `201`; non-UTC `201`; **malformed `400`, not `500`**; absent
optional timestamp `201` using the server clock; stale version `409`.

## 21. Existing data and read path

Records written before the repair read back unchanged, and no data was migrated.
Every stored instant was already UTC — the driver made anything else unwritable,
so there is no mixed historical semantics. Calendar dates read back as
themselves. Ordering is still newest-first and the pursuit's derived counts are
unchanged.

## 22. End-to-end pitch, through the interface

Opened from the Pipeline button on a real target, material chosen with the
keyboard, summary typed, Enter. `201`. **This is a real save from the Windows
client at UTC+3**, not a replayed request.

## 23. End-to-end submission, through the interface

The same, with its material and its calendar date. `201`.

## 24. Persisted and read back

| Sent by the client | Stored | Rendered at UTC+3 |
| --- | --- | --- |
| `2026-09-18T03:30:28.0094673+03:00` | `2026-09-18T00:30:28.009467+00:00` | 2026-09-18 03:30:28 +0300 |
| `2026-09-18T03:30:59.2745122+03:00` | `2026-09-18T00:30:59.274512+00:00` | 2026-09-18 03:30:59 +0300 |

Same instant, same day for the operator. (PostgreSQL keeps microseconds, so the
final tick digit is dropped — pre-existing and unrelated.)

## 25. Idempotency

One key, the same instant written `+03:00` and then `Z`: `201` twice, **the same
submission identifier**, one record. Normalizing on the way in is what makes a
retry that reformats its timestamp a retry rather than a conflict. The contract
is unchanged.

## 26. Concurrency

`expectedVersion: 99` → **409**. Version checks untouched, nothing reordered, and
the existing concurrency suite runs in CI.

## 27. Security and tenant boundaries

Both endpoints with a non-UTC body: unauthenticated **401**, read-only observer
**403**, authenticated non-member **403**, cross-tenant route **404**.

## 28. Structural guard

Two, both semantically precise:

- `TheHostReadsEveryIncomingInstantAsUtc` reads the API host's own serializer
  options out of its service provider and parses a `+03:00` timestamp through
  them. The repair is a single registration; this is what fails if it is ever
  dropped, since every other test exercises the converter directly.
- `Contract_PublishesTimestampsAsDateTimeStrings` and
  `Contract_PublishesCalendarDatesAsDates` pin what the published document says
  about an instant and about a business date, which is the difference this whole
  wave turns on.

No rule was added that merely greps for `DateTime`.

## 29. Schema impact

**NO CHANGE.** No migration; no column type altered; latest migration still
`20260909072201_AiResultClassification`.

## 30. OpenAPI impact

**NO CHANGE.** 263 paths / 187 schemas, and the document is **byte-identical**
to the one generated before this wave (sha256 `942d0e74…` both).

This took two steps rather than one. The custom converter first degraded every
timestamp schema to "any value at all" — type, `date-time` format and
nullability gone — while the path and schema counts stayed exactly the same, so
the gate reported `[OK]`. A schema transformer restored the document, and
`OpenApiContractTests` now pin the published shape of four instants and the
calendar date beside them.

## 31. Contract impact

**NO CHANGE.** API contract **14**. The wire type was always `DateTimeOffset`.

## 32. Unit count

**3,755** — unchanged.

## 33. Windows count

**942** — unchanged. The client was not modified.

## 34. Reviewer count

**163** — unchanged.

## 35. Integration count

**851 / 851** (was 821): +17 serialization, +8 workflow, +5 contract-shape tests.

## 36. `postgres:18.6` evidence

CI run `35292653905`, job "Integration tests (PostgreSQL 18.6)": the service image
`postgres:18.6` pulled and started, **851 passed, 0 failed**. The local cluster
used for the runtime evidence is not authoritative and no integration result
here comes from it.

## 37. TLA+ result

**4 / 4** — `OfflineWriteQueue`, `OutboundSend`, `AiApproval`,
`LocalInferenceLease`, locally and in CI.

## 38. Executable commit

**`ada2310`** — "A timestamp is still published as a date-time string", on top of
**`f4ea972`** — "A timestamp from anywhere but UTC could not be saved". Two
commits: the repair, and the contract restoration it obliged.

## 39. Final tip

The documentation commit that adds this report, directly on `ada2310`; it
changes nothing outside `docs/`.

## 40. Authoritative CI

**CI `35292653905` on `ada2310` — success, both jobs.**

| Gate | Before (`a728324`) | After (`ada2310`) |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | **3,755** |
| Windows | 942 | **942** |
| reviewer | 163 | **163** |
| integration | 821 / 821 | **851 / 851 vs `postgres:18.6`** |
| OpenAPI | 263 / 187 | **263 / 187, byte-identical** |
| API contract | 14 | **14** |
| TLA+ | 4 / 4 | **4 / 4** |

## 41. Nightly

**Not green on the final tip, and not for a reason in this repository.**

| Run | Commit | Result |
| --- | --- | --- |
| `35242443163` | `a728324` | success, both jobs |
| `35292349561` | `f4ea972` — the behavioural repair | **success, both jobs** |
| `35292656188`, `35293184426`, `35293480720` | `ada2310` | **could not start** |

GitHub answered each attempt with: *"The job was not started because recent
account payments have failed or your spending limit needs to be increased."*
The jobs ran for three seconds with no steps. CI on the same commit had already
been granted runners minutes earlier, so this began between the two.

The unverified delta is `f4ea972 → ada2310`: 21 lines in `Program.cs` and 62
lines of tests. It touches no client code, and Nightly's Windows job packages
the client. Its integration job runs the same suite CI ran green on this exact
commit. **This is an operator action — billing — and the wave does not claim
that gate.**

## 42. New findings

| Finding | Severity | Disposition | What |
| --- | --- | --- | --- |
| `AOS-R002-025` | S3 | **DEFERRED** (003C) | A request whose required nested object is absent returns `500`, not `400` (`participants[].party`). Found while building a probe; a different root, and not on the temporal path. |
| `AOS-R002-026` | S3 | **DEFERRED**, owner decision | The two dialogs cannot express the time of day of an instant: they offer a date picker only, so the stored time is whichever moment the dialog was opened. Surfaced by the semantic classification §7 required. Repairing it would change what the fields mean, which §16 forbids here. |
| `AOS-R002-027` | S3, `TestGap` | **REPAIRED here, narrowly** | The contract gate compares the number of paths and schemas, not what they say. This wave's own converter emptied every timestamp schema without changing either count, and the gate printed `[OK]`. Found by regenerating the document and comparing it byte for byte. The document is restored and the published shape of five fields is now asserted; the gate itself still counts, which is recorded rather than rewritten. |

Recorded, not filed as its own identifier: `MailboxSynchronizer` persists
provider timestamps unchanged, so the same shape exists off the HTTP path. Graph
normally returns UTC, Graph is not configured here, nothing was observed — §8
forbids expanding on that.

`AOS-R002-024` was left alone: the refusal the operator saw pre-fix read
"Invalid request" rather than the server's reason. That is refusal copy, and it
belongs to 003C.

## 43. Confirmation: no 003C–003F work occurred

None. No file belonging to `AOS-R002-020`, `AOS-R002-021`, `AOS-R002-024`,
003B's six deferred identifier fields, or any 003C–003F item was edited. No
dispatch site was touched; the 218 fire-and-forget sites remain an observation.
No dialog, no client code, no domain type, no EF mapping, no migration and no
contract changed. Audit 002 and the earlier waves' reports were not rewritten;
this wave adds notes.

---

## Closing

```
REPAIR WAVE 003A.2 = COMPLETE EXCEPT ONE GATE
```

Every closure-gate item is met except **"Nightly is green"** on the final tip,
which GitHub refuses to run for an account-billing reason (§41). Nothing in the
repository is outstanding.

| | |
| --- | --- |
| Finding | `AOS-R002-001` — S1 — **REPAIRED** |
| Root cause | a client-supplied instant in any non-zero offset reached Npgsql unchanged, and Npgsql writes only offset zero to `timestamptz` |
| Failure boundary | `EF/NPGSQL_TYPE_RULE`, before PostgreSQL saw the statement |
| Semantics | `OccurredAt`, `SentAt`, `DueAt` = **`INSTANT`**; `ResponseExpectedBy` = **`CALENDAR_DATE`**, untouched |
| Boundary chosen | the API boundary — one converter on the host's serializer options |
| Root scope | `SHARED_SERVER_MAPPING_ROOT` — repaired once, for every endpoint |
| Pre-fix, UTC+3, through the UI | pitch **500**, submission **500** |
| Post-fix, UTC+3, through the UI | pitch **201**, submission **201**, instants preserved |
| Midnight | instant preserved; the operator's own calendar date still reads back, east and west |
| Schema / OpenAPI / contract | no change / byte-identical / 14 |
| Authoritative CI | `35292653905` on `ada2310`, success, both jobs |
| Nightly | **blocked on the final tip** — account billing; green on `f4ea972`, which carries the behavioural repair |
| Audit 002 | remains **COMPLETE** |
| 003C–003F | not begun |

### Left for the owner

- **`AOS-R002-026`** needs a decision: whether a pitch or a submission should let
  the operator say what time it happened, be modelled as a calendar date, or
  keep a time nobody chose.
- **`AOS-R002-025`** and the refusal copy (`AOS-R002-024`) both belong to
  refusal/validation work.
- **`AOS-R002-027`**: the contract gate counts paths and schemas. Making it
  compare against a published baseline would be a change to the canonical gates.
- **GitHub Actions billing.** Until the spending limit or payment is settled, no
  workflow job can start. Re-dispatching Nightly on `ada2310` afterwards is the
  only step left in this wave.
