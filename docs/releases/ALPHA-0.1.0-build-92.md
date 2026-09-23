# AgencyOS ALPHA 0.1.0, build 92

Reality Closure wave 9: a name finds the work, and a project says what it became.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **92** |
| Executable commit | **`a7378c4a1eb035fdd5e3869044cc06de23b0483d`** |
| Tag | **`alpha-a7378c4`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 91, `alpha-623d04e` |

**No contract change, no migration, no new endpoint and no new domain concept.**
Two server queries, one client method signature, four return paths, one harness
verb.

## 2. The executable commit is not the branch tip

```
git diff --stat a7378c4a1eb035fdd5e3869044cc06de23b0483d..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. This wave is about finding, not about truth

Every record repaired here was already correct, already stored and already
reachable by somebody who knew where to look. What was missing was the way in.

**F-06 — an opportunity could not be found by the name of the person it is
about.** A pursuit exists to place a human, and it was the one commercial surface
that could not be found by that human's name, while the deal it becomes and the
contract that follows both could. The search now matches the four identities a
pursuit records: the talent profile it is about, the company being approached, a
person approached directly, and the individual dealt with at a company.
`DealQueries` already contained exactly those joins, written for the same
purpose; the same shape now runs against the pursuit.

**Finance matched a reference string and nothing else.** An operator asking what
somebody owed had to already know a number, while the row in front of them showed
the payer's name. A receivable records both the client it earns for and the party
that has to pay; an invoice records the party billed. All are matched now.

**F-17 — Phase III called a project "a destination that points nowhere."** It
listed roles, companies, source properties, materials and packages, and said
nothing about the pursuit, deal or contract they exist for.

**Entity to intelligence.** Signals, theses and research cases all record their
subjects and render them, so the workspace could always say who a claim was
about. A person or company could not say what was claimed about them.

**The intelligence desk reported research with overdue work and offered no way to
reach it** — so an operator could see that something needed doing and still had
to already know which case it was.

**A receivable named its contract on every row and could not open it.** The other
half of that pair was wired in wave 4, when a contract gained the Obligations tab
that shows the money it obliges.

## 4. The register predicted a contract change. There was none

F-17 was the one finding in the whole programme recorded as needing new published
fields — *"contract impact of the whole repair programme: none, except F-17."*

That prediction assumed the repair meant enriching `ProjectDetailResponse`.
Measured at build 91, `OpportunityFilter`, `DealFilter` and `ContractFilter` all
already carry `ProjectId`, and `/opportunities`, `/deals` and `/contracts` all
already publish `projectId` as a query parameter. The relationships were stored,
queryable and published. **The client simply never asked.**

A project now reads those three lists through the filter that was already there.
No published shape changed. **The programme's contract impact is none, without
exception.**

## 5. Nothing was made findable that is not actually related

A record matches a human name only where a relationship is recorded: a subject, a
target, a contact, a client, a payer, a debtor. Nothing matches a coincidental
name or a phrase in prose, and **strategy notes remain unsearchable** — a caller
without the grant still cannot confirm what a note says by searching for it.

Every positive test is paired with a person who exists in the same tenant and is
on nothing. That negative was also driven live, which is the one result in this
wave worth reading twice: typing the name of a real, unrelated person returns an
empty list rather than everything.

## 6. Rows that do something say so

Every new return path is **invoked**, not selected, so the row carries the Invoke
pattern — which is what tells a screen reader it does something and what makes
Enter activate it. A row wired through selection alone would be a mouse-shaped
affordance announcing nothing about being a way out of the page.

The receivable's route is a button instead, because that row already means
"select this to act on it", and one gesture cannot mean both without navigating
away every time somebody picks a receivable to write off.

Navigation preserves role, not just name: a deal reached from a project still
reads `Counterparty: A24`.

## 7. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35802918232` and canonical Nightly run `35802920426`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,021 |
| Windows | 1,369 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 944 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

### Live journeys

Every one begins from something a real operator would know — a person's name, a
project title, a desk saying work is overdue — and never from an identifier.

| Starting clue | Destination | API fallback |
| --- | --- | --- |
| the name `Quillon` | `Autumn slate - lead role` in Pipeline | no |
| the name `Thessaly` (unrelated) | **nothing, correctly** | no |
| the project `The Quiet Coast` | its pursuit and deal, then the deal opened in Deals | no |
| the person `Evander Quillon-Mbeki` | a thesis and a research case, then the thesis opened in Intelligence | no |
| the desk's overdue-research row | that research case | no |

**API fallback count: 0.**

## 8. Verdict

**F-06 and F-17 are closed**, along with the three cross-surface pairs that were
silent in one direction. No in-scope instance remains open and none is blocked by
a projection: every relationship this wave needed was already modelled.

**F-05 is untouched**, deliberately. Its data is correct and its remaining issue
is window-versus-total disclosure, which is not a navigation question.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build
92.
