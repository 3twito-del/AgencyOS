# Offline Classification

Every AgencyOS command and query is classified for offline behaviour. The
classification is deliberate and narrow: the offline write queue is a safety
mechanism, and widening it casually is how a queue stops being safe.

Three classes:

- **ONLINE_ONLY** — refused when the server cannot be reached. Never queued.
- **OFFLINE_SAFE** — may be queued while offline, submitted later under a stable
  idempotency key, and re-authorized by the server when it finally runs.
- **OFFLINE_READ_ONLY** — served from the encrypted local cache when the server is
  unreachable, clearly labelled as a possibly stale copy.

The rule for admitting anything to `OFFLINE_SAFE` is that its conflict semantics
must be **defined and proven**, not merely plausible. See
`docs/adr/ADR-0013-synchronization-architecture.md`,
`ADR-0014-concurrency-and-idempotency.md` and `specs/OfflineWriteQueue.tla`.

## OFFLINE_SAFE (M2/M3 — unchanged in M4, M5, M6 and M7)

| Command | Why it is safe |
|---|---|
| CreatePerson | Creation guarded by an idempotency key; a replay returns the original answer. |
| UpdatePerson | Carries `expectedVersion`; a stale edit is refused, not applied. |
| CreateCompany | As CreatePerson. |
| UpdateCompany | As UpdatePerson. |
| CreateTask | As CreatePerson. |
| CompleteTask | Guarded transition; replay is idempotent, stale is refused. |
| ReopenTask | As CompleteTask. |
| RecordInteraction | Append-like; guarded by an idempotency key. |

M4 adds **nothing** to this list.

## ONLINE_ONLY (all M4 mutations)

| Command | Why it is not queued |
|---|---|
| CreateTalentProfile | Uniqueness is per person per tenant. Two devices creating one offline would both believe they succeeded, and only one can. |
| UpdateTalentProfile | Version-guarded, but see the note on notes below. |
| AddTalentDiscipline / EndTalentDiscipline | Effective-dated; an offline date is the device's idea of today, which may be wrong by the time it submits. |
| CreateProspect | At most one open pursuit per person. Two agents starting one offline is exactly the collision the invariant exists to prevent, and neither would know. |
| AdvanceProspectStage | The legal target depends on the current stage, which an offline client may not have. |
| ConvertProspectToRepresentation | **The most consequential command in the milestone.** It creates a relationship, closes a pursuit and assigns a lead in one transaction. Its conflict semantics offline are not merely unproven, they are unattractive: a conversion queued on Monday and submitted on Thursday would sign a client on terms nobody re-confirmed. |
| CreateRepresentation | As ConvertProspect. |
| Activate / Suspend / Resume / Terminate / Expire | Legality depends on current status. A suspend queued against a representation that has since terminated is not a conflict to resolve, it is a command that should never have been made. |
| AddRepresentationScope / EndRepresentationScope | Effective-dated, and scope changes are commercially meaningful. |
| AssignRepresentationTeamMember / RemoveRepresentationTeamMember | Membership is checked server-side at execution; queuing would defer an authorization question. |
| AddCredit / UpdateCredit | Could be made safe later — see below. |
| AddMaterial / UpdateMaterial | Could be made safe later — see below. |

### Why credits and materials are ONLINE_ONLY for now

They are the two M4 commands that would qualify most easily: both are additive,
both are version-guarded on update, and both would be safe under an idempotency
key. They are excluded because M4 has no offline capture surface for them, so
admitting them to the queue would widen it for a workflow that does not yet exist.

`CLAUDE.md` section 5 forbids infrastructure with no current workload, and a queue
entry that nothing enqueues is exactly that. The condition for adding them is a
Windows surface that genuinely captures a credit or a material away from a
connection.

## ONLINE_ONLY (all M5 mutations)

| Command | Why it is not queued |
|---|---|
| CreateProject / UpdateProject | Could be made safe; see below. |
| ChangeProjectStatus | The legal target depends on the current status, which an offline client may not have. |
| ChangeProjectStage | Effective immediately and freely reversible, so a stale queued move would silently undo a colleague's correction. |
| CreateProjectRole / ChangeProjectRole | Role exclusivity is a statement about the whole roster, and the roster is what an offline client cannot see. |
| AttachToProjectRole | **The invariant this milestone exists to protect.** Two agents attaching different directors offline would both believe they succeeded, and only one can. Deferring that discovery by hours is worse than refusing it now. |
| ChangeAttachmentStatus | Legality depends on the current status, and ending an attachment that has already ended is not a conflict to merge. |
| AddProjectCompany / EndProjectCompany | Effective-dated, and one open involvement per capacity is enforced per project. |
| CreateSourceProperty | Could be made safe; see below. |
| LinkSourceProperty / UnlinkSourceProperty | Version-guarded against a project the client may not hold the current version of. |
| LinkMaterialToProject / UnlinkMaterialFromProject | As above. |
| LinkCreditToProject | Asserts that a credit and a project are the same work. That is a judgement worth making with the current record in front of you. |
| CreatePackage / UpdatePackage | Could be made safe; see below. |
| ChangePackageStatus | Legality depends on the current status. |
| AddPackageElement / RemovePackageElement | Element targets are validated against this tenant at execution; queuing would defer that check. |

