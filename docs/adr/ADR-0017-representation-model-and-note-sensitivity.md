# ADR-0017: Representation is a relationship, prospecting is a pursuit, and client-ness is derived

Status: Accepted
Date: 2026-09-07

## Context

M4 turns AgencyOS into a representation system. The central modelling question is
how to record that the agency represents somebody, and the tempting shortcuts are
all wrong in ways that only show up months later:

- a `IsClient` boolean on `Person`;
- one lifecycle enum spanning prospecting and representation;
- a `PrimaryAgentUserId` column on the representation.

Each is smaller to write and each creates a second source of truth.

## Decision

### Person stays the human. TalentProfile is the agency's view of them

There are no `Actor`, `Writer` or `Director` subclasses. A person who acts and
writes is one person with two disciplines, not two records, and the moment they
are two records every question about "how many people do we represent" has two
answers.

`TalentProfile` carries the agency's representation metadata, separately from
`Person`, because a person exists whether or not the agency has a view about them.
Merging them would put internal positioning on the same row as a contact's phone
number and give every person columns that are meaningless for most of them.

### Prospecting and representation are separate state machines

`ProspectStage` (Identified → Contacted → Courting → Declined | Lost | Converted)
and `RepresentationStatus` (Pending → Active ↔ Suspended → Terminated | Expired)
are distinct.

One enum would make `Courting` and `Suspended` siblings, which they are not: one
is a pursuit the agency is running, the other is a relationship with a validity
period. More decisively, it would leave nowhere to record a **former client the
agency is now courting again** - an ordinary thing to happen, and something M4 is
explicitly required to support. With two concepts that case is just a terminated
representation and a new prospect, which is what actually happened.

Both tables are published as data (`AllowedTransitions`) rather than scattered
through methods, and a test enumerates every state paired with every target. For
machines this small that is the whole state space, so it is a proof rather than a
sample.

### Client-ness is derived, never stored

Somebody is a client exactly when they hold an `Active` representation. There is
no flag. A stored flag is a second source of truth that drifts the first time
somebody terminates a representation without remembering to clear it, and the
drift is invisible until a client list is wrong in front of a client.

### The lead representative is derived from the team

`Representation` has no `PrimaryAgentUserId`. The lead is the team member holding
`Lead` with no end date, and a partial unique index allows exactly one. The
invariant "the primary representative is a member of the team" is then true by
construction rather than by a check that drifts the first time somebody is removed
from the team.

### At most one live representation per person, enforced three times

The handler checks, the domain refuses, and a partial unique index refuses:

```sql
CREATE UNIQUE INDEX ux_representations_one_live_per_person
ON representations (organization_id, person_id)
WHERE status IN (1, 2, 3);
```

A duplicate active representation is what a retried conversion would produce, and
the agency believing it represents somebody twice is not a state worth being
merely unlikely. The index holds even when the first two layers are bypassed
entirely - which an integration test demonstrates by converting one prospect from
eight clients at once and asserting exactly one representation results.

### Effective dates, not overwrites

Scope and team are effective-dated rows; status and stage changes are append-only
events. Ending a scope closes its row rather than deleting it, so "we picked up
their literary representation in 2027 and dropped it in 2029" survives. Dates are
`DateOnly`, because "representation started on 3 March" is a business date and
storing it as a timestamp would invent a time of day nobody agreed.

### Credits carry an M5 seam, not a fake project

M4 has no canonical `Project`. A credit records the work by title, as reported,
and carries a nullable `ProjectId` with no foreign key. Inventing placeholder
project rows now would be worse than leaving the column empty: they would look
canonical, other things would start referencing them, and M5 would have to unpick
real relationships rather than fill in a column.

### Materials are metadata, and say so

`ExternalUri` must be an absolute HTTP or HTTPS address. A device-local path is
**refused**, not stored: it resolves on exactly one machine, breaks the moment the
file moves, and carries the author's account name into a shared record. AgencyOS
stores no file content in M4 and the UI says so rather than looking like a
document store that silently is not one. M10 owns storage.

## Note sensitivity: the one fine-grained permission

M4 introduces information that is materially more sensitive than the records
holding it: a talent profile's internal positioning, and a prospect's strategy.

The permission model gains coarse read/write pairs for talent, representation and
prospects, matching M2's grant model, plus exactly one finer permission:
`talent.notes.read`.

