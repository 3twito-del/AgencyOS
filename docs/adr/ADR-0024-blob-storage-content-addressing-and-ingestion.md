# ADR-0024 — The content store: blobs, hashes and crash-consistent ingestion

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0011 (tenant isolation), ADR-0014 (concurrency and idempotency),
  ADR-0022 (contract versions are immutable), ADR-0023 (money, and the invoice
  that holds no document)

## Context

Every milestone before this one stored facts. M10 is the first that stores
*files*, and a file is a different kind of thing: it is large, it is opaque, it
arrives from outside, and it is the thing somebody will put in front of a lawyer.

Two failures decide the design. The first is a database row that points at bytes
which are not there — a document that opens as an error during an argument. The
second is bytes that are there and cannot be trusted: a filename that escapes the
storage root, a media type taken from an extension, a file described as safe that
nothing examined.

## Decisions

### 1. Bytes live behind `IBlobStore`, never in an entity column

The store has six operations: put, open for reading, exists, delete-only-when-
unreferenced, and the two identity operations. It is the only thing in AgencyOS
that touches file content.

Large binary content is **never** a column on an ordinary EF entity. A
two-hundred-megabyte deck in a `bytea` is a row that cannot be selected without
materializing the whole file, a backup that grows without bound, and a
`SELECT *` somebody writes later that brings the server down.

`FileSystemBlobStore` is the ALPHA implementation and it is **a local filesystem,
not distributed object storage**. It is correct for a single-server deployment and
wrong for two. Nothing in the seam pretends otherwise: there is no replication, no
consistency story across hosts, and no bucket. Introducing object storage is a
later decision with its own ADR, and the seam exists so that decision does not
have to touch the domain.

### 2. Identity is the SHA-256 of the actual bytes

Every `BlobObject` carries the digest, the byte length, the media type, the
storage key and when AgencyOS was handed it. The digest is computed over the bytes
as they are written, not over anything a caller declared.

The storage key is derived from the digest and the organization:
`{organization}/{aa}/{bb}/{sha256}`. A caller's filename never becomes a path.

### 3. Deduplication is per organization, and is a storage fact only

Identical bytes within one tenant are stored once. Identical bytes across tenants
are stored twice, deliberately.

A store shared across tenants would answer "do you already hold this file?" for
anybody who could guess the content — a covert channel that leaks whether another
agency holds a particular script, offer or contract. The disk it would save is
worth less than the question it would answer.

**Deduplication never merges documents.** Two documents holding the same bytes are
two documents, with their own titles, links, histories and — critically — their
own sensitivities. A privileged copy of an ordinary memo is privileged. The
response says `deduplicated: true` because an operator who uploaded two files and
sees one stored object should know the system recognized the bytes rather than
lost a file.

### 4. A document is a name; a version is the thing

`Document` holds mutable metadata: title, kind, sensitivity, reference, status.
`DocumentVersion` holds a digest, a length, a media type and a filename, and **is
never edited**. Uploading again produces version N+1.

`Document.CurrentVersion` is derived from the versions, never stored, for the
reason M8 gave about contract versions and M9 gave about balances: a stored
pointer to "the current one" is a second truth that drifts.

The database enforces it. `trg_document_versions_immutable` and
`trg_blob_objects_immutable` refuse an `UPDATE` to a hash, a length or a storage
key on a row that already exists. Application code is not the only thing standing
between a version and a silent rewrite.

This immutability is its own thing, and is not the same as the audit trail's
(ADR-0012), an Offer's (ADR-0021), a ContractTerm's (ADR-0022) or a journal
entry's (ADR-0023). They coincide in shape and differ in reason: this one exists
because somebody may need to read what was actually signed.

### 5. `RecordedAt` is when AgencyOS was told, and says so

A version's date is the moment the bytes arrived here. It is **not** the authoring
date of the document, which AgencyOS does not know and does not ask for. A
contract dated March that arrives in July is recorded in July, and every surface
that shows the date labels it "recorded" for that reason.

