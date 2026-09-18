# Repair Wave 003A.2 — request-side `DateTimeOffset` inventory

Repair Wave 003A.2 registered **one converter for every `DateTimeOffset` the API
reads**. This is the check that its reach is no wider than the semantics that
were proven: every request property it touches must be an instant, because
converting anything else through a time zone is the mistake the wave existed to
avoid.

Taken on the closure pass at executable tip `ada2310`. No code was changed to
produce it.

---

## How the list was built

From the **published contract** rather than from a search, so the list is what
the converter actually meets: every `requestBody` schema in
`artifacts/openapi/AgencyOS.Api.json`, resolved through `$ref`s and arrays, and
every property of `format: date-time` in them.

Cross-checked against the source: 20 request records in `AgencyOS.Contracts`
declare `DateTimeOffset` properties, 24 properties in total. **The two lists
match exactly** — nothing in the source is missing from the document, and
nothing in the document is absent from the source.

Two things the converter does **not** reach: query-string parameters, which are
bound by `TryParse` and not by the JSON serializer, and `DateOnly` properties,
which have their own converter and their own `date` columns.

## The naming convention, tested rather than assumed

Across the whole request surface:

| | Property names | Any counter-example |
| --- | --- | --- |
| `format: date` (calendar) | 41 distinct names — `openedOn`, `signedOn`, `startsOn`, `dueOn`, `responseExpectedBy`, `effectiveFrom`, … | **none ends in `At`** |
| `format: date-time` (instant) | 11 distinct names — `occurredAt`, `sentAt`, `dueAt`, `observedAt`, `communicatedAt`, `expiresAt`, `startedAt`, `endedAt`, `firstObservedAt`, `publishedAt`, `resolvesBy` | **none ends in `On`** |

The one name that does not follow the suffix rule is `resolvesBy`, which shares
its suffix with the calendar-date `responseExpectedBy`. It is examined on its
own below rather than waved through.

## The 24 properties

All columns below are `timestamp with time zone`; none is `date`.

