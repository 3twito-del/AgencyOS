# AgencyOS ALPHA 0.1.0, build 88

Reality Closure wave 5: an operator can now originate what the product could
already resolve.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **88** |
| Executable commit | **`ecef4567e5e8d1e47147c6668de75914c68ef7fe`** |
| Tag | **`alpha-ecef456`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 87, `alpha-01a1fd4` |

**No contract change, no migration, no new endpoint and no domain change.** Two
pages, three new dialogs, the command registry and two tests. Nothing under
`src/AgencyOS.Contracts/`, `src/AgencyOS.Api/`, `src/AgencyOS.Domain/`,
`src/AgencyOS.Application/` or `src/AgencyOS.Infrastructure/` was touched.

## 2. The executable commit is not the branch tip

```
git diff --stat ecef4567e5e8d1e47147c6668de75914c68ef7fe..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

The Definition-of-Done adjudication left four operations unsettled after F-13
closed. Three turned out to share a shape, and a sharper one than F-13's: **the
product could record what became of an object that no supported workflow could
create.**

| Act | Where | What it was before |
| --- | --- | --- |
| **Record option** | Contract → Options, beside *Record outcome* | an operator could say an option had been exercised and could not say the contract granted one |
| **Record obligation** | Contract → Obligations, beside *Record outcome* | the same, for what a contract obliges somebody to *do* |
| **Change status** | Talent → Representation, beside the status line | M4 shipped a representation status lifecycle and published its transition table; the page displayed the status and offered no way to change it |

The representation one is the sharpest. Converting a prospect activates the
representation for you, so **signing a client worked and ending one did not.** An
agency running this for a year would have reported people as current clients who
had left — in the client list, in commission eligibility, and in every count
derived from `IsClient`. Nothing was wrong with the data model. The one verb that
ends a relationship had no button.

**The fourth needed no repair.** `CreateTalentProfileAsync` has no caller, which is
what put it on the list, but a talent profile is not authored on its own: handing a
radar entry to representation creates one where none exists and tells the operator
it did. That route is real, was already delivered, and is now pinned rather than
assumed.

## 4. The transition table is not copied into the client

The dialog that changes a representation's status keeps no second edition of M4's
transition table. It offers the statuses, minus the one already held, and the
server says which this representation cannot reach — because a copy in the client
would be free to drift from the one actually enforced, invisibly.

Proved live: a representation in `Pending` was offered `Suspended`, and the server
refused in its own words — *"A representation cannot move from Pending to
Suspended."* The page did not change.

The one fact the page does mirror is that `Terminated` and `Expired` have an empty
transition set. That is terminality, not a rule about pairs, and a control that can
only be refused is a dead end.

## 5. Nothing resolves what nothing can create

The general form of what this wave found, and the part most worth keeping. A new
test pins each pair of an object's two halves, so a later build that ships an
outcome control without a way to originate what it acts on fails a gate rather than
waiting for a census to notice.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35782038893` and canonical Nightly run `35782060131`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,966 |
| Windows | 1,305 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

**Both journeys ran through the real Windows client** against a fresh tenant at
contract 17. A person went from nothing to a represented client entirely through
the product — radar, talent profile, prospect, client — and then through
`Suspended`, back to `Active`, and to `Terminated`, with the page closing her
scopes and team on the day given and disabling the control at the terminal state. A
contract option and a contract obligation were each recorded and then resolved
through the outcome controls that had been there since M8.

Two domain refusals were surfaced live and left the records unchanged: *"A party
cannot owe an obligation to itself"* and the transition refusal above.

Disclosures are recorded in full in
`artifacts/operational-alpha/reality-closure/WAVE-05-RESIDUAL-DOD-CLOSURE.md`,
including a correction to build 87's note about driving date pickers: the technique
works only on an empty picker, and opening a pre-filled one clears it.

## 7. Verdict

Every capability Reality Closure identified as shipped-and-unreachable now has a
route, and each was verified by an observed state transition. That is scoped to
what the census identified and adjudicated; CLAUDE.md §7 still has no general
automated enforcement, which is the mechanism that let all of this through.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 88.
