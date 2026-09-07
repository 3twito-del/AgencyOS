# ADR-0019: Attachment records what is true, packages hold what is wanted, and companies are not roles

Status: Accepted
Date: 2026-09-07

## Context

M5's central risk is not schema. It is that four superficially similar
relationships get collapsed into one table, because each pair looks alike from a
distance:

- somebody attached to a project, and somebody the agency wants;
- a person in a creative role, and a company financing the project;
- a package element, and an attachment;
- a credit, and a project.

Every collapse is cheaper to write and each destroys a distinction the agency
actually relies on.

## Decision

### Attachment records claims about the world. It has no `Targeted` state

`AttachmentStatus` is `InDiscussion → Attached ↔ Conditional → Ended | Withdrawn`.
Every value is something reported about the project.

There is deliberately no `Targeted`. "We want Ada for this" is the agency's
intention, not a fact about the project, and recording it as an attachment would
make a project's roster a mixture of fact and hope that nothing downstream could
separate. Somebody would eventually read the roster to a buyer.

Wanting somebody lives as a **proposed package element**, and becomes an
opportunity in M6. The milestone brief invited this call explicitly and it is the
right one: semantic purity here is what lets M6 build a pipeline rather than
untangle one.

`InDiscussion` is included because it is reported fact - and it deliberately does
**not** occupy a role, because several directors can genuinely be in talks for one
job at once.

### Occupancy is derived, never asserted

A role's `Filled` status is computed from the attachments that actually hold it,
recomputed whenever an attachment is created or moved. Nothing sets it by hand.

M5 shipped a bug here first: attaching somebody `InDiscussion` marked the role
filled, because the create path asserted occupancy instead of deriving it. A role
marked filled with nobody in it makes every "missing a director" view wrong, which
is the one question this part of the model exists to answer.

### One holder of an exclusive role, enforced in the database

`ProjectRole.IsExclusive` is **opt-in**. Claiming exclusivity wrongly blocks
legitimate data entry; omitting it wrongly only fails to catch a duplicate. A
convenience constraint should fail open.

Where it is claimed, a partial unique index enforces it:

```sql
CREATE UNIQUE INDEX ux_attachments_one_holder_per_exclusive_role
ON attachments (project_role_id)
WHERE status IN (2, 3) AND role_is_exclusive;
```

`role_is_exclusive` is copied onto the attachment row - **the only denormalization
in M5**. PostgreSQL forbids a subquery in an index predicate, so the index cannot
consult `project_roles`; without the column the invariant could only be enforced
by a trigger that locks the role row, or not at all. The project aggregate keeps
the copy in step and refuses to declare a role exclusive while several parties
already hold it. An integration test races eight clients at one showrunner job and
asserts exactly one wins.

### Company participation is a distinct model, and here is why

Generic `Attachment` cannot carry it without loss.

A studio is not a position anybody fills. There is no role that could be open,
targeted or filled, so expressing it as an attachment means inventing a fake
`ProjectRole` on every project purely to hang the studio off. Worse: one company
can be the **producer** - a real creative role, through an attachment - *and* the
**studio** on the same project. A single table would have to collapse those into
one nullable-role shape, which is precisely the overloading the milestone forbids.

So `ProjectCompanyParticipation` carries Studio, Network, Streamer,
ProductionCompany, Financier, Distributor, SalesCompany - effective-dated, one open
involvement per (project, company, capacity). It records **known participation**,
not a sales process; buyers and pitches are M6.

### PackageElement is the agency's working list, Attachment is the project's roster

A package is allowed to contain hopes. That is the entire reason it exists as a
separate concept.

`PackageElement` may name an attached director, a star nobody has approached, and
a role nobody has been cast in - all at once, each with an explicit kind. What it
must never do is make the second look like the first, so the API returns
`IsAttached` per element and the Windows surface renders attached and proposed in
**separate columns** rather than one list with a badge.

