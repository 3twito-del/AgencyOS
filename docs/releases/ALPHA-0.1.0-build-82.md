# AgencyOS ALPHA 0.1.0, build 82

The task actionability repair: every list that shows a task now says who owns the
work and when it is due, and the Command Center no longer shows a task's subject
where its owner should be.

**Date:** 2026-09-20

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **82** |
| Executable commit | **`d6bd5b84c04a9955c4d6ddb6d8f05fec5ed93e3e`** |
| Tag | **`alpha-d6bd5b8`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 81, `alpha-af87542` |

**No contract change and no migration.** `src/AgencyOS.Contracts/` was not touched.

## 2. The executable commit is not the branch tip

```
git diff --stat d6bd5b84c04a9955c4d6ddb6d8f05fec5ed93e3e..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Six surfaces list tasks. Five decided separately what a row should show, and the
result was that **none of them showed an owner or a due date**. The one that showed
a person showed the wrong one: the Command Center rendered the task's **subject** —
the client it is about — unlabelled beneath the title, in the position a byline
occupies, and a blind operator read it as who was accountable.

Every value on that row was true. Their arrangement was not.

### One vocabulary, six layouts

The wording now lives in one place. `TaskLine` decides what a row may say about
who, when, and what it concerns; six layouts ask it rather than each inventing an
answer. Layouts still differ where the context differs — a research task and a
contract task do not need the same shape — but what a row **means** no longer
varies by which screen you opened.

### Ownership, extended from one banner to every list

| State | Shown |
| --- | --- |
| identifier present, name resolved | the name |
| identifier present, no name | "Assigned, name unavailable" — never "Unassigned" |
| identifier present and null | "Unassigned" |
| **no identifier field at all** | **nothing** |

That last row is the one that matters. **Contract, opportunity and finance task
responses carry no assignment at all.** They are not authoritative for ownership,
so they say nothing — because "Unassigned" would be a claim they cannot support.

This is not hypothetical. A contract task **created with an assignee** comes back
from its own endpoint with no assignee field, so the contract surface shows the
title and the due date and stays quiet about the rest. Saying "Unassigned" there
would have reintroduced the build-80 defect through a well-meant repair.

Extending those three published responses would be an additive API change and so a
**contract bump**; it was not made, and the decision is recorded for the owner.

### The subject stays, labelled

It was never the problem, and it is how an operator tells rows apart on a list that
spans clients. It reads "About X" now, with the owner beside it. **Two labelled
identities cannot be confused for one another the way one bare identity could.**

### Dates and accessibility

Due dates render `yyyy-MM-dd` under InvariantCulture; an undated task says **"No
due date"** rather than falling silent. `RowLabel`'s vocabulary had no word for a
task's assignee, so a screen reader announced "title, open, high" and left a blind
operator exactly where the visual row did — task rows now announce the same owner
and due date the visible row shows. No identifier is ever spoken.

### One divergence absorbed rather than renamed

Research tasks call the field `AssignedToDisplayName`; the rest call it
`AssigneeDisplayName`. Both are published. The shared wording accepts both, which
removes the risk without a contract change.

## 4. What validated it

### CI — run [35526826825](https://github.com/3twito-del/AgencyOS/actions/runs/35526826825), build 82

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,899 / 3,899** |
| Windows tests | **1,083 / 1,083** |
| Reviewer tests | **163 / 163** |
| Integration, **PostgreSQL 18.6** | **920 / 920** |
| API contract | OpenAPI **3.1.1**, **264 paths, 188 schemas** — unchanged |
| Formal (TLC) | **4 / 4** |
| Release artifact | **111 artifacts**, contract **17**, manifest verified, unsigned |

### Nightly — run [35527327243](https://github.com/3twito-del/AgencyOS/actions/runs/35527327243), tag `nightly-d6bd5b8`

| Gate | Result |
| --- | --- |
| Integration, **PostgreSQL 18.6** | **920 / 920** |
| Unit tests | **3,899 / 3,899** |
| Nightly artifact (Windows) | **published** |

### Live verification

Against a fresh synthetic case, through the ordinary Windows client:

- **assigned and dated** — `Return the Blackthorn Quay deal memo | Review member | Due 2026-10-14 | High`
- **genuinely unassigned** — `Confirm the fitting date with wardrobe | Unassigned | Due 2026-10-30 | Normal`
- **about a company, assigned elsewhere, no date** — `Ask Blackthorn Quay who is covering the insurance rider | About Blackthorn Quay Media | Review Owner | No due date | Normal`
- **the row that caused the finding** — `Give Desmond a delivery date… | About Anneliese Varga-Oyelaran | Review Owner | Due 2026-09-17 | High`
- **subject ≠ owner, both people** — `Give Pale Harbour a start date | About Cordelia Ashgrove-Mbeki | Review member | Due 2026-09-18 | High`
- **not authoritative** — `Serve the option notice… | Normal | Due 2026-11-05`, silent on ownership for a task that **is** assigned

## 5. Coverage added

**+13 unit, +15 Windows.** `TaskLineTests` covers the meaning — including a
projection with no assignment field staying silent, a name without an identifier
that may name but not deny, and a task about one person assigned to another keeping
them apart. `TaskSurfaceSemanticsTests` covers the family: every surface asks the
shared wording, no row binds `Subject.Name` directly, and the Command Center row
carries subject, owner and due date distinctly. Semantic assertions — no pixel or
screenshot coupling.

## 6. What this version does not claim

- **Three published task responses still cannot express ownership.** Those surfaces
  are honestly silent rather than wrong. Changing that needs a contract decision
  that was not taken here.
- **Deals, Pipeline and Intelligence were not verified live** — the LAB fixture
  holds no deal, opportunity or research task. They are covered by the
  whole-surface and shared-wording tests.
- **No governance rule was added.** The attribution wording drafted in the prior
  review remains a candidate; this repair did not require it, and CLAUDE.md was not
  touched.
- **The blind-handoff verdict is unchanged at THESIS PARTIALLY DEMONSTRATED.**
- Everything build 81 did not claim still stands.

## 7. Reproducing the validation

```
git fetch --tags
git checkout alpha-d6bd5b8
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```