A caller without it receives those two fields **absent**, not a refusal. An
observer can still see who the agency represents without reading what it privately
thinks about them. Absent and empty are deliberately indistinguishable, in the API
and in the Windows client, because a UI that hinted "something was withheld here"
would leak the fact that a note exists.

Redaction has one implementation, `SensitiveNotes`, and every read path that can
return these models calls it.

It was originally written inside `RepresentationQueryService`, on the reasoning
that a single authorized read path meant no endpoint could forget. That reasoning
was wrong within the same milestone: saved views run the projection directly, so a
saved Prospects view returned strategy notes in full to an observer for whom the
prospects endpoint redacted them. Redaction is a property of the data, not of the
service that happens to fetch it, so it now lives in a type both paths call.
`RepresentationSavedViewTests` asserts the two routes agree.

### Representation-team membership is not an authorization dimension

Being on a representation team says who *works* a relationship. It does not say
who may *read* it. Wiring team membership into authorization would make assignment
a way to escalate privilege, and would mean removing somebody from a team silently
revokes their access to records they may legitimately need.

Relationship-aware access control is therefore **explicitly deferred**. The
roadmap mentions ABAC; M4 has no concrete rule that requires it, and building an
attribute engine on speculation is exactly what `CLAUDE.md` section 5 forbids. The
condition for revisiting is a stated policy that some records must be invisible to
colleagues in the same tenant - at which point this decision should be reopened
rather than worked around.

## Why not formal methods here

M3 used TLA+ because its offline queue was genuinely distributed: lost messages,
crashes, interleaving. M4's lifecycles have none of that. They are small,
synchronous state machines, and their only real hazard is concurrent conversion -
which is made impossible by a database constraint and demonstrated by a concurrent
integration test against PostgreSQL.

Writing a model here because the previous milestone had one would be ceremony. The
exhaustive transition enumeration is stronger evidence for this shape of problem,
and the concurrency test exercises the thing a model would have had to assume.

## Two defects this shape did not prevent

Worth recording, because both were invisible to the compiler and to every test
written before the API surface was exercised end to end.

**Saved-view filters were never carried.** `SavedViewFilters` and
`SavedViewFiltersModel` have the same shape, so the endpoint mapped one onto the
other positionally. M4 appended eight fields to both; the positional call still
compiled and silently defaulted every one of them. The domain validated the new
filters, the query layer applied them, and the API passed none - so a saved view
for "my television clients" quietly returned every talent record in the tenant. A
returned superset is the worst failure mode available here, because it looks like
a working feature. Both mappings now use named arguments.

**That fix was weaker than this ADR originally claimed, and M5 proved it.** Named
arguments stop a value landing in the wrong field; they do nothing about a field
left out, because every filter is optional and omitting one still compiles. M5
added seven more filters and forgot all seven, in exactly the same way. Two
identical record shapes mapped by hand cannot be made safe by care, so
`SavedViewFilterMappingTests` now walks the property list by reflection and fails
when anything does not survive the round trip. That is the guarantee this
paragraph should have described the first time.

**Two paths differed only by parameter name.** `/talent/{personId}` and
`/talent/{talentProfileId}` route correctly in ASP.NET Core, which separates them
by HTTP method, and are the same path in OpenAPI, which does not. Profile
mutations moved to `/talent-profiles/{talentProfileId}`, and
`OpenApiContractTests` now fails on any pair that collapses to the same shape.

The shared lesson is that a contract test asserting paths exist does not assert
that the values crossing them arrive.

## Consequences

- Every representation question is answered from representation records, so there
  is no flag to fall out of step.
- Re-signing a former client is a first-class path rather than a special case.
- Ending a representation closes its scopes and team, so a terminated relationship
  leaves nothing dangling open.
- Writes to one person's representation serialize on the partial unique index.
  At one agency's volume that is unmeasurable.
- The transition tables are public API of the domain. Widening one is a visible
  diff and an immediate test failure, which is the intent.
- A new read path that returns talent or prospect models must call
  `SensitiveNotes`. Nothing in the type system forces this, so it is asserted by
  test instead; a path added without it is the failure to watch for.

## Evidence that would cause reconsideration

- A concrete requirement that colleagues in one tenant must not see each other's
  clients, which would justify reopening the deferred relationship-aware policy.
- An agency structure where one person is genuinely represented under two
  simultaneous agreements, which would break the one-live-representation
  invariant and require modelling agreements separately from the relationship.
- Sustained need to record representation terms precise enough that M8's contract
  model should own them instead.
