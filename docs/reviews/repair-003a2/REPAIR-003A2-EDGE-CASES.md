# Repair Wave 003A.2 — edge cases

What was tested around offsets, midnight and daylight saving, and why each
assertion is the right one for the field's semantic class.

All fixed values carry explicit offsets, so every test means the same thing on
any runner. **No test reads `TimeZoneInfo.Local`**, and none was made to pass by
putting a machine or a CI runner on UTC.

---

## 1. Offsets (§12, §18)

`NonUtcTimestampSerializationTests.EveryOffsetMeansTheSameInstant` — one moment,
written six ways, all read as `2026-09-18T00:13:39.1646065Z`:

| Written | Meaning |
| --- | --- |
| `2026-09-18T00:13:39.1646065Z` | UTC |
| `2026-09-18T00:13:39.1646065+00:00` | UTC, spelled out |
| `2026-09-18T03:13:39.1646065+03:00` | Tel Aviv — the machine the defect was found on |
| `2026-09-17T17:13:39.1646065-07:00` | Los Angeles |
| `2026-09-18T14:13:39.1646065+14:00` | the eastern extreme of the offset range |
| `2026-09-17T12:13:39.1646065-12:00` | the western extreme |

Each also asserts the offset is now zero, so a regression that stopped
normalizing would fail here rather than at a database.

Live, through the API, with a real database: submission and pitch written at
**+03:00**, **−07:00** and **Z** — `201` each
(`evidence/repaired-verification.json`). Before the repair the first two were
`500`.

## 2. Midnight and date shift (§13)

The fields are instants, so the assertion is that **the instant survives**, and
that the operator's own calendar date still reads back as the one they chose.
An assertion that the UTC date is unchanged would be wrong here — it is the
assertion a calendar-date field would need, and using it on an instant is how a
"fix" ends up shifting days.

| Written | Read as | UTC day | Day in the writer's zone |
| --- | --- | --- | --- |
| `2026-09-18T00:30:00+03:00` | `2026-09-17T21:30:00Z` | 17th | **18th** |
| `2026-09-17T23:30:00-05:00` | `2026-09-18T04:30:00Z` | 18th | **17th** |

Both directions on purpose: east of Greenwich the UTC date runs behind the
operator's, west of it the UTC date runs ahead.

And end to end: the pitch saved from the Windows client at
`2026-09-18T03:30:28.0094673+03:00` is stored as `2026-09-18T00:30:28.009467Z`
and renders at UTC+3 as **2026-09-18 03:30:28 +0300** — the same instant, the
same day for the operator who recorded it.

## 3. Daylight saving (§12)

Applicable because these are instants. Israel's 2026 transitions were used
because that is the zone the defect was reproduced in.

| Case | Written | Read as |
| --- | --- | --- |
| **Ambiguous** — 01:30 happens twice on 2026-10-25 | `01:30+03:00` (daylight) | `2026-10-24T22:30:00Z` |
| | `01:30+02:00` (standard) | `2026-10-24T23:30:00Z` |
| **Skipped** — 02:30 never happens on 2026-03-27 | `02:30+02:00` | `2026-03-27T00:30:00Z` |

The two ambiguous readings stay **one hour apart**: the offset in the text says
which of the two moments was meant, and the converter never consults a time zone
to guess. The skipped local hour is a legible instant when written with an
offset, and is read as one rather than rejected or moved.

**No DST complexity was invented for the calendar-date field.** For
`ResponseExpectedBy` the only question is whether the date survives untouched,
and a test asserts exactly that while an instant in the same request is
normalized around it.

## 4. Serialization contract (§14)

| Case | Expectation |
| --- | --- |
| Round trip | the instant that goes out comes back |
| Absent `sentAt` / `responseExpectedBy` | stay `null`; the server fills the instant with its own clock |
| `"not a timestamp"`, `"2026-13-01T00:00:00Z"`, a bare number | `JsonException` — refused, not guessed at |
| A whole pitch request as the client writes it | `occurredAt` and `followUp.dueAt` both read as UTC instants |
| `responseExpectedBy` next to a non-UTC `sentAt` | date unchanged, instant normalized |

Formatting is not over-asserted: the tests check instants and offsets, not the
serializer's exact text.

## 5. Server robustness (§15)

Live against the repaired API:

| Case | Result |
| --- | --- |
| Canonical UTC | `201` |
| Non-UTC, east and west | `201` |
| Malformed timestamp | **`400`**, not `500` |
| Absent optional timestamp | `201`, server clock used |
| Stale `expectedVersion` | `409` |

## 6. Where the host is configured

`NonUtcTimestampWorkflowTests.TheHostReadsEveryIncomingInstantAsUtc` reads the
API host's own `JsonSerializerOptions` out of its service provider and parses a
`+03:00` timestamp through them. Every other test here exercises the converter;
this one proves the host actually uses it, which is the single line the whole
repair rests on.
