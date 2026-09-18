# Repair Wave 003A.2 — root cause of `AOS-R002-001`

**A timestamp written in the caller's own offset never reached the database.** It
travelled through binding, the command and the domain untouched, and Npgsql
refused to write it. The operator saw an unexplained `500`. Every offset but zero
failed, so the pitch and submission workflows were impossible from anywhere that
is not on UTC.

Full field-by-field path: [`REPAIR-003A2-TEMPORAL-MAP.md`](REPAIR-003A2-TEMPORAL-MAP.md).

---

## 1. What these values mean (§3)

| Field | Class |
| --- | --- |
| `RecordPitchRequest.OccurredAt` | **`INSTANT`** |
| `RecordSubmissionRequest.SentAt` | **`INSTANT`** |
| `OpportunityFollowUpRequest.DueAt` | **`INSTANT`** |
| `RecordSubmissionRequest.ResponseExpectedBy` | **`CALENDAR_DATE`** — already correct, untouched |

The decisive evidence is that AgencyOS already models both, in the same request:
`sentAt` is a `DateTimeOffset` against `timestamptz`, and `responseExpectedBy` is
a `DateOnly` against `date`. The domain calls one *"when it happened"* and
*"mirrors the interaction, which remains authoritative"*, and the other *"so
silence becomes visible"*. The server's own default for the first is
`clock.UtcNow`. They are ordered against each other in SQL and rendered with
`LocalDateTime` in the client. Nothing here was inferred from the column type
alone, and nothing is `UNKNOWN`.

**So §5 does not apply to any affected field.** No midnight-local was converted
to UTC to satisfy a driver, because no affected field is a calendar date.

## 2. Where it actually failed (§7)

**`EF/NPGSQL_TYPE_RULE`.** Not PostgreSQL, and not validation.

```
System.ArgumentException: Cannot write DateTimeOffset with Offset=03:00:00 to
PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.
(Parameter 'value')
   at Npgsql.Internal.Converters.DateTimeOffsetConverter.WriteCore(PgWriter writer, DateTimeOffset value)
   at Npgsql.NpgsqlParameter.Write(…)
   …
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(…)
```

wrapped in `DbUpdateException` from `SaveChangesAsync`, unhandled, `500` with a
trace id. The statement never reached the server; the driver refused to
serialize the parameter. Nothing on the path — binding, application command,
domain — looked at the offset at all.

## 3. How far it reached (§8)

**`SHARED_SERVER_MAPPING_ROOT`**, proved at the API rather than reasoned about:

| Endpoint | `+03:00` | `-05:00` / `-07:00` | `Z` |
| --- | --- | --- | --- |
| `POST …/pitches` | **500** | **500** | 201 |
| `POST …/submissions` | **500** | **500** | 201 |
| `POST /interactions` — probe only, not in scope | **500** | **500** | 201 |

Any endpoint accepting a client-supplied `DateTimeOffset` was affected; the two
workflows are instances. `AOS-R002-001` had already named three other dialogs.
The repair is made once, at a boundary every endpoint passes through, so their
call sites were not touched and no dialog was edited.

### Residual, recorded not repaired

`MailboxSynchronizer` persists `ProviderMessage.SentAt` / `ReceivedAt` exactly as
the provider supplies them. Microsoft Graph normally returns UTC, Graph is not
configured in this environment, and nothing was observed — so §8's rule applies
and it is left alone. If a provider ever returns an offset, that path has the
same shape.

## 4. The repair (§4, §10, §11)

One converter, registered once, on the way in:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new UtcInstantConverter()));
```

`Read` returns `reader.GetDateTimeOffset().ToUniversalTime()`.

It carries one consequence that had to be repaired with it. The OpenAPI schema
generator cannot see through a custom converter, so it published every
`DateTimeOffset` property as "any value at all" — no type, no `date-time`
format, no nullability. The document is the contract and clients are generated
from it, so a schema transformer restores exactly what those properties said
before, and the generated document is byte-identical to the one built before
this wave. The contract gate counts paths and schemas and both counts were
unchanged, so it would not have caught this; `OpenApiContractTests` now assert
the published shape instead.

**Why this boundary.** The contract types these fields as `DateTimeOffset`, so
every offset is a legal way to write an instant, and a client in Los Angeles is
as valid as one in Tel Aviv. Internally AgencyOS keeps instants in UTC — the
server clock is `UtcNow`, storage is `timestamptz`. The place where an outside
representation becomes an internal value is where the request is read, and that
is the narrowest point that covers every endpoint at once.

| Boundary | Why not |
| --- | --- |
| Client UI / client wrapper | Leaves the server fragile to every other client, and repairs nothing for anyone who is not this Windows build. |
| Each endpoint or command | The same conversion in dozens of places, each able to be forgotten. |
| Domain value object | The domain is offset-agnostic; an instant is an instant. Nothing there is wrong. |
| Persistence mapping | Where the driver's rule lives, and it would also cover the provider path — but a model-wide value converter on every `DateTimeOffset` changes how the whole model is queried and materialized, for a defect whose reproduced instances all arrive as JSON. Too broad for what is proven. |

**Conversion is lossless.** `ToUniversalTime` keeps the moment and changes only
how it is written down. None of §11's pseudo-fixes was used: no offset was
stripped from a string, no `Z` appended, no `DateTimeKind` forced, no date
recovered from a converted instant, no culture-dependent parse, no machine
timezone changed, and no test pinned to UTC — the tests carry explicit offsets
precisely so they do not depend on where they run.

**Calendar dates are untouched.** `ResponseExpectedBy` is a `DateOnly` and the
converter never sees it; a test pins that.

## 5. What the repair does not change (§16, §26)

Schema, OpenAPI and the API contract are unchanged: the wire type was always
`DateTimeOffset` and still is, and the server now accepts the whole of its value
space instead of a third of it. No pitch or submission state machine, target
relationship, material, owner, picker, authorization rule, idempotency contract,
concurrency check or audit event was altered.

The responses the API sends are unchanged in practice: stored instants were
already UTC, and the converter writes them the same way.

One small honesty note: PostgreSQL stores microseconds, so a .NET tick-precision
instant loses its last digit on the way in — `…0094673` reads back as `…009467`.
That is pre-existing and unrelated to this repair.
