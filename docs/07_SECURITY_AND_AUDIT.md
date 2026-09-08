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
