# AgencyOS ALPHA 0.1.0, build 81

The minimum operator-context coherence repair: the first two findings taken under
the invariant adopted in `7855811`, and the first test of whether that rule changes
how a repair is designed.

**Date:** 2026-09-20

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **81** |
| Executable commit | **`af87542b968db4fea7eab4c9769100b7445ad851`** |
| Tag | **`alpha-af87542`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 80, `alpha-993109f` |

**No contract change and no migration.** Every field this repair needed was already
published, already mapped and already arriving.

## 2. The executable commit is not the branch tip

Every gate below ran against `af87542b`. Commits after it are documentation only.

```
git diff --stat af87542b968db4fea7eab4c9769100b7445ad851..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

### A — a task assigned to a named member was reported as "Unassigned"

The blind-handoff retest of build 80 read **"Unassigned"** off a task assigned to
Review member, and briefed the case as unowned.

Nothing was wrong with the domain, the column, the API or the renderer's own
reasoning. The loss happened twice:

- `RepresentationQueries` called the task projection's **two-argument** overload,
  whose assignee lookup is optional, so the overview returned an assignee **id with
  no name**;
- the view model then kept the name and **dropped the id**, and a renderer holding
  only a null name cannot tell *nobody is accountable* from *this read did not
  resolve who*.

**The repair is not "populate the display name."** That was the obvious one-line
fix, and it would have left the mechanism intact — the task list was already
resolving names correctly when the overview was not, which is exactly how this
shipped.

Under the new invariant, absence must stay distinguishable to the renderer:

- the assignee **id is carried through to presentation** and never discarded;
- ownership is decided from it, not from the name;
- three states replace two — **named**, **assigned but unnamed**, **nobody**;
- the overview resolves the name as well, and both queries now take that lookup
  from **one** place instead of two. The duplicate was the cause.

There is **no fourth state**. The name lookup reads every user with no permission
filter, so the authorization model cannot currently hide an identity, and
CLAUDE.md §5 says not to require states the domain cannot produce. No test pretends
otherwise.

### B — detailed notes were unreachable from the person

Interaction detailed notes were rendered on exactly one surface in the client, the
org-wide Command Center. The person's own record showed **none of its contact at
all** — although the overview had been carrying it, unrendered, the whole time.
Somebody handed a name could read that a call happened and never learn what the
call was about.

**The timeline was not touched.** ADR-0012 makes it a curated summary on purpose,
and widening it to carry notes would have contradicted an accepted decision in
order to answer a question the timeline is not for.

Instead the person's Talent context shows its **recent contact**, using the same
disclosure the Command Center uses — promoted to a shared template so the two
surfaces cannot drift apart again about what a contact shows. Collapsed by default,
absent entirely when there is nothing to read, and reachable from the keyboard.

## 4. What validated it

### CI — run [35521783619](https://github.com/3twito-del/AgencyOS/actions/runs/35521783619), build 81

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,886 / 3,886** |
| Windows tests | **1,068 / 1,068** |
| Reviewer tests | **163 / 163** |
| Integration, **PostgreSQL 18.6** | **920 / 920** |
| API contract | OpenAPI **3.1.1**, **264 paths, 188 schemas** — unchanged |
| Formal (TLC) | **4 / 4** |
| Release artifact | **111 artifacts**, contract **17**, manifest verified, unsigned |

### Nightly — run [35522369680](https://github.com/3twito-del/AgencyOS/actions/runs/35522369680), tag `nightly-af87542`

Dispatched against the same commit, not the branch tip.

| Gate | Result |
| --- | --- |
| Integration, **PostgreSQL 18.6** | **920 / 920** |
| Unit tests | **3,886 / 3,886** |
| Nightly artifact (Windows) | **published** |

### Live verification

Against a fresh synthetic case on the LAB cluster, through the ordinary Talent UI:

- **assigned** — *"Confirm the stop-date before the Verdigris table read.
  **Review member.** Due 9 October 2026. One other action is open."*
- **genuinely unowned** — *"Decide whether to submit for the Halvard pilot.
  **Unassigned.** Due 15 October 2026."*
- **detailed notes** — from the person's own surface, by keyboard
  (three tabs to a focusable **Button "Detailed notes"**), the full note read back:
  *"Sunniva said Verdigris has lost its completion bond and is carrying the second
  block on a bridge facility that expires on 2026-11-30…"*
- **no dead affordance** — an interaction with no notes exposes **no** expander.

The third ownership state — assigned with the name unresolved — is proved at the
presentation and read-model level rather than by corrupting a stored task.

## 5. Coverage added

**+4 integration, +5 unit, +6 Windows.**

| Suite | Covers |
| --- | --- |
| `TaskOwnershipProjectionTests` (integration) | the read-model contract: **a projection reporting an assignee id must report the name**, asserted across every ownership-bearing read at once, so a future path that forgets the lookup fails here rather than in a handover |
| `TaskOwnershipTruthTests` (unit) | the three states, that a blank name is a missing name, and that unassigned and unresolved are **not the same state** |
| `OwnershipSurfaceTruthTests` (Windows) | the **sentences**: no composed message calls owned work unowned; a resolved name is spoken and the identifier is not |
| `DealPaperTruthTests` (extended) | the renderer branches on `IsAssigned` rather than the name, and recent contact is on the person's surface |

## 6. What this version does not claim

- **The blind-handoff verdict is unchanged at THESIS PARTIALLY DEMONSTRATED.** It
  was not re-run, and this repair does not revise it.
- **Only two findings were taken.** Reconciliation, contract obligations,
  opportunity search asymmetry, intelligence reachability, cross-client search
  contamination and the rest of the third retest remain open and untouched.
- The local `postgres:18.6` container gate still does not run on the authoring
  machine; Docker's Linux engine is unavailable. Local integration used **LAB
  PostgreSQL 19 beta 3** as corroboration. **The 920 / 920 on 18.6 is CI's and
  Nightly's.**
- Everything build 80 did not claim still stands.

## 7. Reproducing the validation

```
git fetch --tags
git checkout alpha-af87542
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```

The integration suite needs PostgreSQL 18.6 and the PostgreSQL 18 client tools on
`PATH`; without them the three backup/restore drills fail on a missing `pg_dump`,
which is an environment result and not a product one.
