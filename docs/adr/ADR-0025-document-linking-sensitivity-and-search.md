# ADR-0025 — Documents: linking, sensitivity, and what a search may see

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0007 (authorization), ADR-0011 (tenant isolation),
  ADR-0022 (privilege is asserted, never inferred), ADR-0024 (the content store)

## Context

A document is only useful when it is attached to something: this contract, that
client, this invoice. Attaching it raises two questions that look small and are
not.

The first is structural. Fourteen kinds of record can hold documents, and the
obvious shortcut — a `(entity_type, entity_id)` pair — has no referential
integrity at all. It permits a link to a deleted record, to a record in another
tenant, and to a typo.

The second is about disclosure. If access flowed along a link, a member who can
see a deal would be able to read counsel's advice about it. If a search matched
inside file contents, the *count* of results would report the contents of
documents the searcher may not open.

## Decisions

### 1. Links are typed, with real foreign keys

`DocumentLink` carries one `DocumentLinkTarget` discriminator and exactly one of
fourteen nullable typed identifier columns, with a `CHECK` constraint requiring
precisely one to be set. Each column carries a composite foreign key
`(organization_id, target_id)` to the target's alternate key, so PostgreSQL
enforces both existence and tenancy.

The precedent is `ContractTaskLink` (M8) and `FinanceTaskLink` (M9). The pattern
is wider here — fourteen arms — and is still worth it: an untyped pair has no
integrity, and fourteen link tables would be fourteen tables saying one thing.

`LinkArcSynchronizer` fills the typed column from the discriminator centrally on
save, so no call site can set one and forget the other.

`CommunicationLink` uses the same shape for messages, deliberately: filing
correspondence and filing a document are the same question asked twice.

### 2. A link is context, not authorization

Being able to read the deal a document is filed against grants **nothing** about
the document. Its own sensitivity decides who may open it, on its own.

This is stated on the link dialog, because a link is exactly where a person's
intuition says permission should flow, and it does not.

### 3. Sensitivity is stated by a person and never inferred

`DocumentSensitivity` is Internal, Confidential, Privileged, Financial or
Restricted, and it is a required field with no default anywhere in the system.

It is not read from the filename, the folder, the document kind or what the
document is linked to. Privilege is a legal conclusion a lawyer draws, and a
system that guessed it would be wrong in both directions — labelling ordinary
correspondence privileged, and leaving genuine advice unmarked. This is the same
decision M8 made about contract clauses (ADR-0022), applied to files.

Privileged and Restricted require their own grants to read. Financial defers to
`finance.read`, so the classification does not become a second, weaker way to see
the books.

### 4. A writer may not file into a classification they could not read

`AuthorizeClassifyAsync` requires the elevated grant as well as
`documents.write`. Otherwise anybody could hide a document from themselves, and —
worse — could move somebody else's document out of their reach.

This has a consequence for the role map. A member writes documents and cannot read
privileged ones; an administrator reads privileged ones and writes nothing. Under
the rule above, neither could ever record counsel's advice, so **the owner holds
`documents.write` and `documents.link` in addition to the elevated read grants**.
Without that, the classification would exist and be unreachable.

### 5. A list is filtered in the query; a single document is refused

`ReadableSensitivitiesAsync` narrows a document list **inside the SQL**, before
anything is counted, ranked or paged. Filtering afterwards would leak the count,
and a count of privileged documents about a named person is itself a disclosure.

Fetching one document by identifier answers `403` rather than `404`. The two
choices are different disclosures and this one is deliberate: a person who
followed a reference to a document they may not open should learn that a grant
exists to ask for, rather than concluding the contract was never filed. The
document's existence is already implied by the reference they followed.

Communications make the opposite choice, for a reason set out in ADR-0026.

### 6. Search covers metadata only, and the screen says so

M10 searches titles and references. It does not search inside files.

Full-content search over a corpus containing privileged contracts requires the
authorization to be airtight in the index, in the snippets, in the ranking and in
the counts. Getting that wrong leaks the text of a contract to somebody who may
not read it, and a partly-correct implementation reads exactly like a correct one.
Correct security is worth more than feature breadth, so **full-content search is
deferred, explicitly, and the interface tells the user what the box does** rather
than letting them conclude the corpus is empty.

No OpenSearch. No vector database. The metadata search is PostgreSQL full-text,
as M3 established.

Saved views can name documents as a target, and their filters cover kind, status,
sensitivity, link shape and dates — never document text. A saved view is a query
somebody else may run, and a predicate matching inside a privileged contract would
report its contents to whoever ran it. Views run through the same authorization-
aware query service as the list, so a view can never widen what its owner may see.

### 7. Bytes leave through one authorized route

There is one download endpoint. There is no public URL, no signed link, no
pre-signed expiry and no path in any response — checked by a test that reads the
raw JSON rather than the typed contract, because the guarantee is about what
leaves the server.

A public permanent URL is not introduced in ALPHA. It is the kind of convenience
that turns a permission system into a decoration, and the milestone that stores
privileged material is the wrong one to add it in.

## Consequences

- A document can be filed against anything the business knows, and the database
  guarantees the target exists in the same tenant.
- Somebody with deal access still cannot read counsel's note about the deal.
- A phrase inside a stored contract is not findable in M10, and the interface says
  so rather than implying the document is missing.
- The owner role gained two write grants. That is a widening, recorded here rather
  than made quietly.