`IsAttached` is computed from whether the referenced attachment *currently holds*
its role, not from the element's kind. An element pointing at an attachment that
has since ended stops being a claim.

Element targets are raw identifiers interpreted by kind, which is compact and easy
to abuse: without validation a package could reference another tenant's person by
pasting in a GUID, and it would render, because rendering only needs the id. Every
kind is resolved against the tenant before it is stored. A foreign key per kind
would enforce this structurally at the cost of six mostly-null columns and a check
constraint nobody could read; the trade is deliberate and tested.

### Package lifecycle is about internal readiness only

Draft → Assembling ↔ Ready → Active ↔ Paused → Closed | Abandoned. Assembly is not
one-way: a package found to be missing something returns from Ready to Assembling.

There is no submitted, pitched or negotiating state. The moment a package goes to a
buyer, that is an opportunity - M6. Putting a submission state here would mean M6
either duplicating it or inheriting a half-built pipeline.

### Package strategy gets its own permission

A project's logline is a shared fact about a piece of work. A package's strategy is
what the agency privately thinks its play is, and routinely names who it expects to
pass. That is a materially different sensitivity, so `packages.strategy.read` is
separate from `talent.notes.read`: the populations differ, and a coordinator who
legitimately reads client positioning has no particular reason to read the agency's
assessment of a buyer.

Redaction goes through the shared `SensitiveNotes` from M4, so saved views and
search apply the identical rule. **Strategy is also excluded from the package
search vector** - otherwise a caller could confirm what a note says by searching a
phrase and watching the package surface, which would defeat the redaction entirely.

### Source properties describe, and assert nothing legal

Named `SourceProperty`, never rights or ownership. It records what a project comes
from and says nothing about who owns it, whether it is available, whether an option
exists, or what territories apply. Chain of title and options are M8, and a model
that implied AgencyOS had verified any of it would be worse than no model at all,
because somebody would rely on it.

`AttributedCreator` is free text with an optional `CreatorPersonId`: the author of
a novel is usually not somebody the agency represents, and manufacturing a `Person`
row for every credited author would fill the directory with people nobody has a
relationship with.

Linked to projects **many-to-many** - one book spawns a film and a series; one
project draws on a book and an article.

### The M4 credit seam closes with a foreign key, not an inference

`Credit.ProjectId` becomes a real composite, tenant-qualified foreign key. It stays
nullable: most historical credits describe work the agency had nothing to do with
and will never have a project record.

**Linking is always explicit.** A credit is never matched to a project because the
titles look alike; that would manufacture canonical relationships out of a guess,
and nothing downstream could tell which relationships were asserted and which were
assumed. Unlinking leaves the recorded title alone, because what a credit said is a
fact in its own right.

### Materials link to projects through a join, not a column

An optional `ProjectId` on `Material` would force a 1:1 - a lookbook reused across a
slate could belong to one project only. Worse, it would express a project fact by
mutating the talent's own record, so linking a screenplay to a project would bump
the version of something owned by the writer.

## Consequences

- A project's roster contains only claims about the world, so it can be read out
  loud without qualification.
- Removing somebody from a package cannot accidentally end their attachment, and
  ending an attachment cannot silently empty a package - the two are separate
  records with separate meanings.
- M6 has a clean place to put opportunities, with nothing to unpick.
- A package element's target is a raw identifier, so its integrity rests on one
  validator rather than on foreign keys. That validator is the thing to watch.
- `role_is_exclusive` is denormalized and could in principle drift. It is written
  only by the aggregate that owns both sides, and a test covers the propagation.

## Evidence that would cause reconsideration

- A concrete workflow where "targeted" must appear on the project's roster rather
  than in a package, which would mean the fact/intention line is drawn wrong.
- Package elements needing referential integrity strong enough to justify the
  six-column shape after all - most likely if something outside the package starts
  depending on them.
- A company capacity that turns out to be a role somebody genuinely fills, which
  would blur the participation/attachment split deliberately drawn here.