### Why the additive M5 commands are still ONLINE_ONLY

`CreateProject`, `CreateSourceProperty` and `CreatePackage` would qualify: all
three are additive, none has a uniqueness constraint that two offline clients
could both violate, and an idempotency key makes each replay-safe.

They are excluded for the same reason M4 excluded credits and materials: there is
no offline capture surface for them, so admitting them would widen the queue for a
workflow that does not exist. `CLAUDE.md` section 5 forbids infrastructure with no
current workload, and a queue entry nothing enqueues is exactly that. The
condition for adding them is a Windows surface that genuinely opens a project away
from a connection.

## ONLINE_ONLY (all M6 mutations)

| Command | Why it is not queued |
|---|---|
| CreateOpportunity / UpdateOpportunity | Could be made safe; see below. |
| ChangeOpportunityStatus | The legal target depends on the current status, and closing requires an outcome the offline client cannot validate. |
| AddOpportunitySubject / RemoveOpportunitySubject | The subject is resolved against this tenant at execution; queuing would defer that check. |
| AddOpportunityTarget | **One open target per party is enforced per opportunity.** Two agents adding the same buyer offline would both believe they succeeded. |
| UpdateOpportunityTarget / MoveOpportunityTarget | Stage legality depends on the current stage, which a stale client may not hold. |
| RecordTargetResponse | Describes something a buyer did. Recording it against a target that has since been withdrawn is not a conflict to merge. |
| RecordSubmission | **The one that matters.** A queued submission would replay against a pursuit that may have closed while the client was offline - and the user would have already told a client it went out. |
| AmendSubmission | Version-guarded against a row the offline client may not hold the current version of. |
| RecordPitch | Creates a pitch and its interaction in one transaction. A queued half of that pair is a duplicated meeting. |

### Why the additive M6 commands are still ONLINE_ONLY

`CreateOpportunity` would qualify on the same reasoning as `CreateProject`: it is
additive, has no uniqueness constraint two offline clients could both violate, and
an idempotency key makes it replay-safe.

It is excluded for the reason M4 and M5 gave, and one stronger. There is no
offline capture surface for a pursuit, so admitting it would widen the queue for a
workflow that does not exist. And market activity is the wrong class of work to
hold in a queue at all: a submission the user believes went out, held on a laptop
for four hours, is worse than a submission refused with an explanation, because
the user has already acted on the belief.

## ONLINE_ONLY (all M7 mutations)

| Command | Why it is not queued |
|---|---|
| CreateDeal | **One live negotiation per target per kind.** Two agents opening one offline would both believe they succeeded. |
| UpdateDealMetadata | Version-guarded, and the kind may not change once offers exist. |
| CloseDeal / ReopenNegotiation | Reopening unwinds an acceptance. Replaying that against a deal somebody else already reopened is not a conflict to merge. |
| DraftOffer / UpdateDraftOffer | Could be made safe; see below. |
| ChangeDraftOfferTerm | Legality depends on the offer still being a draft, which a stale client cannot know. |
| RecordOffer / RecordDraftOffer | **The one that matters most.** A queued offer replays against a negotiation that may have been accepted or closed in the meantime - and the user has already told somebody the number went across. |
| AnswerOffer (accept) | **The most consequential act in the milestone.** A queued acceptance would agree terms the agency may no longer be offering, hours after the fact. |
| AnswerOffer (reject, withdraw, expire) | Each describes something a specific party did. Recording it against an offer that has since been superseded is a false statement, not a merge conflict. |

### Why even the additive M7 commands are ONLINE_ONLY

`CreateDeal` and `DraftOffer` would qualify on the reasoning M4, M5 and M6 used:
additive, idempotency-keyed, no uniqueness two offline clients could both violate.

They are excluded, and the reason is stronger here than it was there. Market
activity was the wrong class of work to hold in a queue; commercial terms are
worse. An offer the user believes went across, held on a laptop for four hours, is
a number somebody has already repeated on a call. An acceptance held the same way
is an agreement the agency believes it has and the counterparty does not. Neither
is a conflict later synchronization can repair, because the damage happened outside
the system.

## OFFLINE_READ_ONLY

| Read | Cached since |
|---|---|
| People list and detail | M3 |
| Companies list | M3 |
| Tasks list | M3 |
| Talent and client list, with representation status, lead and scopes | M4 |

M5, M6 and M7 add nothing to this table. See below.

