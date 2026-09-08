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

## OFFLINE_SAFE (M2/M3 — unchanged in M4, M5, M6, M7, M8, M9 and M10)

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

## ONLINE_ONLY (all M8 mutations)

| Command | Why it is not queued |
|---|---|
| CreateContract | Anchors to an accepted offer, and M7 can lawfully unwind an acceptance while the write sits in a queue. |
| UpdateContract / ChangeContractStatus | Version-guarded, and the legality of a transition depends on state a stale client cannot see. |
| RecordEffectiveDate | Refused after termination, which a queued client may not know has happened. |
| AddContractParty | Uniqueness of the signature row depends on the party set, and two clients adding offline would both believe they succeeded. |
| **RecordSignature** | **The most consequential act in the milestone.** It is the only route to execution, and a queued signature would mark a contract fully executed hours after the fact, dated from a signature the agency has already told somebody about. |
| RecordContractVersion | Version numbers are a sequence, and two clients recording offline would both claim the same number. The draft is also what reconciliation compares against. |
| ChangeContractTerm / FinaliseContractVersion | Legality depends on the version still being a draft, which a stale client cannot know. |
| RecordRightsGrant (including supersession) | Supersession names a specific predecessor row and its version. Replaying that against a grant somebody else already superseded is not a conflict to merge. |
| EndRightsGrant | As above. |
| RecordOption / ResolveOption | An exercise, a decline or a waiver describes what a specific party did on a specific day. Recording it against an option that has since been resolved is a false statement, not a merge conflict. |
| RecordObligation / ResolveObligation | **Breach especially.** A queued breach determination would assert a legal conclusion about a duty that may have been satisfied while the laptop was shut. |
| RecordNoticeRequirement / RecordNotice | A notice is an assertion that something passed between the parties. Held for four hours it is an assertion nobody can date. |
| CreateContractTask | Additive and idempotency-keyed, and excluded for the reason below. |

### Why even the additive M8 commands are ONLINE_ONLY

`CreateContract` and `CreateContractTask` would qualify on the reasoning M4, M5 and
M6 used: additive, idempotency-keyed, no uniqueness two offline clients could both
violate.

They are excluded, and the case is stronger than it was for negotiations. M7's
argument was that a queued offer is a number somebody has already repeated on a
call. M8's is that a queued legal act is a **position the agency has taken**. A
signature recorded offline is an executed agreement the counterparty may not have;
a breach recorded offline is a determination that may already be wrong. Neither is
a conflict later synchronization can repair, because the consequence happened
outside the system.

There is also no offline legal capture surface to serve. Admitting these commands
would widen the queue for a workflow that does not exist.

## ONLINE_ONLY (all M9 mutations, and all M9 reads)

M9 is the first milestone where the **reads** are online-only as well.

| Command | Why it is not queued |
|---|---|
| RecordMonetaryObligation | Refused unless the contract is operative, and a contract can be abandoned or superseded while the write sits in a queue. |
| QuantifyObligation / ReleaseObligation | Version-guarded, and both assert that somebody now knows something they did not. Held for four hours, it is an assertion nobody can date. |
| RaiseReceivable | The beneficiary decides whether collected money becomes agency revenue or a liability. A queued receivable with the wrong beneficiary would misstate income for as long as it took somebody to notice. |
| **RecordPayment** | **The act the milestone turns on.** A queued payment is money the agency believes it has. Two clients recording the same wire offline would both believe they succeeded, and the ledger would say the money arrived twice. |
| AllocatePayment / ReverseAllocation | Legality depends on what is outstanding *now*, which a stale client cannot see. An allocation replayed against a receivable somebody else has settled is not a conflict to merge; it is an over-allocation. |
| ReversePayment | Version-guarded, and it writes a second payment. Replaying it would reverse a payment twice. |
| RecordInvoice / IssueInvoice / VoidInvoice | The invoice number is unique per organization, and two clients numbering offline would both claim the same one. Issuing also asserts a date somebody has already told a payer about. |
| RecordAdjustment | A deduction is a fact from a remittance advice. Queued, it would reconcile a receivable hours after somebody looked at the variance and drew a different conclusion. |
| CreateCommissionRule / EndCommissionRule | Overlap is refused against the rules that exist. Two clients creating offline would both pass their own check and produce the overlap the check exists to prevent. |
| CalculateCommission | Chooses the governing rule by date and supersedes the previous entitlement by version. Replayed later, it would supersede an entitlement somebody has already acted on. |
| AdjustCommission | Version-guarded, and it changes what the agency says it is owed. |
| PostJournalEntry / ReverseJournalEntry | The narrowest and most consequential act in the system. A hand-written entry queued for hours would post into a period somebody has already reported on. |

