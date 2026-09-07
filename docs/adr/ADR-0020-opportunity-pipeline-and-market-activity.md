# ADR-0020: An opportunity is a pursuit, a target is a market conversation, and AgencyOS records rather than sends

Status: Accepted
Date: 2026-09-07

## Context

M6 is where an agency's actual work becomes data: taking something to market and
tracking what happens. The risk is not schema, and it is not volume. It is that
the pipeline is the part of an agency system most likely to be built as a CRM
clone, and a CRM clone gets four things wrong at once:

- it fuses the pursuit with each market conversation, so one buyer passing looks
  like the whole thing failing;
- it stores derived facts - "has a submission", "awaiting reply", "probability" -
  next to the events they come from, and the two drift;
- it treats a recorded submission as a sent one, so the system claims a delivery
  it never performed;
- it lets deal language leak in early, so `Won`, `Offer` and `Closed` appear
  before there is a deal model to give them meaning.

M5 deliberately left this space empty. Packages hold what the agency wants and
attachments hold what is true, and neither has a submitted or pitched state, so
M6 builds a pipeline rather than untangles one.

## Decision

### Opportunity status and target stage are different axes, and both are needed

`OpportunityStatus` is `Draft → Active ↔ Paused → Closed → Active | Cancelled`.
It answers one question: is the agency working this at all.

`OpportunityTargetStage` is `Identified → Approved → Contacted → Engaged →
Interested → Advanced`, leaving to `Passed`, `Withdrawn` or `Exhausted`. It
answers a different question: where does this one conversation stand.

Collapsing them is the defining CRM mistake. Twelve buyers on one project means
twelve simultaneous, unrelated positions; a single status field forces the
pursuit to pretend it is at whichever stage the loudest target is at. A project
where eleven buyers passed and one is reading is not "passed", and it is not
"interested" either - it is one live conversation inside a pursuit that is still
open, which is exactly what an agent needs to see.

Both machines are total and enumerated: 25 status pairs and 81 stage pairs are
asserted individually, legal and illegal alike, so an added value cannot silently
acquire permissive transitions.

### There is deliberately no `Submitted` stage

A submission is an event that happened at an instant. A stage is where a
conversation currently stands. `Submitted` as a stage would mean a target sits
there forever after one email, and a second submission six weeks later would have
nowhere to go.

Recording a submission moves a target `Contacted → Engaged` and leaves a
`Submission` row that carries the date, the channel and the material snapshot.
The stage says where things are; the submission rows say what happened.

### Opportunity outcomes stop short of the deal boundary

`OpportunityOutcome` is `Placed`, `NoInterest`, `Withdrawn`, `Superseded`,
`NotPursued`. There is no `Won`, no `Lost`, no `DealClosed`.

`Placed` means the pursuit achieved what it was for. Whether money changed hands,
on what terms, and whether the deal later collapsed are M7 facts, and a `Won`
here would be a commercial claim made by a model that has no commercial
vocabulary. The same reasoning removes offer-shaped pitch outcomes:
`PitchOutcome` is `NoDecision`, `FollowUpRequested`, `MoreMaterialRequested`,
`Interested`, `Passed`. A buyer saying they are interested is a market signal.
An offer is a document with terms, and M6 has no way to represent one honestly.

### AgencyOS records that a submission happened. It does not send anything

This is the single most consequential decision in M6, and it is enforced in three
places rather than asserted once.

- The domain: `Submission.SentAt` is *when the agent says it went*, defaulting to
  now but freely backdated, because most submissions are recorded after the fact.
- The API: nothing in M6 has an outbound transport, holds a credential, or has a
  delivery status. `ExternalReference` is an opaque string the agent supplies and
  AgencyOS assigns no meaning to - it is the seam an M10 mail integration would
  fill, and until then it is a note.
- The Windows surface: the submissions tab carries a permanent, non-dismissible
  statement that AgencyOS records rather than sends and cannot confirm delivery.

A system that implied it had verified delivery would be worse than one that said
nothing, because somebody would rely on it in front of a client.

### Silence is derived, never stored

Nothing anywhere records a non-response. A submission carries
`ResponseExpectedBy`; a target is awaiting a reply when a submission's expected
date has passed with no response event since. Both are computed in the query
layer from rows that describe things that actually happened.

The alternative - a nightly job writing "no response" rows, or an
`IsAwaitingResponse` column - manufactures events out of the absence of events,
and then owns the problem of keeping them true. A buyer who replies at 11pm makes
every stored silence flag wrong until something notices.

The same rule kills every convenience boolean: there is no `HasSubmission`, no
`HasBeenPitched`, no `SubmissionCount` column. Counts and last-activity instants
are projected at read time from the submission, pitch and response rows. Two
facts that can disagree are one fact too many.

### A pitch is one interaction, read commercially

`OpportunityPitch` has a **required, unique** `InteractionId`. Recording a pitch
creates the pitch and its interaction in one command, in one transaction.

The user is never asked to create an interaction first and attach a pitch to it.
Two steps is how one meeting ends up in the system twice - once as a call log by
whoever was in the room, once as a pitch by whoever updates the pipeline - with
the same participants and the same timestamp and no way to tell they are the same
event. The unique index makes the duplicate impossible rather than unlikely.

The pitch does not restate the interaction. Participants, timestamp, type and
detailed notes live on the interaction; the pitch adds only the commercial
reading - kind, outcome, what was shown.

### Subjects use typed foreign keys, not a kind plus a raw GUID