| # | Request type | Property | Endpoint(s) | Business meaning | Class | Evidence | UTC canonicalization valid |
| --: | --- | --- | --- | --- | --- | --- | :-: |
| 1 | `RecordPitchRequest` | `occurredAt` | `POST …/opportunity-targets/{id}/pitches` | when the meeting, call or email happened | `INSTANT` | contract: *"When it happened. Defaults to now."*; domain: *"Mirrors the interaction, which remains authoritative"*; `opportunity_pitches.occurred_at` | **yes** |
| 2 | `RecordSubmissionRequest` | `sentAt` | `POST …/opportunity-targets/{id}/submissions` | when the agent says it went | `INSTANT` | contract: *"When the agent says it went. Defaults to now."*; `submissions.sent_at` | **yes** |
| 3 | `OpportunityFollowUpRequest` | `dueAt` | pitches, submissions | when the follow-up is due | `INSTANT` | contract: *"When it is due."*; `tasks.due_at` | **yes** |
| 4 | `RecordInteractionRequest` | `occurredAt` | `POST /interactions` | when the interaction happened | `INSTANT` | contract: *"When it happened, which is not when it was recorded."*; `interactions.occurred_at` | **yes** |
| 5 | `FollowUpTaskRequest` | `dueAt` | `POST /interactions` | when the task is due | `INSTANT` | contract: *"When it is due."*; `tasks.due_at` | **yes** |
| 6 | `CreateTaskRequest` | `dueAt` | `POST /tasks` | when the task is due | `INSTANT` | same type and column | **yes** |
| 7 | `DealFollowUpRequest` | `dueAt` | offers, answers, terms | when the task is due | `INSTANT` | same | **yes** |
| 8 | `LegalFollowUpRequest` | `dueAt` | option resolve, notice, signature | when the task is due | `INSTANT` | same | **yes** |
| 9 | `MoveOpportunityTargetRequest` | `occurredAt` | `POST …/opportunity-targets/{id}/stage` | when the stage change happened | `INSTANT` | contract: *"When it happened, which is not always now."*; `opportunity_target_events.occurred_at` | **yes** |
| 10 | `RecordTargetResponseRequest` | `occurredAt` | `POST …/opportunity-targets/{id}/responses` | when they responded | `INSTANT` | contract: *"When it happened."*; same event table | **yes** |
| 11 | `RecordOfferRequest` | `communicatedAt` | `POST /deals/{id}/offers` | when the offer was made or received | `INSTANT` | contract: *"When it was made or received. Defaults to now."*; domain: *"When it was actually made or received… freely backdated"*, distinct from `RecordedAt`; `offers.communicated_at` | **yes** |
| 12 | `RecordOfferRequest` | `expiresAt` | same | when the offer was stated to lapse | `INSTANT` | contract: *"When it was stated to lapse, if a lapse was stated."*; domain refuses to expire by clock; `offers.expires_at` | **yes** |
| 13 | `OpenOfferRequest` | `communicatedAt` | `POST /offers/{id}/record` | as 11 | `INSTANT` | same property | **yes** |
| 14 | `DraftOfferRequest` | `expiresAt` | `POST /deals/{id}/draft-offers` | as 12 | `INSTANT` | same property | **yes** |
| 15 | `UpdateDraftOfferRequest` | `communicatedAt` | `PUT /offers/{id}` | as 11 | `INSTANT` | same property | **yes** |
| 16 | `UpdateDraftOfferRequest` | `expiresAt` | `PUT /offers/{id}` | as 12 | `INSTANT` | same property | **yes** |
| 17 | `CreateRelationshipRequest` | `startedAt` | `POST /relationships` | when the working relationship began | `INSTANT` | contract: *"When it began."*; `professional_relationships.started_at`; the aggregate has no date-only counterpart | **yes** |
| 18 | `EndRelationshipRequest` | `endedAt` | `POST /relationships/{id}/end` | when it ended | `INSTANT` | contract: *"When it ended; defaults to now."*; `professional_relationships.ended_at` | **yes** |
| 19 | `RecordSignalRequest` | `occurredAt` | `POST /intelligence/signals` | when the reported thing happened | `INSTANT` | domain: *"When the thing happened, where that is known… A hiring reported on Friday may have happened weeks earlier"*; `signals.occurred_at` | **yes** |
| 20 | `RecordSignalRequest` | `observedAt` | same | when somebody here was told | `INSTANT` | domain: *"When somebody here observed or was told the claim."*; `signals.observed_at` | **yes** |
| 21 | `RecordSourceRequest` | `publishedAt` | `POST /intelligence/sources` | when the source says it was published | `INSTANT` | domain: *"When the source says it was published. Often unknown."*; `intelligence_sources.published_at` | **yes** |
| 22 | `RecordSourceRequest` | `observedAt` | same | when somebody here saw it | `INSTANT` | domain: *"When somebody here saw it."*; `intelligence_sources.observed_at` | **yes** |
| 23 | `CreateRadarEntryRequest` | `firstObservedAt` | `POST /intelligence/radar` | when they first came onto the radar | `INSTANT` | domain: *"When they first came onto the radar."*; `talent_radar_entries.first_observed_at` | **yes** |
| 24 | `CreatePredictionRequest` | `resolvesBy` | `POST /intelligence/predictions` | the deadline the prediction is judged against | `INSTANT` | **domain: *"The moment by which the event must happen for the answer to be Yes."*** and it is compared as `now > ResolvesBy`; `predictions.resolves_by` | **yes** |

### The one that needed looking at

`resolvesBy` is the only instant whose name follows the same pattern as a
calendar date elsewhere (`responseExpectedBy`), and its own list filters —
`resolvesAfter`, `resolvesBefore` — are `DateOnly`, which is what a deadline
*date* would look like. The domain settles it in its own words: **"The moment by
which the event must happen"**, evaluated as `now > ResolvesBy` to decide whether
a prediction is awaiting resolution. It is a deadline instant that people happen
to filter by day. Normalizing it to UTC preserves that moment exactly.

## Verdict

```
GLOBAL CONVERTER SCOPE = SEMANTICALLY VALID
```

All 24 request-side `DateTimeOffset` properties the converter can reach are
`INSTANT`. None is `CALENDAR_DATE`, `LOCAL_WALL_TIME`, `ZONED_DATE_TIME` or
`UNKNOWN`. For every one of them `ToUniversalTime` preserves the value's meaning
and changes only how it is written down, which is what the converter does.

`ResponseExpectedBy` — the calendar date that travels in the same request as
`sentAt` — remains `DateOnly` in the contract, `DateOnly` in the domain and
`date` in PostgreSQL. **The converter never sees it**, and the published document
still describes it as `format: date`.

### What the converter also touches, and why it is harmless

The same converter serializes responses. Every stored instant is UTC — before
the repair the driver made any other value unwritable — so responses carry the
same values they did before, written the same way. No historical data changed
meaning, because no non-UTC instant was ever stored.
