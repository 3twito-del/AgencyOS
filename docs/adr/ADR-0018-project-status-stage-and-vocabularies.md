# ADR-0018: A project's status and its stage are different facts, and type vocabularies stay enums

Status: Accepted
Date: 2026-09-07

## Context

M5 introduces the canonical project. Two modelling questions had to be settled
before anything else could be written, because both are expensive to change later
and both have a tempting wrong answer.

## Decision

### ProjectStatus and DevelopmentStage are separate, and interact by one rule

`ProjectStatus` (Active ↔ Inactive → Completed | Cancelled → Archived) says whether
anybody is working the record and how it finished. `DevelopmentStage` (Concept …
Released) says where the work itself has got to.

Merging them into one enum is the obvious economy and it destroys the most useful
thing the record says. "Cancelled" and "in pre-production" become mutually
exclusive, when the fact an agency actually needs months later is that a project
was cancelled *during* pre-production - how far it got before it died.

**Stage moves in both directions.** Projects genuinely fall out of pre-production
back into development when financing or a cast attachment collapses. A forward-only
ladder would not prevent that happening; it would only teach people to lie to the
system, or to abandon the record and open a new one, which loses the history the
model exists to keep.

The rule that does bite is the interaction: **stage is frozen once status is
terminal.** Advancing the creative stage of a cancelled project is meaningless, and
freezing it preserves where the work stopped. The whole 5 × 7 × 7 matrix is
enumerated by test.

`Cancelled` returns to `Active`; `Completed` does not. Revival is an ordinary
industry event. A finished film does not un-finish - what follows it is a different
project, and recording it as the same one would erase that.

### Status and stage change by separate commands

Not one `PATCH` taking arbitrary fields. A single endpoint that set both would let
somebody cancel a project and advance its stage in one action under one reason,
and nothing downstream could recover which decision the reason belonged to.

### ProjectType stays an int-backed enum

The argument for a catalog table is that new entertainment mediums would otherwise
force destructive migrations. **For an int-backed enum they do not.** The column is
already `integer`; adding a member is a code change with no DDL at all. That is how
`CompanyType`, `CreditType`, `MaterialType` and `RepresentationScopeArea` have
worked since M2, and no migration has ever been needed to extend one.

A catalog would buy per-tenant, edit-at-runtime vocabularies that nothing has asked
for, and cost a join on every read, the loss of compile-time exhaustiveness over
`switch`, and saved views that break when somebody deletes a row the view names.

The same reasoning covers `SourcePropertyType`, `ProjectRoleType`,
`ProjectCompanyCapacity`, `AttachmentStatus`, `PackageStatus` and
`PackageElementKind`. Every one keeps an `Other = 99`.

**The condition that would flip this:** vocabularies that must change without a
release, or that differ per tenant. Neither exists, and speculating on either is
what `CLAUDE.md` section 5 forbids.

## Consequences

- The question "how far did this get before it stopped" is answerable for every
  project that stopped.
- Stage regression is a first-class recorded event with a reason, not a
  workaround.
- Extending a vocabulary is a one-line code change and a test, with no migration.
- A tenant cannot invent its own project type. If that is ever needed, this
  decision is reopened rather than worked around with `Other` plus free text.

## Evidence that would cause reconsideration

- A tenant that genuinely needs its own vocabulary, or a partner integration that
  supplies types AgencyOS does not control.
- A stage model where regression turns out to need its own audit distinct from a
  forward move - which would suggest stage transitions want a table like status
  transitions rather than free movement.
