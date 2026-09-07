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

## OFFLINE_SAFE (M2/M3 — unchanged in M4)

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

## OFFLINE_READ_ONLY

| Read | Cached since |
|---|---|
| People list and detail | M3 |
| Companies list | M3 |
| Tasks list | M3 |
| Talent and client list, with representation status, lead and scopes | **M4** |

M4 extends the change feed with a talent entry keyed by **person**, because the
cached talent row denormalizes representation status and a representation change
must refresh it too. That entry is emitted for both talent-profile and
representation changes.

The cache schema moves from version 1 to version 2. The upgrade is additive and
**preserves the write queue**, which is the only thing in the file the server has
never seen; discarding a cache to avoid writing a migration would lose a user's
work. `AgencyOS.Tests.Unit.Client.CacheMigrationTests` proves the path before any
real cache depends on it.

### Deliberately not cached

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
