# ADR-0026 — Communications: mailboxes, evidence, and what AgencyOS refuses to infer

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0007 (authorization), ADR-0011 (tenant isolation),
  ADR-0012 (curated history vs audit), ADR-0020 (submissions are recorded, not
  sent), ADR-0025 (linking is context, not access)

## Context

Until M10, AgencyOS recorded that things happened. An Interaction says a person
wrote down that a call took place. A Submission says somebody recorded that
material went out.

Synchronizing a mailbox is different in kind. The message existed independently of
AgencyOS, it says exactly what it says, and it is now sitting in the agency's
database. That is genuinely useful — the negotiation is in the email thread, not
in anybody's notes — and it brings three problems: the content is untrusted, the
mailbox belongs to a person rather than to the agency, and the temptation to read
business meaning out of prose is immediate and wrong.

## Decisions

### 1. A message is not an interaction

`CommunicationMessage` is what a provider reported. `Interaction` (M2) is what a
person recorded. They are not merged and neither is derived from the other.

A synchronized message is **not** editable: `trg_communication_messages_frozen`
refuses updates to its subject, body, participants and dates. AgencyOS did not
write it and has no business rewriting it. What can change is what AgencyOS
decided *about* it: its links, and the identification of its participants.

An operator who wants a note about a call still records an Interaction. Turning
every email into an Interaction would flood the timeline that exists precisely to
hold the things somebody thought worth writing down.

### 2. Three dates, kept apart

Sent, received, and when AgencyOS first saw it. They are three different facts and
the schema keeps all three. Merging them would invent a history nobody observed —
a message back-dated by its sender would appear to have arrived when it did not.

### 3. Synchronization uses the provider's cursor, and is idempotent

Delta cursors, never `received_at > last_sync`. A timestamp comparison misses
back-dated messages, re-reads everything whose flags changed, and has no answer at
all for a message that moved folders.

Every write is keyed on `(account, external_message_id)`, so synchronizing twice
produces the same rows. A provider hands back the same message after a cursor
reset, after a move, and sometimes for no reason at all; without that key one email
becomes six.

An expired cursor is **not an error**. It is the provider saying "start again", and
the only correct response is to forget the cursor and do so — which is safe
precisely because the writes are keyed.

Provider identifiers are external identities stored alongside AgencyOS
identifiers. They never become primary keys: a provider that re-issues one, or a
mailbox reconnected against a different tenant, would otherwise corrupt the rows.

### 4. Provider HTML is sanitized once, on the way in

`MessageSanitizer` reduces provider HTML to a small allow-list of structural
elements and strips **every attribute**. Script, style, frames, forms, objects,
SVG and their content are discarded entirely.

Two removals are worth naming. `img` is not on the list at all: an external image
in a stored message is a beacon that fires when somebody browses a deal's history,
telling the sender the agency is looking at their email today. `a href` is not
either: the text of the link is kept so a reader sees what it said, and the
destination does not become clickable inside a window that holds the agency's
records.

Sanitizing at storage rather than at render means one pass protects every surface
that will ever show the message. The Windows client renders plain text and shows
what was removed; it does not host a WebView in this milestone.

### 5. An address is not an identity

A participant row keeps the raw address and display name **exactly as the message
carried them**, always.

Identification is separate, explicit and made by a person.
`participant-suggestions` reports every match and says whether it was the only
one; when more than one record shares an address — a shared assistant mailbox, a
company inbox — AgencyOS reports the ambiguity and chooses nothing. "Leave
unidentified" is a real answer with its own button.

A wrong identification attributes somebody's correspondence to the wrong person,
quietly, and is hard to find later.

### 6. Attachments are known before they are held

Synchronization records that an attachment exists: its name, type and size.
It does not download it. Pulling every attachment would drag signatures, logos and
cloud placeholders into canonical storage, each with a sensitivity nobody chose.

Ingesting is an explicit act with its own classification, and it is the moment the
bytes become something AgencyOS holds and is answerable for. Until then
`HoldsContent` is false and the interface says so.

### 7. A linked message is evidence, never a state change

Filing a message against a deal records that the correspondence bears on it.

It does **not** advance the deal. An email saying "we accept" is evidence that
somebody wrote that sentence; accepting an offer is a command a person issues,
with its own authorization and its own audit entry. No Opportunity, Submission,
Offer, Contract or Invoice is ever created or transitioned from message text.

Compound "send and record" workflows are deliberately not built in M10. A person
sends, and a person files. Joining them is a later decision that needs the send
protocol to have been in production first.

### 8. A mailbox belongs to a person

`MailboxVisibility` is Private, Shared or Organization, and the default is
Private. The owner is taken from the authenticated caller who completed the OAuth
flow and never from the request.

Reading somebody else's mailbox needs `communications.shared.read`, which is
deliberately *not* granted to members. Sharing a tenant is not an argument for
reading a colleague's correspondence, and generic CRM access is not mailbox
access. Message lists are narrowed to readable mailboxes **inside the query**, so
a count never leaks the size of a mailbox the caller cannot open.

A message in a mailbox the caller may not read answers `404`, not `403` — the
opposite of the choice documents make, and deliberately. For correspondence the
existence *is* the disclosure: it says who is talking to whom. A document refusal
tells somebody which grant to ask for; there is no grant to ask for here short of
reading another person's mail.

Widening visibility exposes every message already synchronized, not only future
ones, and the dialog says so.

### 9. The desk shows what needs a person, and nothing else

The communications command centre reports unknown outcomes, failed sends and
mailboxes needing attention: counts and real rows.

It is not an inbox. Outlook exists and is better at being one, and recreating it
would produce a worse mail client attached to a CRM. Nothing here is ranked,
summarized, prioritized or triaged — M10 records communications and does not
interpret them. Reasoning over this evidence is M11's subject, and it will reason
over the recorded rows through the same authorized query services, never over the
content store directly.

### 10. Reads are not audited; consequential acts are

Opening a message, listing an inbox and polling for changes are not audit events.
Filling the audit trail with reads would bury the writes that matter (ADR-0012).

Connecting and disconnecting a mailbox, changing visibility, ingesting an
attachment, and every outbound state change are audited. The curated
`communication_events` history is separate from the audit trail and is not a
substitute for it.

Telemetry carries counts and state names. It never carries a message body, a
subject, an attachment, a recipient or a token.

### 11. The Graph adapter is written by hand

Six REST calls, over `HttpClient`, rather than the Microsoft Graph SDK. The SDK
models the whole of Microsoft 365 and would be a substantial dependency in
exchange for six operations; M3 reached the same conclusion about a generated API
client.

`ICommunicationProvider` is deliberately not Microsoft-shaped: no Graph
identifiers, no Graph paging, no Graph error model. The fake provider implements
the same protocol, which is what makes CI able to exercise a lost acknowledgement.

**No mailbox in this repository has been connected to a real Microsoft tenant.**
The adapter compiles and satisfies the protocol contract; that is a different
claim from working, and the two are reported separately.

## Consequences

- The negotiation that happened in email is attached to the deal it belongs to.
- A member cannot read a colleague's mailbox, and cannot learn how large it is.
- A stored message cannot run anything and cannot report that it was opened.
- Nothing in the business advances because an email said so.
- Real-provider behaviour remains unproven until somebody exercises it.
