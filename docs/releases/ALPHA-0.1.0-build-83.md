# AgencyOS ALPHA 0.1.0, build 83

The semantic attribution repair: a name a row shows now says which role it is
playing, in what is seen and in what is announced — and the two channels can no
longer name different people.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **83** |
| Executable commit | **`7397378af3771c01fb289bb5bea77472dd2a9fce`** |
| Tag | **`alpha-7397378`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 82, `alpha-d6bd5b8` |

**No contract change and no migration.** `src/AgencyOS.Contracts/` was not touched,
and neither were the API, domain, application or infrastructure projects. Every
fact this repair needed was already published; the defect was entirely in how the
client rendered and announced what it was already given.

## 2. The executable commit is not the branch tip

```
git diff --stat 7397378af3771c01fb289bb5bea77472dd2a9fce..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Two findings, both instances of the rule adopted in CLAUDE.md §1 principle 12:
*where more than one domain role could apply to an identity a surface shows, which
role it plays must be explicit — in what is seen and in what is announced.*

### The target contact, which the two channels disagreed about

A pipeline target carries three identities: the party approached, the individual
dealt with at that company, and the internal member responsible. The row printed
the second as a bare name. `RowLabel`, which had no entry for
`ContactDisplayName` and did have one for `OwnerDisplayName`, announced the
**third** in the same position.

So for one row the product said this to a sighted operator:

```
Saltmarsh Verity Pictures
Perpetua Danforth-Achebe
```

and this to a screen-reader operator:

```
Saltmarsh Verity Pictures, Review Owner, Interested
```

Two different people, in the same slot, neither with a role. No blind-handoff
operator could have reported it: each of them receives one channel.

Both now read `TargetLine`, which answers with the role attached — `Contact:` for
the counterparty individual, `Owner:` for the internal member, taken from the
dialogs that set them. A person target has no contact, and none is invented.

The same value appears in two other places, and both took the same wording: the
deal dialog's target picker (`EntityChoice`) and the record-pitch dialog.

### The task assignee, which was left bare beside a labelled subject

Build 82 labelled a task's subject `About …` and left the assignee as a bare name
directly beneath it. The row proved a name could be labelled and then declined to
label the other one.

`TaskLine.Who` now says `Assigned to <name>`, joining the vocabulary the other two
ownership states already belonged to — `Unassigned`, and `Assigned, name
unavailable`. Deliberately not "Owner", which means something else here: the
member responsible for a deal, target or opportunity.

`RowLabel` also calls `TaskLine.About`, which it never did. The Command Center
showed `About X` and announced nothing of it, so the seen row and the spoken row
disagreed about what the row contained.

### The accessible row no longer loses the answer to a long title

Task semantics were appended last and cut first. The build-82 blind operator
measured it: *"the longer the task, the less an assistive user is told about it."*
The 160-character bound stays; what yields inside it changed. A 144-character
title now announces as

```
Chase the Saltmarsh completion bond paperwork through legal and confirm the…,
About Cassius Wetherbourne-Adeyemi, Assigned to Review member, Due 2026-09-25
```

## 4. What did not change

**Silence where a projection cannot speak.** `OpportunityTaskResponse` and
`ContractTaskResponse` carry no assignee field. Both were verified live carrying
tasks that *are* assigned in the domain, and both say nothing rather than
"Unassigned" — which they could not support. No field was added to either to make
the layouts symmetrical.

**Ownership authority, from builds 80 and 81.** An identifier present and null is
`Unassigned`; an identifier present whose name did not resolve is `Assigned, name
unavailable` and never `Unassigned`.

**Every row type outside targets and tasks** announces exactly what it did in
build 82. The owner role word is reached through `TargetLine`, which recognises
only rows declaring `ContactDisplayName`, so the thirteen other contract types
carrying `OwnerDisplayName` are untouched.

## 5. A defect introduced and fixed inside this build

The first cut of the budget arithmetic had no floor. Where the subject and the
assignee nearly filled the 160 characters between them, the title was given a
negative allowance and the row came out with a leading comma at 161 characters —
from the code whose purpose is to stay under 160. Reproduced as a failing theory,
then fixed by dropping the title below a length worth reading. Commit `7397378`.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI, and by
canonical Nightly on the same executable. Locally: build with 0 warnings and 0
errors, unit 3,922, Windows 1,087, Reviewer 163, integration 920, OpenAPI 3.1.1 at
264 paths and 188 schemas, TLC 4/4.

Live verification drove the real Windows client against a fresh synthetic case
whose contact, owner, subject and assignee are four distinct people, across the
target row, a person target, the pitch dialog, the deal target picker, the Command
Center, and the Talent, Pipeline and Contracts task tabs. Evidence in
`artifacts/operational-alpha/operator-context/`.

## 7. Verdict

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 83.
