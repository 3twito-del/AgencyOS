# AgencyOS ALPHA 0.1.0, build 80

The blind-handoff truth and actionability repair: the five findings the blind
retest of build 79 left open, repaired and validated.

**Date:** 2026-09-20

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **80** |
| Executable commit | **`993109f1c817a2d2ebb19815934c542bc829f9c1`** |
| Tag | **`alpha-993109f`** |
| API contract version | **17** (was 16) |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 79, `alpha-329d8db` |

## 2. The executable commit is not the branch tip

Every gate below ran against `993109f1`. Commits after it are documentation only.

```
git diff --stat 993109f1c817a2d2ebb19815934c542bc829f9c1..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Four of the five were the product knowing something and not saying it. The first
was the product saying two things at once.

### A — the Deal page contradicted itself, twenty pixels apart

Build 79 repaired the green banner that asserted no contract existed. It did not
repair the subtitle beside it, which inferred the same thing from deal status
alone. On a `TermsAgreed` deal with an executed contract, an operator read:

> "terms agreed, no contract recorded"

directly above a panel reading **"Contract executed"**.

The build-79 repair was real, was green, and was verified live. What was verified
was *the string that changed* rather than *the screen an operator reads*. The
subtitle was older, had never been looked at, and inferred contract absence from
deal status — the same mistake in a different control.

**The repair.** The deal's own line now says nothing about paper at all — "terms
agreed", and nothing more — because contract truth on this page has exactly one
source, the standing computed from the contracts the server reports.

The guard is a **whole-screen invariant**: every deal status crossed with every
arrangement of contracts a deal can carry, asserting that the page may never
claim presence and absence together, whatever the contract is doing. Testing one
control is what let the contradiction survive a repair.

### B — the longer account reached nobody

`DetailedNotes` has been on the interaction read model since the first release and
no surface ever rendered it. Whatever an operator wrote past the one-line summary —
a payment condition, which part was fact and which was judgement — was stored
faithfully and shown to no one.

It is now a collapsed `Expander` on the communication surface: folded away
entirely when empty, keyboard-operable and text-selectable, which a trimmed
`TextBlock` is not.

### C — a named tab that could never show anything

The project **Attachments** tab was bound to nothing at all. Attachments already
arrive nested inside each role, so they are flattened in the view model rather
than fetched — holders first, then by name — and the tab now represents the data
it is named after.

A tab either means something or should not exist.

### D — an open task with nobody accountable

The blind operator asked who owned an overdue action, found no such field, and
concluded the model had no such idea.

**That conclusion was wrong, and so was mine.** `TaskItem.AssignedTo` has existed
since M2, is populated, and deal and contract follow-ups already set it. It simply
never reached the API contract. The capability was there; the contract hid it.

Contract 17 exposes it:

- a task carries an assignee and that person's display name;
- a task may be **created** assigned;
- an assignment can be changed or cleared through `POST /tasks/{taskId}/assignee`.

Clearing is a real state, not a gap: work is sometimes genuinely unowned, and
saying so is more honest than leaving the last person's name on something they
handed back. An assignee must be an **active member of the same organization**,
checked in the handler because the entity cannot see the membership table.

Assignment is accountability, and stays separate from `CreatedBy`, which is
provenance, and from `Subject`, which is who the task is about. **No migration:
the column was always there.**

The next-action banner now names that person, or prints "Unassigned" rather than
staying silent — a blank space reads as "somebody has this" when often nobody
does.

### E — one date, written two ways

Representation dates on the talent surface mixed a culture-dependent short date
with an ISO one. They are all `yyyy-MM-dd` under `InvariantCulture` now, through a
converter rather than per-call formatting, so the next surface cannot drift.

## 4. The API contract moved to 17

Additive throughout. Tasks gained an assignee and a display name, task creation
gained an optional assignee, and one endpoint was added. Nothing about existing
tasks changes, no existing client changes behaviour, and the supported range stays
open at **1**.

`build/Version.props` and `ApiContract.Current` moved together, which
`BuildMetadataTests` enforces.

## 5. What validated it

### CI — run [35508029003](https://github.com/3twito-del/AgencyOS/actions/runs/35508029003), build 80

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,881 / 3,881** |
| Windows tests | **1,059 / 1,059** |
| Reviewer tests | **163 / 163** |
| Integration, **PostgreSQL 18.6** | **916 / 916** |
| API contract | OpenAPI **3.1.1**, **264 paths, 188 schemas** |
| Formal (TLC) | **4 / 4** — OfflineWriteQueue, OutboundSend, AiApproval, LocalInferenceLease |
| Release artifact | **111 artifacts**, contract **17**, manifest verified, unsigned |

### Nightly — run [35508622500](https://github.com/3twito-del/AgencyOS/actions/runs/35508622500), tag `nightly-993109f`

Dispatched against the same commit, not the branch tip.

| Gate | Result |
| --- | --- |
| Integration, **PostgreSQL 18.6** | **916 / 916** |
| Unit tests | **3,881 / 3,881** |
| Nightly artifact (Windows) | **published** |

### Live verification

Against a synthetic scenario on the LAB cluster:

- **A:** `'Contract executed' is on screen`, with no sentence beside it claiming
  absence.
- **B:** `'Detailed notes' is on screen`.
- **C:** `'Cordelia Ashgrove-Mbeki' is on screen` on the Attachments tab.
- **D:** `'Next action' is on screen`, and the API reads back
  `assigneeDisplayName: "Review member"`.
- **E:** `yyyy-MM-dd` applied.

## 6. Coverage added

`DealPageTruthTests` (new, unit) holds the whole-screen invariant over 5 deal
statuses crossed with 11 contract arrangements. `DealPaperTruthTests` (Windows)
gained guards that the next action names an owner or says there is none, that
detailed notes are reachable, that the attachments list has a source, and that
representation dates use one format.

## 7. What this version does not claim

- **The local `postgres:18.6` container gate did not run on the authoring
  machine.** Docker Desktop's Linux engine would not start — its `docker-desktop`
  WSL distribution is absent and the VM's last successful init was 2026-05-03. The
  local integration run used **LAB PostgreSQL 19 beta 3**, as corroboration only.
  **The 916 / 916 on PostgreSQL 18.6 above is CI's**, which pins that image.
- **Build 79's release note contains a claim since withdrawn.** Its §7 stated that
  "the review harness cannot type into an `AutoSuggestBox`". That is false: the
  behaviour is timing-dependent and a blind operator obtained keystroke proof. The
  tagged document is left as it was written, and corrected here rather than
  rewritten there.
- **The blind-handoff verdict is unchanged at THESIS PARTIALLY DEMONSTRATED.** It
  was not re-run, and this repair does not revise it. The other findings the blind
  operator recorded remain open.
- Everything build 79 did not claim still stands: unsigned, no security audit, the
  empty-403 authorization gap, the unrefactored dispatch sites, the two
  identifier-only link kinds, the three deliberately unwired finance dialogs, and
  a project reachable only through an opportunity still not found by a person's
  name.

## 8. Reproducing the validation

```
git fetch --tags
git checkout alpha-993109f
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```

The integration suite needs PostgreSQL 18.6 and the PostgreSQL 18 client tools on
`PATH`; without them the three backup/restore drills fail on a missing `pg_dump`,
which is an environment result and not a product one.