`OpportunitySubject` carries four nullable, typed, tenant-qualified foreign keys -
`TalentProfileId`, `ProjectId`, `PackageId`, `ProjectRoleId` - with a CHECK
constraint asserting exactly one is set and a second asserting it matches the
declared kind.

ADR-0019 accepted a validator-only raw identifier for `PackageElement` and named
that validator as the thing to watch. M6 does not repeat it. The exclusive-arc
shape costs four columns and two check constraints and buys structural integrity:
a subject cannot point at another tenant's project even if somebody pastes in a
GUID, because there is no column that would accept it.

`PrimarySubject` is derived from the opportunity's kind and the subjects present -
`TalentEngagement` requires a talent profile, `Staffing` a project role, and so
on. It is never a stored flag, so it cannot disagree with the subject rows.

### The model is not only about selling projects, and not a junk drawer

`OpportunityKind` is `TalentEngagement`, `ProjectMarket`, `PackageMarket`,
`Staffing`, `Partnership`, `Other`. Placing a client in someone else's project,
taking a project out, taking a package out and filling a role are all genuine
agency pursuits with different subjects, and each kind requires the subject that
makes it that kind.

`Other` exists and requires no particular subject. That is the pressure valve, and
it is deliberately narrow: it does not make the model generic, and every other
kind is refused without its subject rather than silently accepted.

### Task links are a separate table

Follow-ups are ordinary M2 tasks. `OpportunityTaskLink` joins a task to a pursuit
and optionally a target, with a unique index on the task: a task belongs to at
most one pursuit.

The alternative was another nullable id column on `TaskItem`, which already
carries the M2 subject shape. Every milestone that adds a linkable thing would add
another nullable column and another combination nothing validates. A join table
adds one row per link and stays the same size as the system grows.

Recording a submission or a pitch may create its follow-up in the same
transaction, so the next action exists because the activity happened rather than
because somebody remembered.

### One open target per party, enforced in the database

```sql
CREATE UNIQUE INDEX ux_opportunity_targets_one_open_company
ON opportunity_targets (opportunity_id, company_id)
WHERE company_id IS NOT NULL AND stage IN (1, 2, 3, 4, 5, 6);
```

Two colleagues working the same buyer on the same project without knowing about
each other is the situation this part of the system exists to prevent. Closed
stages are excluded, so a buyer who passed can be approached again later - which
happens, and is a new conversation rather than a resurrection of the old one.

### Strategy notes get their own permission and stay out of the search vector

`opportunities.strategy.read`, separate from `packages.strategy.read` and
`talent.notes.read`. A pursuit's strategy routinely names who the agency expects
to pass and why, which is a different sensitivity from client positioning.

Redaction goes through the shared `SensitiveNotes` from M4, so detail, list,
saved views and search all apply the identical rule, and the field is returned
**absent** rather than refused - absent is deliberately indistinguishable from
empty. Strategy is excluded from the generated `search_vector`, because a caller
who could confirm a note's contents by searching a phrase and watching the pursuit
surface would have defeated the redaction entirely.

### Saved-view filters are validated against a single accept-list

M5 shipped a defect where seven new filter fields were dropped in the API's
field-by-field mapping; the reflection round-trip test caught it. M6 does not rely
on that test alone.

`SavedViewDefinition` now declares `AcceptedFilters` per target - an accept-list
rather than the previous reject-lists - and `SetFilterNames`/`AllFilterNames` are
derived by reflection over the filter record. A new filter field is therefore
covered by the completeness test automatically, and a filter accepted for the
wrong target is a validation failure rather than a silently ignored value.
Definition version moves to 4 and `TargetIntroducedIn` records that `Opportunities`
arrived at version 4, so an older client reading a newer view fails loudly.

### Everything in M6 is online-only

No M6 read is cached and no M6 write is queued. The local cache schema stays at
v2.

A stale pipeline is worse than no pipeline: an agent who submits to a buyer
because their cached copy did not show yesterday's submission has done real damage
in the world. The offline write queue exists for the M2 shapes it was designed
around, and expanding it to market activity would mean replaying a submission
against a pursuit that closed while the client was offline.

## Consequences

- One buyer passing changes one target's stage and nothing else, so a pursuit's
  status stays an honest answer to "are we working this".
- Every pipeline number - submitted, awaiting a reply, last activity, overdue - is
  computed from event rows, so it cannot be stale, but the read queries are
  correspondingly more work than reading a column.
- M7 attaches an offer to a target, or to a submission, or to a pitch, without
  M6 having pre-judged which. No M6 row carries a nullable `DealId`.
- AgencyOS cannot tell the user whether a submission arrived, and says so on the
  screen. That is a real limitation, deliberately visible.
- Backdated `SentAt` means the submission timeline reflects when things happened
  rather than when they were typed, and consequently is not an audit trail. The
  audit log is, and it records when each row was actually written.

## Evidence that would cause reconsideration

- A pursuit shape whose progress genuinely cannot be read off its targets, which
  would mean the two-axis split is missing a third axis rather than being wrong.
- Derived pipeline counts becoming a measurable read-path problem at real volume,
  which would justify a maintained projection - a projection, not a column on the
  aggregate, and still never the canonical fact.
- An outbound mail integration in M10 that can genuinely confirm delivery, which
  would make "recorded, not sent" a per-submission distinction rather than a
  system-wide one.
- A second thing needing to link to a task, which would confirm the join table was
  the right shape, or a year with no such thing, which would suggest it was not.
