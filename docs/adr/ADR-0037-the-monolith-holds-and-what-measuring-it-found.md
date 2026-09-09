# ADR-0037 — The monolith holds, and what measuring it found

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M14 — Scale, architectural fitness and specialized services
- **Supersedes:** nothing
- **Builds on:** ADR-0024 (blob storage and content addressing), ADR-0029
  (background work leasing, and not Temporal), ADR-0036 (the model-checker pin)

## Context

M14 asked one question: what concrete pressure exists today that the modular
monolith cannot satisfy cleanly? The default answer was to keep it, and extraction
required evidence.

Seventeen module families were assessed. **None passes the admission test**, and
every one fails at the same item — a measured problem or a hard runtime boundary.
Not because the modules are perfect, but because nothing has been measured and the
only candidate with a genuine runtime argument depends on a feature that does not
exist.

That could have been an unsatisfying result. It was not, because getting to it
turned up three findings and two real defects.

## What the review found

### The mechanism this milestone would have built is already here

`CommunicationWorker` claims rows with `FOR UPDATE SKIP LOCKED` under an expiring
two-minute lease, identifies itself per process, releases leases explicitly, and is
documented as safe to run concurrently. A broker, a workflow engine, or a new job
framework would replace a working mechanism with an unproven one.

### There is no publish step, so an outbox has nothing to make atomic

No outbox exists and no in-process domain-event dispatcher exists. AgencyOS uses
direct command handlers and a sequence-ordered change feed that clients poll. The
`SaveChanges(); Publish();` hazard cannot occur here, so adding an outbox now would
be adding a solution to a problem the architecture does not have.

### The scaling bug M14 expected to close does not exist

Every read path is bounded and clamped — ten query services at 200, search at 50
with a 256-character query cap, sync at 500, AI at 100. There are 279 indexes and
20 generated `tsvector` columns. There was no `limit=1000000` to close.

### The one candidate, and why it fails

Document text extraction has a real runtime argument: dependency-heavy libraries,
untrusted input, memory spikes, parsers that hang. The seam already exists
(`IDocumentTextExtractor`, a `TextExtractionState` with an honest `Unsupported`
value, a plain-text implementation capped at 4 MB) and it runs **inline on the
ingestion request path**, which is right for bounded text and would be wrong for
PDF or OCR.

But no PDF, DOCX or OCR extractor exists. M10 deferred them and nothing has asked
for them since. Building one inside M14 so that it could then be extracted would be
manufacturing the evidence, and the sentence the milestone requires —
*"we are adding X because measured condition Y…"* — has no Y.

Recorded as a threshold: if that feature is ever wanted, extraction moves off the
request path first, and a worker process rather than a service is the likely answer.

## Decisions

### 1. Everything stays in the monolith

Including Finance, Deals, Legal and Authorization, whose clean boundaries are not
evidence for separate processes. Cross-domain workflows benefit from one
transaction, relational integrity and composite tenant keys, and none of that is
worth trading for topology.

**Fifteen technologies were considered and none adopted**: domain microservices,
Kafka, RabbitMQ, Redis, OpenSearch, Neo4j, a vector database, Temporal,
Kubernetes, a service mesh, multi-region, Rust, Python, C++ and gRPC.

### 2. Measure round trips, not wall-clock

Hosted CI cannot assert a time: a shared runner's numbers move for reasons
unrelated to the code, and a flaky performance gate teaches people to ignore the
gate.

What it can assert is an algorithmic invariant with no baseline and no magic
number: **the count of database round trips a read makes must not depend on how
many rows come back.** That is what an N+1 is, and it hides in development where
every tenant has four people. Each test compares two measurements of the same
endpoint, so it cannot drift and never needs re-baselining.

The counter carries an `EverObserved` flag that one test asserts alone: an
instrument that was never wired in would report zero on both sides of every
comparison and pass everything.

Measured result: **no N+1 in the read paths under test.**

