# Security & Audit

## Identity
Early:
- development identity + explicit authorization architecture.

Mature:
- OpenID Connect / OAuth 2.x;
- Microsoft Entra ID where appropriate;
- Windows Hello / passkeys;
- MFA/conditional access.

## Authorization
Use:
- RBAC for broad roles;
- policy/attribute-based authorization for context;
- relationship-aware checks where needed.

Never trust the Windows client to enforce permissions.

## Data classification
Suggested:
- PUBLIC
- INTERNAL
- CONFIDENTIAL
- PRIVILEGED
- FINANCIAL
- RESTRICTED

M10 made five of these real for stored files. `DocumentSensitivity` is Internal,
Confidential, Privileged, Financial or Restricted; it is required with no default
and is **never inferred** from a filename, a folder, a document kind or what the
document is linked to. Privileged and Restricted each need their own read grant,
and Financial defers to `finance.read` so the classification does not become a
weaker second way to see the books.

A writer may not file a document into a classification they could not then read.
Otherwise anybody could hide a document from themselves, or move somebody else's
out of their reach.

A document list is narrowed **inside the query**, before anything is counted or
paged: a count of privileged documents about a named person is itself a
disclosure. See `docs/adr/ADR-0025-document-linking-sensitivity-and-search.md`.

M11 added a second, narrower scale for what the agency thinks.
`IntelligenceSensitivity` is Internal, Confidential, SourceSensitive or
Restricted; it is required with no default and is **never inferred** from the
content. Whether somebody spoke in confidence is something they said, not
something a system can detect from wording.

The three elevated classifications share one grant, `intelligence.sensitive.read`,
on M10's precedent: what varies between them is what they protect, not who should
see it. Forecasting and the talent radar take their own write grants, so somebody
who may record what they heard is not automatically somebody who may stake a
prediction or open a pursuit.

Every intelligence read is narrowed **inside the query**, including the detail
reads. A thesis a member may open can cite a source-sensitive signal, and naming
that signal in a citation would disclose it as surely as a list would; counts are
computed over the same narrowed set, so a source's citation count and a thesis's
supporting count agree with the rows the reader can see. The object itself is
refused with a 403 rather than hidden as a 404, so somebody who followed a
citation learns a grant exists to ask for.

Global search is narrowed harder: it returns Internal claims only, for everybody.
The command palette shows a result count before anything is opened, and a count is
an answer. See `docs/adr/ADR-0030-intelligence-provenance-judgment-and-no-scores.md`.

## Audit
Reads are not audited. Opening a document, listing messages and polling a mailbox
for changes produce no audit entry: filling the trail with reads buries the writes
that matter. The curated `document_events` and `communication_events` histories
are a business record beside the audit trail and are not a substitute for it.

Consequential changes create immutable audit records:
- actor;
- source/client;
- timestamp;
- entity;
- action;
- before/after or semantic delta;
- reason when required;
- correlation/trace id.

## Secrets
- no secrets in source control;
- .NET User Secrets/environment in development;
- vault/HSM-backed secrets in mature deployment.

M10 introduced the first stored third-party credential: an OAuth refresh token for
a connected mailbox. It is exchanged server-side, encrypted at rest with ASP.NET
Core Data Protection, and never returned to a client — no API contract has a field
that could carry one, and no log line, exception message, audit delta or telemetry
attribute carries one either.

**The current arrangement protects a leaked database and not a compromised
application server.** The key ring is file-backed beside the application, so
anything running as the service can decrypt. That is the "vault/HSM-backed" line
above, still outstanding, and it is stated in
`docs/adr/ADR-0027-oauth-token-protection-and-graph-provisioning.md` rather than
left to be discovered.

No Microsoft application registration, client secret or tenant identifier is in
this repository. Without one an operator provides, Microsoft mailboxes cannot be
connected, and the interface says so rather than failing obscurely.