### 6. Kind and media type are different questions

`DocumentKind` is business meaning: Contract, Invoice, Headshot, Reel. `MediaType`
is technical format. A PDF can be any of the three, and the two facts answer
different questions — "find the executed agreements" and "can this be previewed".
Neither is inferred from the other, and neither is inferred from the filename.

### 7. Ingestion is a ledger, then bytes, then rows

The ordering is the whole design, because a filesystem and PostgreSQL do not share
a transaction and nothing here pretends they do.

1. A `blob_ingestions` row is written and committed: *staging*.
2. The bytes are written to a temporary file, flushed to disk, and atomically
   renamed into place. The ledger row moves to *stored*.
3. The document rows are written and committed. The ledger row moves to
   *finalized*.

A crash between any two steps leaves bytes nobody references — an orphan, swept
later by a job that reads the ledger and deletes only content that no blob record
in the organization claims. It never leaves the reverse: **a database record
committed to bytes that were never durably written.** That failure produces a
document which opens as an error, and it is the one the ordering exists to make
impossible.

The sweeper honours a grace period so a request still in flight is never swept out
from under itself, and it consults the blob records rather than the ledger alone,
because deduplication means a later ingestion may legitimately have adopted
exactly those bytes.

### 8. Everything arriving from outside is hostile

- **Size:** 200 MB for an upload, 50 MB for a message attachment. Refused before
  anything is stored, and refused for an empty file too: a zero-byte upload is
  almost always a failed transfer, and recording one would claim AgencyOS holds a
  contract when it holds nothing.
- **Filenames:** reduced to a leaf, stripped of control characters and of the
  characters that break a `Content-Disposition` header. A name that reduces to
  nothing becomes `untitled`. The name is never a path component, because storage
  keys come from the digest.
- **Media types:** a declared type is accepted only if it is well-formed and free
  of control characters; otherwise the file is an opaque stream. An extension is
  never trusted.
- **Serving:** `X-Content-Type-Options: nosniff` and a locked-down
  `Content-Security-Policy` on every download. Inline display is a short
  allow-list — PDF, common images, plain text — and everything else is an
  attachment.
- **Not done:** nothing is executed, no archive is expanded, no macro is run, and
  no format is converted.

### 9. AgencyOS implements no malware scanning, and says so

`BlobScanState` exists with four values and every version is `Unscanned`. The
seam is there so a real scanner can fill it in; until one does, **nothing is ever
labelled `Clean`**. The Windows surface says "Not scanned" and warns beside it.

Writing a scanner is not a thing to attempt as part of a document store, and
calling a file safe because it was stored successfully would be the single most
dangerous sentence in the milestone.

### 10. Extracted text is a projection, never the content

Plain-text formats are extracted natively. PDF and DOCX report `Unsupported` and
say so on the screen: they are formats nothing tried to read, which is different
from a failure.

No OCR. No Python. No model. `docs/11_TESTING_AND_FORMAL_METHODS.md` makes
document parsers a mandatory fuzzing target, and adding two before the fuzzing
exists would be the wrong order.

The extracted text is a reading aid displayed *beside* the file. The stored file
is the document, and the surface says so.

### 11. Archiving destroys nothing

`Archive` takes a document out of ordinary lists and requires a reason. Every
version, every byte and every link stays exactly where it was, and the document
can be restored. There is no delete in M10, and no purge tooling: the only
deletion anywhere is the sweeper removing bytes that no record claims.

## Consequences

- The agency can hold its files, and can prove which draft was signed.
- A single-server deployment is assumed. Two application servers sharing a
  database would not share a content store, and that is a known limit stated here
  rather than discovered later.
- An orphaned blob after a crash is expected and swept. An unreadable document is
  not, and cannot be produced by the ordering.
- A file the agency stores is not a file anybody has vetted.
