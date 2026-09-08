# ADR-0030 — Intelligence: provenance, judgment, and the refusal to score

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M11 — Intelligence: sources, signals, theses, predictions,
  watchlists, relationship intelligence, talent radar and research workflows
- **Supersedes:** nothing
- **Builds on:** ADR-0011 (composite tenant keys), ADR-0012 (audit and domain
  history), ADR-0014 (concurrency and idempotency), ADR-0021 (economic
  redaction in saved views), ADR-0025 (document classification and linking)

## Context

M11 is where AgencyOS starts recording what the agency *knows* and what it
*thinks*, as distinct from what it has done. Everything before it recorded facts
with an owner: a contract was signed, a payment arrived, a message was sent. This
milestone records claims, positions and expectations — things that can be wrong.

That difference is the whole design problem. A system that stores "Sable is
leaving Northgate" next to "the Northgate contract was executed on 14 March" and
renders them the same way has quietly asserted that the first is as settled as the
second. Six months later somebody makes a decision on it.

The brief was explicit about the failure modes to avoid, and they are the ones this
milestone would fall into naturally: collapsing the chain into one "intelligence
note", labelling a claim true, inventing a relationship score, and adding a model
that summarizes or extracts. Each is easy, each looks like progress, and each
destroys the thing that makes the record worth keeping.

## Decisions

### 1. The chain is kept apart in the type system

A **source** is evidence. A **signal** is a claim with provenance. A **thesis** is
a position somebody holds. A **prediction** is a falsifiable statement with a date
and a probability. A watchlist, a radar entry and a research case are standing
pieces of work built on those four.

They are separate aggregates, separate tables, separate endpoints and separate
screens. Nothing in M11 merges two of them, and there is no "intelligence item"
type anywhere.

The temptation is real: all six have a title, a classification, an owner and a
date, and one table with a discriminator would be less code. It would also lose
the only thing that matters — that a rumour, a considered position and a
falsifiable forecast are different kinds of statement, and that treating them alike
is how an agency talks itself into a decision.

### 2. A signal keeps at least one source, and the rule never relaxes

`Signal.RemoveEvidence` refuses to remove the last citation. So does the handler.
So does a trigger on `signal_evidence`, which allows the delete only when the
parent signal is already gone — the cascade case.

Three enforcements of one rule is unusual in this codebase and deliberate here. A
claim with no provenance is a rumour with a database row; it is indistinguishable
from something somebody made up; and it is exactly what a hurried code path would
produce. The database is the only layer no future code path can go around.

### 3. There is no `Verified`

`SignalVerification` is `Unverified`, `Corroborated`, `Disputed`, `Retracted`.

Corroborated means independent evidence agrees. It does not mean the claim is
true, and AgencyOS has no way to determine that. A fifth member reading as "true"
would be applied by people meaning "I checked and it is right", and the system
would then be storing a truth claim it cannot support. The word is absent from the
domain, from the contract, from the client and from a unit test that asserts no
rendering of any state reads as an assertion of fact.

`ThesisStatus` is `Draft`, `Active`, `Retired`, `Superseded` for the same reason:
there is no True and no False. A position is abandoned by recording why.

### 4. A probability is somebody's assertion, stored as a decimal, never edited

`PredictionRevision` holds a `numeric(5,4)` probability with the forecaster and the
moment. A new forecast is a new revision; a trigger refuses `UPDATE` and refuses
`DELETE` while the prediction exists.

Money is not the only thing that must not be a float (`CLAUDE.md` §5): a Brier
score is `(probability − outcome)²`, and a forecast stored as a double stops being
the number somebody stated by the time it is scored. Precision is capped at four
decimal places, because a forecaster who types 0.6237 is asserting a precision they
do not have.

The immutability is not fussiness. Calibration is measured against the history, and
a forecaster who could revise a number after resolution would score perfectly every
time.

### 5. `Unresolvable` is a real outcome, and is never scored

A question whose answer never became knowable resolves as `Unresolvable`. It is
counted in the calibration model and excluded from every figure.

Scoring it as half right would manufacture a number from an absence. Scoring it as
wrong would push forecasters towards questions that are easy to grade rather than
questions worth asking.

### 6. Calibration is arithmetic and a sample count, never a verdict

`PredictionCalibrationModel` carries a mean Brier score, a mean probability, an
observed frequency and the counts they were computed from. There is no
"well calibrated", no grade and no forecaster ranking anywhere in the milestone.

A mean over eleven resolved predictions supports very little. The sample count
travels beside every figure — in the model, in the contract and on the screen — so
a reader can see that for themselves rather than being told a conclusion.

### 7. Relationship intelligence has dimensions and no score

`RelationshipIntelligenceModel` reports what a person recorded (`RecordedStrength`,
their note, the lead agent) and, separately, what the M2 rows count (interactions
over 30, 90 and 365 days, the last contact, open and overdue tasks).

There is no `RelationshipHealth`, no `Affinity` and no `InfluenceScore`. A
composite would be arithmetic over incommensurable things — emails, meetings, a
subjective 1-to-5 — and its apparent precision would be believed. Fourteen emails
is not a strong relationship, and the model refuses to say it is; where nobody has
recorded an assessment, the client renders "Not recorded" rather than filling the
gap from the counts.

### 8. Classification is stated, never inferred, and applied in SQL

`IntelligenceSensitivity` is `Internal`, `Confidential`, `SourceSensitive`,
`Restricted`. Whether somebody spoke in confidence is something they said, not
something a system can detect from wording, so nothing infers it.

