# Repair Wave 003A.2 — temporal map

Every temporal value the two affected workflows carry, from the Windows control
to the PostgreSQL column. Written before any code changed.

Baseline executable `a728324`; local zone **Israel Standard Time, UTC+03:00, DST
active**.

---

## 1. `RecordPitchDialog` → `POST /opportunity-targets/{id}/pitches`

| | `OccurredAt` | `FollowUp.DueAt` |
| --- | --- | --- |
| **Business meaning** | when the meeting, call or email happened | when the follow-up task is due |
| **Windows control** | `DatePicker OccurredPicker`, header "When" — **date only, no time control** | none; computed |
| **Client expression** | `OccurredPicker.Date` | `DateTimeOffset.UtcNow.AddDays(7)` |
| **Client .NET type** | `DateTimeOffset` — the chosen date, carrying the time of day the dialog was constructed with, at the **local offset** | `DateTimeOffset`, already UTC |
| **JSON form** | `"2026-09-18T03:13:39.1646065+03:00"` (captured) | `"…+00:00"` |
| **API DTO** | `RecordPitchRequest.OccurredAt`, `DateTimeOffset?` | `OpportunityFollowUpRequest.DueAt`, `DateTimeOffset?` |
| **Binding** | System.Text.Json, no converters registered | same |
| **Default when absent** | `clock.UtcNow` (`M6Endpoints.cs:413`) | caller's, else none |
| **Application command** | `RecordPitchCommand.OccurredAt`, `DateTimeOffset` | `AddFollowUpAsync` |
| **Domain** | `OpportunityPitch.OccurredAt`, `Interaction.OccurredAt`, and the target's stage event, all `DateTimeOffset` | `TaskItem.DueAt`, `DateTimeOffset` |
| **EF** | `.HasColumnName("occurred_at")`, no converter | `due_at`, no converter |
| **PostgreSQL** | `opportunity_pitches.occurred_at`, `interactions.occurred_at`, `opportunity_target_events.occurred_at` — all `timestamp with time zone` | `tasks.due_at`, `timestamptz` |
| **Nullable** | request nullable, domain non-null | request nullable, task non-null |
| **Existing normalization** | **none anywhere on the path** | none |
| **Existing validation** | none temporal | none temporal |
| **Other writers** | every endpoint taking a `DateTimeOffset`; `MailboxSynchronizer` from provider timestamps | same |
| **Readers** | `OpportunityQueries`: `OrderByDescending`, `Max`, and `e.OccurredAt > s.SentAt`; client renders instants as `LocalDateTime` where it shows them | task lists |

## 2. `RecordSubmissionDialog` → `POST /opportunity-targets/{id}/submissions`

| | `SentAt` | `ResponseExpectedBy` | `FollowUp.DueAt` |
| --- | --- | --- | --- |
| **Business meaning** | when the agent says it went | the day a reply is expected | when the chase task is due |
| **Windows control** | `DatePicker SentPicker`, "Sent" | `DatePicker ResponsePicker`, "Reply expected by" | same control |
| **Client expression** | `SentPicker.Date` | `DateOnly.FromDateTime(ResponsePicker.Date.DateTime)` | `ResponsePicker.Date` |
| **Client .NET type** | `DateTimeOffset`, local offset | **`DateOnly`** | `DateTimeOffset`, local offset |
| **JSON form** | `"2026-09-18T03:14:06.579828+03:00"` (captured) | `"2026-10-02"` (captured) | `"2026-10-02T03:14:06.5801846+03:00"` (captured) |
| **API DTO** | `DateTimeOffset?` | `DateOnly?` | `DateTimeOffset?` |
| **Domain** | `Submission.SentAt`, plus the target's stage event | `Submission.ResponseExpectedBy`, `DateOnly` | `TaskItem.DueAt` |
| **PostgreSQL** | `submissions.sent_at`, `timestamptz` | `submissions.response_expected_by`, **`date`** | `tasks.due_at`, `timestamptz` |
| **Existing normalization** | none | n/a — already date-only end to end | none |

## 3. Semantic classification (§3)

| Value | Class | Evidence |
| --- | --- | --- |
| `OccurredAt` (pitch, interaction, target event) | **`INSTANT`** | Domain: *"When it happened. Mirrors the interaction, which remains authoritative."* An interaction is a meeting, call or email — a moment. The server's own default is `clock.UtcNow`, an instant. Stored `timestamptz`; ordered, compared against other instants (`e.OccurredAt > s.SentAt`) and aggregated with `Max`. Where the client shows instants it renders `LocalDateTime`. |
| `SentAt` (submission, target event) | **`INSTANT`** | Contract: *"When the agent says it went. Defaults to now."* Same storage, ordering and comparison as above. |
| `FollowUp.DueAt` | **`INSTANT`** | Contract: *"When it is due."* `tasks.due_at` is `timestamptz`; the pitch dialog already computes it as `UtcNow.AddDays(7)`. |
| `ResponseExpectedBy` | **`CALENDAR_DATE`** | `DateOnly` in the contract and the domain, `date` in PostgreSQL, and the contract says *"so silence becomes visible"* — a business day, not a moment. **Correct today; untouched by this wave.** |

**The schema already distinguishes the two.** The same request carries a
`timestamptz` instant and a `date` calendar date, each typed accordingly through
contract, domain and column. That is not historical debt; it is a deliberate
distinction, and it is why the instant fields are treated as instants here.

Nothing is `UNKNOWN`, so §3's stop condition does not apply, and §5's date-only
branch applies to no affected field.

### 3.1 One thing the classification exposes but does not repair

The instant is real, but **the dialogs cannot express its time of day**: both
offer a `DatePicker` only, so the time component is whatever the dialog was
constructed with. An operator recording yesterday's meeting gets yesterday's
date at today's construction time. That is a product design gap in how an
instant is captured, not the defect under repair, and changing it would change
what the two workflows mean (§16). Recorded as an observation.

## 4. The failure boundary (§7)

| | |
| --- | --- |
| Classification | **`EF/NPGSQL_TYPE_RULE`** |
| Thrown by | `Npgsql.Internal.Converters.DateTimeOffsetConverter.WriteCore` |
| Exception | `System.ArgumentException: Cannot write DateTimeOffset with Offset=03:00:00 to PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported. (Parameter 'value')` |
| Wrapped as | `DbUpdateException` from `SaveChangesAsync`, unhandled → `500` with a trace id |
| When | writing the parameter, **before the statement reaches PostgreSQL** |

PostgreSQL never sees the value and never refuses it; the driver refuses to
serialize it. The value passes through JSON binding, the application command and
the domain **unchanged and unchecked**.

## 5. Root scope (§8)

Proved at the API, not inferred:

| Endpoint | `+03:00` | `-05:00` | `Z` |
| --- | --- | --- | --- |
| `POST /opportunity-targets/{id}/pitches` | **500** | — | 201 |
| `POST /opportunity-targets/{id}/submissions` | **500** | — | 201 |
| `POST /interactions` (not in scope; probe only) | **500** | **500** | 201 |

**`SHARED_SERVER_MAPPING_ROOT`.** Any endpoint that accepts a client-supplied
`DateTimeOffset` and persists it fails for every non-zero offset. The two
workflows in scope are instances, not the root; `AOS-R002-001` already named
three other dialogs.

**Latent, not reproduced:** `MailboxSynchronizer` persists
`ProviderMessage.SentAt` / `ReceivedAt` straight from the provider. Microsoft
Graph normally returns UTC, Graph is not configured here, and nothing was
observed — recorded as an observation, not repaired (§8 forbids expanding
without proof).