### Why the M9 reads are online-only too

Every previous milestone could argue that a stale read is a slightly old read. A
person looking at yesterday's contract list knows roughly what they are looking
at, and the surface says the copy is stale.

Finance breaks that argument. **A balance computed from a four-hour-old copy is
not a slightly old balance; it is a different number**, and there is nothing on
the screen that tells the reader which one is in front of them. Outstanding,
unapplied, collected commission and every account balance are derived at read
time from allocation rows, so a cached page would be a projection of a projection
— stale in a way that looks exactly like current.

The consequence is also different in kind. A stale contract list leads somebody to
open the wrong record. A stale receivable balance leads somebody to tell a client
they have been paid.

So the whole M9 surface reaches the server or says it could not:

- receivables, invoices, payments and allocations;
- monetary obligations and their amounts;
- commission rules, entitlements and what has been collected;
- accounts, balances and journal entries;
- reconciliation, the finance history and the finance command centre.

There is also nothing to serve. There is no offline finance capture workflow, and
admitting these reads would widen the cache for a surface that does not exist.

The cache schema therefore stays at **version 2**. M9 adds no migration and no
`QueuedOperation` member; finance writes are online-only by construction rather
than by a check somebody could forget.

## ONLINE_ONLY (all M10 mutations, and all M10 reads)

M10 is online-only in both directions, for two different reasons.

| Command | Why it is not queued |
|---|---|
| RecordDocument / AddDocumentVersion | The bytes are the point, and a queue is not a content store. Holding a two-hundred-megabyte file in a local write queue would put a copy of a privileged contract on a laptop for as long as the queue took to drain. |
| UpdateDocument / ArchiveDocument / RestoreDocument | Version-guarded, and archiving states a reason as of a moment. Replayed hours later it would archive a document somebody has since replaced. |
| LinkDocument / UnlinkDocument | The target must exist in the tenant now. A link queued against a record somebody has since removed is a refusal delayed by four hours. |
| ConnectMailbox | Exchanges an authorization code, which expires in minutes. Queueing one guarantees it fails. |
| DisconnectMailbox / ChangeMailboxVisibility | Both are decisions about who may read somebody's correspondence, taken as of now. A queued visibility change would widen access at a time nobody chose. |
| LinkMessage / UnlinkMessage / ResolveParticipant | Identification is a judgment about a person. Queued, it would be applied against a mailbox the operator may no longer be permitted to read. |
| IngestAttachment | Fetches bytes from a mail provider using a credential that lives on the server. There is nothing a client could queue. |
| **ComposeMessage / QueueMessage / CancelMessage** | **The reason the whole milestone is online-only.** An actual send is never held in the generic M3 offline queue: a message queued for four hours is a message somebody has already been told was sent, and the queue's retry semantics — replay under the original key until it succeeds — are exactly wrong for an act that cannot be taken back (ADR-0028). |

### Why the M10 reads are online-only too

M9 established that some reads cannot be cached because a stale figure is a
different figure. M10's reason is different: **the content is the problem, not its
freshness.**

A privileged contract copied into a local SQLite file is a privileged contract on
a laptop. Revoking somebody's `documents.privileged.read` afterwards does not take
it back, and neither does removing them from the organization. The same is true of
a colleague's mailbox: a cached message stays readable after the visibility that
justified reading it has been narrowed.

So the whole M10 surface reaches the server or says it could not:

- documents, versions, links, history and extracted text;
- stored bytes, which leave through one authorized route and are never written to
  the local cache at all;
- connected mailboxes, messages, participants and attachments;
- outbound dispatches, their states and the communications command centre.

There is also nothing to serve. There is no offline document capture and no
offline compose workflow, and admitting these reads would widen the cache for
surfaces that do not exist.

The cache schema therefore stays at **version 2**. M10 adds no migration and no
`QueuedOperation` member; document and communication writes are online-only by
construction rather than by a check somebody could forget.

## OFFLINE_READ_ONLY

| Read | Cached since |
|---|---|
| People list and detail | M3 |
| Companies list | M3 |
| Tasks list | M3 |
| Talent and client list, with representation status, lead and scopes | M4 |

M5, M6, M7 and M8 add nothing to this table. See below.

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

### Deliberately not cached: contracts

**M8 caches nothing, and the reasoning compounds rather than repeats.**

A stale negotiation misleads about money. A stale contract misleads about what the
agency is bound to. An executed date, an effective date, an outstanding signature
or an option deadline read from a four-hour-old copy is exactly the kind of fact
somebody acts on immediately and does not re-check.

Three of M8's guarantees would also not survive caching:

- **Derived facts.** Execution state, outstanding signatures, effectiveness today,
  overdue obligations and the difference count are computed at read time from the
  rows that decide them. A cached copy would be a projection of a projection,
  stale in a way the user could not inspect - and the whole point of deriving them
  was that two facts which can disagree should not exist.
- **Three permission gates.** Terms need `contracts.terms.read`, their figures need
  `deals.economics.read`, and analysis, strategy and privileged rows need
  `contracts.privileged.read`. A cached copy would outlive every one of them. A
  device still holding privileged legal analysis after the grant is revoked is a
  leak with no server-side remedy, and privilege is the worst content in the system
  to leak.
- **Honest absence.** A deadline that could not be resolved is absent rather than
  guessed. A cached copy taken before the anchor event happened, read after it did,
  would show a gap that is no longer true - and the reader has no way to tell which
  kind of absence they are looking at.

The condition for revisiting is the one the slate, the pipeline and the
negotiations set, plus a fourth: a stated rule about which contracts a user needs
away from a connection, a bounded set, a surface that makes staleness impossible to
miss, and an answer about what a cached privileged field means after the permission
behind it is withdrawn.

The cache schema therefore stays at **version 2**. M8 adds no migration.

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
- **Contracts, drafting versions, terms, rights grants, options, obligations,
  notices, the legal deadline list and the legal command centre** — derived,
  permission-gated, or facts about legal position, and usually all three.
- **Legal analysis and contract strategy notes** — the most sensitive content in
  the system, and permission-dependent. Not cached, and the condition for
  revisiting includes answering what a cached privileged field means once the
  permission is revoked.
- **Reconciliation** — computed by the rules kernel from an accepted offer and a
  drafting version. A cached comparison would be a stale answer about whether the
  paper matches the deal, presented as a current one.
- **Stored documents and their bytes** — M10 gave AgencyOS files to hold, and none
  of them is cached. A privileged contract in a local database is a privileged
  contract on a laptop, and revoking the permission that justified reading it does
  not take the copy back (ADR-0025).
- **Synchronized messages and their attachments** — somebody else's
  correspondence, readable only while the mailbox visibility that justified it
  still stands. A cached copy would outlive that (ADR-0026).
- **Outbound dispatches** — a cached state would say a message is queued when it
  has already been sent, or the reverse. It is the one screen where a stale answer
  could lead somebody to send the same message twice (ADR-0028).
- **Receivables, invoices, payments, allocations, commissions, balances, journal
  entries, reconciliation, the finance history and the finance command centre** —
  every figure on them is money, derived at read time, permission-gated, or all
  three. See above: a stale balance is a different number, not an old one.
- **Account balances especially** — computed from posted lines each time they are
  asked for, per currency. A cached balance is the one number in the system that
  somebody would quote to a client without checking.

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