### 3. The upload ceiling is declared, and refused explicitly

It was inherited. Kestrel stops a body at roughly 28.6 MiB, multipart buffering at
128 MiB, and the resulting exception was not mapped — so AgencyOS had a maximum
document size nobody had chosen, that appeared in no document, that two framework
defaults disagreed about, and that surfaced as an unhandled failure.

Now 256 MB, configurable, and enforced by an explicit middleware check before any
body is read. The framework limits stay underneath as a backstop for a chunked body
that declares no length, with the multipart limit set clear of the ceiling so it
cannot trip first.

**Why a middleware rather than the framework's limit.** `TestServer` does not
honour Kestrel's `MaxRequestBodySize`, and setting the multipart limit to the same
number made the form reader trip first and report an oversized upload as a
*malformed* one — the wrong status and the wrong explanation. An explicit check
answers identically under both servers, and the refusal names the number, because
"request entity too large" tells somebody their contract did not file without
telling them what would.

### 4. The blob abstraction is proved, not asserted

`IBlobStore` was written to be implementable by object storage — streaming,
content-addressed, organization-scoped, no folders or listing or signed URLs. That
claim was sound and untested: one implementation existed, so nothing separated the
interface's rules from that implementation's behaviour.

`BlobStoreConformance` is the rules, run against the shipped store and a second one
that shares no code with it.

**Writing the second implementation immediately found a defect in the first.**
`ExistsAsync` is documented to return a bool — only `OpenReadAsync` documents the
exception — but both `ExistsAsync` and `DeleteAsync` resolved keys through a method
that throws. Asking whether another organization's key existed raised instead of
answering false, and an orphan sweep meeting one unresolvable row would have
stopped before the rest. Both now answer, which is also the better posture: a
foreign key is no longer distinguishable from an absent one.

**What this does not prove**, and the suite says so: an in-memory store is not a
network. It shows the interface is not filesystem-shaped. It says nothing about
latency, partial failure, retries or multipart upload.

### 5. The layering is asserted

"Keep the modular monolith" is worth something only while the modularity is real,
and a layering rule that lives in a document degrades one convenient reference at a
time. The project graph, the domain's freedom from persistence technology, and the
page-size clamp are now tests. The clamp test counts what it examined and fails if
the scan stops finding services, because a structural test that quietly matches
nothing is the failure mode these are most prone to.

## Known limitations, recorded rather than solved

**Two multi-instance blockers.** The Data Protection key ring lives on the server's
own disk — already documented in-code as an ALPHA limitation — so two instances
could not decrypt each other's stored mailbox credentials. And the blob root
defaults to `AppContext.BaseDirectory`, so two instances would silently split the
store unless configured otherwise. Both are filesystem identity, neither is an
argument for extraction, and neither is fixed here.

**No wall-clock evidence exists.** Not for reads, not for writes, not for search.
The harness measures round trips. Latency and throughput remain unmeasured, and
this ADR claims nothing about them.

**Measurement is impossible on the development host.** Docker Desktop's Linux
engine returns HTTP 500, so every figure in this milestone came from CI.

## Consequences

**What this buys.** A milestone that added no runtime dependency, no process, no
broker and no language — and that nonetheless fixed two real defects, declared a
limit that was previously an accident, and left behind instruments that will catch
the regressions it was looking for.

**What it costs.** The unmeasured questions stay unmeasured. AgencyOS still cannot
say what a page costs in milliseconds, and the first genuine scaling pressure will
arrive as a surprise rather than as a trend.

**What is deliberately not here.** Every one of the fifteen technologies above,
plus PDF/DOCX/OCR extraction, malware scanning, full-content search, an object
store adapter, rate limiting beyond what a single host needs, and Kubernetes.

**What the next milestone inherits.** A harness that can be pointed at any read, a
conformance suite a second blob backend can be written against, a layering rule
that fails the build, and a fitness matrix whose verdicts say what evidence would
change them.