M4 extends the change feed with a talent entry keyed by **person**, because the
cached talent row denormalizes representation status and a representation change
must refresh it too. That entry is emitted for both talent-profile and
representation changes.

The cache schema moves from version 1 to version 2. The upgrade is additive and
**preserves the write queue**, which is the only thing in the file the server has
never seen; discarding a cache to avoid writing a migration would lose a user's
work. `AgencyOS.Tests.Unit.Client.CacheMigrationTests` proves the path before any
real cache depends on it.

### Deliberately not cached: the whole slate

**M5 adds no cached projections, and that is a policy answer rather than an
omission.** Caching needs a bounded rule saying *which* records, and "all
projects" is not one.

Talent was cacheable because "the client list" is a well-defined, bounded set an
agency can reasonably hold on a laptop. The slate has no equivalent bound: an
agency's projects run to the hundreds, most of them are somebody else's work, and
the obvious candidate rules - active only, mine only, recently touched - are three
different answers to a question nobody has yet asked. Picking one on speculation
is what section 21 of the milestone brief warns against, and picking wrong means
either a cache that misses what the user wanted or one that syncs a tenant's whole
history to every device.

The condition for revisiting is a stated rule about which projects a user needs
away from a connection. Until then, project and package reads are refused offline
and say so, which is honest.

The cache schema therefore stays at **version 2**. M5 adds no migration.

### Deliberately not cached: the pipeline

**M6 adds no cached projections either, and here the reasoning is stronger than a
missing bound.**

A stale pipeline is not merely unhelpful; it is dangerous. An agent whose cached
copy does not show yesterday's submission may submit to the same buyer again, or
tell a client that nobody has seen the material when three people have. The damage
happens in the world, outside the system, and no later sync repairs it.

"All opportunities" also has no bounded deterministic rule, exactly as "all
projects" did not. The plausible candidates - mine, active, recently touched - are
three different answers to a question nobody has asked, and the pipeline is worked
collaboratively, so "mine" is the least stable of the three.

The condition for revisiting is the same: a stated rule about which pursuits a
user needs away from a connection, and a surface that makes their staleness
impossible to miss.

The cache schema therefore stays at **version 2**. M6 adds no migration.

### Deliberately not cached: negotiations

**M7 caches nothing either, and here the case is the strongest of the three.**

A stale pipeline misleads. A stale negotiation misleads about money. An agent whose
cached copy shows the offer at 500,000 when it was countered to 650,000 an hour ago
will quote the wrong figure, and the person they quoted it to will remember it.

Economics makes it worse still. Term values are gated by
`deals.economics.read`, and a cached copy would outlive the permission that
justified reading it - a device that still holds compensation figures after the
grant is revoked is a leak with no server-side remedy.

The condition for revisiting is the same as for the slate and the pipeline: a
stated rule about which negotiations a user needs away from a connection, a bounded
set, and a surface that makes staleness impossible to miss. Economic terms would
need a further answer about revocation before they could be cached at all.

The cache schema therefore stays at **version 2**. M7 adds no migration.

### Also deliberately not cached

- **Client overview** — composes tasks, interactions, credits, materials and
  history. An offline copy would be a snapshot of five things of differing
  staleness presented as one coherent answer, which is worse than saying the
  server is unreachable.
- **Representation detail, credits, materials** — read online. Caching them would
  require answering what a stale credit list means when somebody is about to
  quote from it.
- **Prospects** — a pursuit is worked collaboratively and its stage changes often;
  a stale pipeline is actively misleading.
- **Saved views** — small, server-backed and changed deliberately. A stale
  definition would quietly run the wrong query.
- **Opportunity detail, submissions, pitches, the pipeline board and the market
  command centre** — every figure on them is derived at read time from event rows,
  so a cached copy would be a projection of a projection, stale in a way the user
  could not inspect.
- **Opportunity strategy notes** — permission-dependent. A cached copy would
  outlive the permission that justified reading it.
- **Deals, offers, terms, comparisons, the deal board and the deal command
  centre** — every figure on them is money, derived, permission-gated, or all
  three.
- **Deal strategy notes** — as above, and more sensitive: they routinely state
  what the agency will settle for.
- **Offer comparison** — computed by the rules kernel from two offers. A cached
  diff would be a stale answer about money presented as a current one.

## How the client behaves offline

- Reads fall back to the cache and the surface says so. Offline search results are
  labelled as local matches, and carry no relevance score, because inventing one
  the server did not produce would make them look authoritative.
- Writes that are `OFFLINE_SAFE` are queued, shown in the Sync and Offline
  surface, and retried under their original key.
- Writes that are `ONLINE_ONLY` fail with the server's own explanation rather than
  being silently held.
- The status line always states connection, staleness and how many changes have
  not reached the server. An application that is quietly offline is one that loses
  somebody's afternoon.