Every query narrows by the caller's readable classifications **inside the SQL** —
including the detail reads, not only the lists. A thesis a member may open can cite
a source-sensitive signal, and a citation naming that signal would disclose it just
as surely as a list would. Counts are computed over the same narrowed set, so a
source's citation count and a thesis's supporting count agree with the rows the
reader can actually see.

The top-level object is the exception: a detail read fetches it unnarrowed and the
application refuses it with a 403 rather than hiding it as a 404, so somebody who
followed a citation learns a grant exists to ask for.

Global search is narrowed harder still: it finds `Internal` claims only, for
everybody. The palette shows results beside people and projects, and its result
count is visible before anything is opened — a source-sensitive claim surfacing
there, or merely raising the count, is the disclosure the classification exists to
prevent.

### 9. Subjects live in one table, with a real key per kind

`intelligence_subjects` is a table-per-hierarchy: one table, five owner arcs
(signal, thesis, prediction, watchlist, research case) and ten subject arcs
(person, company, talent profile, project, source property, package, project role,
opportunity, deal, contract), each with a composite `(organization_id, id)` foreign
key.

Five tables of ten arcs each would be fifty foreign keys saying the same thing.
More to the point, "what does the agency know about this person" wants one scan
across all five kinds at once, and that only works if they share a table.

The single `subject_id` column sits beside the typed columns, on M10's `target_id`
precedent, with a check constraint proving they agree. Adding an eleventh subject
kind is deliberately not free.

### 10. The event foreign keys are deferred; nothing else is

`intelligence_events` carries seven composite keys declared
`DEFERRABLE INITIALLY DEFERRED`. Alone among the arcs, they have to be: an event is
written in the same unit of work as the object it happened to, EF orders inserts by
the relationships it knows about, and it knows about none of these — the first
"record a thesis" wrote the history row before the thesis.

Checking at commit rather than at statement is what deferred constraints are for.
The alternative was trading the key for insert ordering, which would have left the
history able to point at nothing.

### 11. The radar hands over to M4 and stops

`TalentRadarStatus` is `Watching`, `Researching`, `ReadyForReview`,
`ConvertedToProspect`, `Dismissed`. There is no Contacted, no Courting and no
Signed.

Those states exist in M4, on a prospect and a representation. A second pursuit
pipeline here would be two systems disagreeing about the same relationship, and the
disagreement would surface at the worst moment. Conversion creates the prospect,
creates a talent profile if there is not one, and marks the radar entry converted
last, so a failure leaves the entry open rather than orphaning a pursuit.

One open radar entry per person, enforced by a partial unique index. Two would be
two analysts researching in parallel without knowing.

### 12. A research case holds no findings

A case gathers sources, signals, theses, predictions and tasks, and records a
question, a context and a conclusion. It has no `ResearchFinding` type.

A finding on the case would be a fifth kind of claim with none of the provenance
rules the other four have, sitting beside them and looking equally solid. What the
research concluded belongs in a thesis, where it can be revised, challenged and
retired with a reason.

### 13. No model, no summarizer, no extraction

M11 adds no OpenAI or Anthropic client, no local model, no embeddings, no vector
column, no semantic search, no RAG, no automatic summarization, no signal
extraction, no thesis generation, no sentiment analysis and no talent ranking.
There is no route on the server that would answer such a request and no button in
the client that would make one.

This is not a deferral on capability grounds. The value of this milestone is that
every judgment in it belongs to a named person on a stated date. A summary with no
author, or a signal extracted from a document by a program, is a claim nobody is
accountable for — and the moment one of those sits in the same list as a claim
somebody staked their name on, the list stops meaning anything.

`docs/09_AI_RUNTIME.md` describes where AI belongs when it arrives: behind
capabilities, never mutating the database directly, never bypassing permissions.
Nothing in M11 changes that, and nothing in M11 exercises it.

### 14. No Python service, no new F# project, no new specification

M11 introduces no distributed protocol. Every operation is a single request against
a single database inside one transaction; the only ordering subtlety is the radar
conversion, which is sequential and idempotent.

Brier arithmetic is a dozen lines of pure decimal C# with no state machine in it,
so `ForecastCalibration` lives in the application layer rather than in F#. There is
nothing here for TLA+ to model that a check constraint does not already state.

## Consequences

Intelligence is `ONLINE_ONLY` in `docs/13_OFFLINE_CLASSIFICATION.md`. Nothing is
cached and the local cache schema is unchanged at v2. A source-sensitive signal
names somebody who spoke in confidence, and a copy on a laptop is not something a
later revocation can take back.

The API contract moves to 11, additively. Saved views reach definition version 9
with four new targets and no probability filter, for the reason ADR-0021 gave about
compensation: a saved view is a query somebody else may run, and a predicate
narrowing by a forecast would tell its reader the forecast.

Five new permissions: `intelligence.read`, `intelligence.write`,
`intelligence.sensitive.read`, `intelligence.predictions.write` and
`intelligence.radar.write`. The three elevated classifications share one grant on
M10's precedent — what varies between them is what they protect, not who should see
it — and Owner holds all five, so an elevated classification is reachable in a
fresh tenant.

Thirty-six audit actions, none of them reads. Reading intelligence is not audited,
for the reason M10 gave: a trail that grew on every read is a trail nobody can
search when something actually happens.

What this milestone does not answer: whether a claim is true. That was never
available to it, and the design is an attempt to stop the system implying
otherwise.
