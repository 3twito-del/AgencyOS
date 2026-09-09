# ADR-0038 — When a refusal may admit that something exists

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M14 — Scale, architectural fitness and specialized services
- **Supersedes:** nothing
- **Builds on:** ADR-0011 (composite tenant keys), ADR-0025 (document
  classification and linking), ADR-0031 (the AI runtime), ADR-0033 (activation and
  the untrusted client)

## Context

M13 found that following a citation to an M11 signal returned **403** where the AI
routes would have returned **404**, recorded it as an inconsistency, and
deliberately did not repair it under an M13 heading.

M14's review found that description wrong in both directions.

**It is wider.** The pattern — load the record, then authorize against that
record's own classification — is not an Intelligence peculiarity. M10 Documents
and M10 Communications do exactly the same thing. Status follows from which
exception is thrown (`PermissionDeniedException` → 403,
`EntityNotFoundException` → 404), so behaviour follows the order of operations,
and three families order it the same way.

**It is more deliberate.** The M10 pattern carries a written rationale:

> "Refuses rather than redacts. A document list with the privileged rows silently
> removed reads as a complete list, and somebody would conclude the contract was
> never filed (ADR-0025)."

That is a considered position, not an oversight. And the cross-tenant case was
already both correct and regression-tested, with the intent stated in the test's
own name: *a person in another tenant is not found, not forbidden-then-leaked*.

So AgencyOS already had a coherent rule. What it did not have was the rule written
down, which is precisely why the difference between two surfaces looked like an
accident rather than a distinction.

## Decision

Four cases, decided by **what the refusal itself would reveal, and to whom**.

### 1. A cross-tenant reference is **404**

Another organization's identifier is indistinguishable from one that names
nothing. The caller has no standing to learn even that the record is real, and a
403 would confirm existence to a stranger holding a GUID.

This is absolute. No permission, role or grant inside the caller's own tenant
changes it.

### 2. Missing the base grant for a surface is **403**

A caller without `documents.read` is told they may not use the document surface.
The refusal is about the caller, not about any record, so it discloses nothing —
and a 404 here would be a lie that sends somebody looking for a document that is
sitting exactly where they left it.

### 3. A classified record inside the caller's own tenant is **403**

A colleague who may read documents but not privileged ones is told the document is
beyond their clearance, not that it does not exist. Documents, Communications and
Intelligence all behave this way.

The reasoning is ADR-0025's, and it is about the record being trustworthy rather
than about hiding: these are shared business records whose existence a colleague
can usually infer from the work itself. A system that answers "no such contract"
to somebody who watched it get signed has not protected the contract — it has
taught them the record is unreliable, which is the more expensive loss.

### 4. Another person's AI run or approval is **404**

A run is not a shared business record. It is one person's question, and the fact
that a colleague asked something is itself private — there is no equivalent of
"you can infer it from the work", because inferring it is the disclosure.

An approval identifier is also a bare GUID that travels in email, so the route
must answer a colleague and a stranger following a link identically.

### The rule in one line

> **Hide existence wherever the asker has no standing to know the record exists.
> Refuse openly wherever they do.**

Cases 1 and 4 hide. Cases 2 and 3 refuse. The asymmetry between a document and an
AI run is not an inconsistency: a document is the agency's record and a run is a
person's question.

## What this ADR does not change

**No behaviour changes.** Every case above already behaves this way. §49 says not
to change existence semantics casually, the one arguable case was decided
deliberately in ADR-0025, and there is no evidence that changing it would help
anybody.

`ExistenceDisclosureTests` pins all four cases, so a future change to any one
surface has to be a decision about the rule rather than a local edit that nobody
notices is a policy change.

## Why this is better than the alternatives

**Answering 404 everywhere.** Maximum anti-enumeration, and it makes the document
surface lie to colleagues about records they know exist. It would also make every
permission problem look like a missing record, which is the least diagnosable
failure a support conversation can start from.

**Answering 403 everywhere.** Honest and it turns every identifier into an oracle.
An approval id from an email would confirm that an approval exists; a GUID from
another tenant would confirm the tenant holds that record.

**Deciding per endpoint as it is written.** What happened. It produces a coherent
result for a while and then produces M13's finding.

## Consequences

**What this buys.** One rule that explains every existing refusal, four regression
tests holding it, and a stated basis for the next surface rather than a re-derived
one.

**What it costs.** Two surfaces answer differently for what looks superficially
like the same event, and somebody reading only the HTTP responses will find that
surprising until they read this. That is a documentation cost, accepted in
exchange for each surface answering the question it is actually being asked.

**The residual disclosure, stated plainly.** Case 3 tells a colleague inside the
tenant that a record exists above their clearance. That is deliberate and it is
still a disclosure: somebody can learn that counsel filed *something* on a matter.
The mitigation is that they are already inside the tenant and already know the
matter exists. If a future classification ever needs to hide its own existence
from colleagues, it needs case 1's treatment and a note here saying so.

**What the next milestone inherits.** A rule to apply rather than a judgement to
re-make, and tests that fail if a new surface picks the other answer by accident.