## Supply chain
Stable builds should include:
- dependency lockfiles;
- SBOM;
- artifact hashes;
- build provenance;
- vulnerability scanning;
- secret scanning;
- code signing;
- MSIX signing.

## AI (M12)

Five grants, deliberately separate. `ai.use` starts and reads your own runs;
`ai.sensitive.use` permits transmitting protected material where policy also
permits it; `ai.propose` permits a model to be offered a canonical write tool at
all; `ai.approve` permits deciding what is put to you; `ai.administer` permits
changing what may be transmitted.

Member holds use, propose and approve. Administrator holds use and administer —
administering the runtime is an operational job, and approving a business act is a
business judgment. Owner holds all five. **Observer holds none**: a model reading
on somebody's behalf reads everything they may read and assembles it into one
place, and handing that to the least-trusted role would make the role's careful
omissions pointless.

### Authorization to read is not permission to transmit

`AiProviderPolicy` answers a different question from every grant above. It is
per-organization and per-provider, closed by default, and `Restricted` has no
reachable ceiling at any level. Provider credentials live in server configuration
and never in PostgreSQL.

### Audited

`ai.run.started`, `ai.run.cancelled`, `ai.approval.granted`, `ai.approval.rejected`,
`ai.proposal.executed`, `ai.provider.policy.changed`.

An executed proposal produces **two** entries: the canonical command's own, naming
the human actor because a human authorized it, and one recording that the proposal
came from a run. A model is never an actor — it holds no permissions and cannot be
held to account.

The approval entries record the tool, its version and the fingerprint. Not the
arguments: what was approved is identified by a hash that can be compared, without
copying whatever the arguments named into a second store with different readers.

### Never in telemetry

The prompt, the model's answer, a fragment of either, a document body, a
communication body, a provider key, or any finance or legal content. Duration,
provider, model, outcome and tool-call count only.

### Run privacy

A run is readable only by the person who started it, and an approval only by the
person it was put to — narrowed in SQL, and answered as missing rather than as
forbidden. There is no permission that widens either.

## Local AI and the workstation (M13)

**Residency is not authority.** A model may run on the user's machine; that
changes nothing about who decides what may be read. A `DeviceLocal` run is
authorized server-side before any disclosure, exactly as a cloud run is
(ADR-0035).

**Restricted is unreachable at every residency**, refused before the provider is
looked up at all. The device-local case is where the exception argument is most
persuasive and it is still refused, because a classification whose meaning depends
on where the arithmetic ran is not a classification.

**Context is disclosed under a lease.** Single-use, ten minutes, bound to the
organization, user, run, subject, residency and a SHA-256 fingerprint over the
rendered context. Capability is probed before the lease is requested, because
issuing the lease is the disclosure. Two audit actions record the boundary
crossings: `ai.lease.issued` when context leaves for a device, and
`ai.local.accepted` when a result is taken back.

**No credential exists on the Windows client**, and no client project can
reference `AgencyOS.Infrastructure`, `AgencyOS.Application`, `AgencyOS.Domain` or
`AgencyOS.Api`. Both are asserted by tests over the project graph and the source
rather than left to review. The device-local path needs no credential: the model
is already on the machine.

**Pointers are not grants.** A citation, a toast and a deep link are each minted
while somebody was authorized and followed later. Resolving a link proves nothing
about the object — a route for an identifier that names nothing resolves exactly
like one that names something real, because telling them apart would itself be a
disclosure. Every drill-down re-authorizes on the server.

**Stored AI results re-authorize on every read**, against the classification
recorded when they were generated. The honest limit: this governs what AgencyOS
will show from now on, and does not reach a copy somebody already read or pasted.

**What leaves the application is narrowed at each exit.** A notification carries no
money amount and needs three separate yeses for any detail. A materialized
document is a copy with a lifetime and never a canonical identity; Restricted
material is never written to disk. A diagnostic summary is an allow-list of twelve
reviewed fields. No crash dump is collected — a dump of this process is a dump of
the agency's material.

