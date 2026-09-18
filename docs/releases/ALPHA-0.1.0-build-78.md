# AgencyOS ALPHA 0.1.0, build 78

The operational-alpha correctness repair: five defects the product-hypothesis
evaluation demonstrated against build 77, repaired and validated.

**Date:** 2026-09-19

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **78** |
| Executable commit | **`004d550e4d66661c8938e07d5589e5cf4afef795`** |
| Tag | **`alpha-004d550`** |
| Branch | `repair-wave-001` |
| API contract version | **15** (was 14) |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Supersedes | ALPHA 0.1.0 build 78's predecessor, `alpha-d8a8bc5` (build 77) |

## 2. The executable commit is not the branch tip

Every gate below ran against `004d550e`. Commits after it on `repair-wave-001` are
**documentation only** — this file and the record of it.

To build, install or audit what was validated, use `alpha-004d550`. To confirm
that rather than accept it:

```
git diff --stat 004d550e4d66661c8938e07d5589e5cf4afef795..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Each defect was reproduced by a failing test **before** it was repaired, and each
of those tests fails again if the repair is reverted. The evidence that found
them is `artifacts/operational-alpha/`, which is untracked and unchanged.

| # | Defect on build 77 | Root cause | Repair |
| --- | --- | --- | --- |
| A | `GET /deals` answered **500** for `talentProfileId`, `projectId`, `counterpartyCompanyId`, `counterpartyPersonId` | One shared shape: a correlated `Any()` comparing two converted strongly-typed identifiers and unwrapping a nullable one through `.Value.Value`, which EF Core cannot translate | One shape for all four — the related identifiers selected and `Contains`'d against this row's foreign key, which PostgreSQL runs as a single `IN (subquery)` |
| B | `/intelligence/relationships/Company/{id}` answered **500** while `Person` answered 200 | The company branch projected `new RadarPerson(default, …)` — a constant of the converted `PersonId` type inside a projection | Project the name, build the record after the round trip |
| C | A contract could be **signed with no paper**, derive `Executed`, and then never accept a version — an executed instrument permanently unable to carry its own money | Nothing required a `ContractVersion` before a signature | An execution-driving signature is refused unless a version exists, checked before the aggregate is touched |
| D | The Deals workspace opened on `Negotiating`, so a negotiation at `TermsAgreed` vanished from it | The client could ask for one status, and the API could express nothing else | `GET /deals?openOnly=true` — the statuses that are still work, read from `Deal.LiveStatuses` |
| E | A **draft with an effective date could raise a collectible obligation** | ADR-0023 §3 defined operative as executed **or** effective-dated | Operative means **executed**. ADR-0040 |

### On D, specifically

`openOnly` reads `Deal.LiveStatuses` — `Draft`, `Negotiating`, `TermsAgreed` —
rather than repeating the set in the API, the query or the client, and it filters
**in SQL before the limit**. That last part is the reason it is a server filter:
the list orders by `UpdatedAt` and takes a page, closing a deal updates it, so
closed rows would otherwise fill a page that was filtered afterwards.
`ClosedNegotiationsDoNotDisplaceLiveOnesWithinTheLimit` proves it.

The client sends a chosen status **or** `openOnly`, never both. "Any status" is
its own explicit choice, so "live work" and "everything, closed included" cannot
collapse into the same selection. `Negotiating` remains selectable.

### On E, specifically

An effective date keeps its own meaning. It may still precede execution, follow
it or be retroactive, and nothing forces `EffectiveOn` to equal `ExecutedOn` —
`AnEffectiveDateBeforeExecutionIsPreserved` pins a date sixty days before
execution surviving it. What changed is that a date no longer answers a question
it was never asking.

Agreed terms remain representable before execution. Draft offers, accepted
offers, contract versions and effective dates all keep their semantics. The
distinction the model now holds is the one it always meant: **agreed terms are
not a collectible legal obligation.**

## 4. The API contract moved to 15

This is an **intentional additive change**, and the first contract move since 14.

`ApiContract`'s own history is the rule: versions 2 through 14 are each an
additive capability addition, and each incremented. The line *"Every step so far
is additive, so the supported range stays open at 1"* says additivity is what
holds `MinimumSupported` at **1** — not what holds `Current` still.

| | |
| --- | --- |
| What changed | `GET /deals` gained one optional query parameter, `openOnly` |
| Backward compatibility | A caller that omits it gets exactly what contract 14 gave it, pinned by `OmittingTheParameterPreservesPreviousBehaviour` |
| `MinimumSupported` | **1**, unchanged — no older client becomes unsafe |
| Published document | 263 paths, 187 schemas — both unchanged; the document's hash moves because the parameter is in it |
| Schema | unchanged; no migration |

## 5. What validated it

Two independent runs against the exact commit.

### CI — run [35402614364](https://github.com/3twito-del/AgencyOS/actions/runs/35402614364), build 78

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,837 / 3,837** |
| Windows tests | **1,048 / 1,048** |
| Reviewer tests | **163 / 163** |
| Integration, **PostgreSQL 18.6** | **908 / 908** |
| API contract | OpenAPI **3.1.1**, **263 paths, 187 schemas** |
| Formal (TLC) | **4 / 4** — `OfflineWriteQueue`, `OutboundSend`, `AiApproval`, `LocalInferenceLease` |
| Release artifact | **111 artifacts**, contract **15**, manifest verified |

### Nightly — run [35403386853](https://github.com/3twito-del/AgencyOS/actions/runs/35403386853), tag `nightly-004d550`

| Gate | Result |
| --- | --- |
| Integration, **PostgreSQL 18.6** | **908 / 908** |
| Unit tests | **3,837 / 3,837** |
| Nightly artifact (Windows) | **published** |

### Release manifest

Read from the downloaded artifact:

```
version = 0.1.0      channel = alpha        buildId = 78
gitCommit = 004d550e4d66661c8938e07d5589e5cf4afef795
apiContractVersion = 15
expectedSchema = 20260909072201_AiResultClassification
ciRun = 35402614364  signing = unsigned     artifacts = 111
```

### Live verification

Run against this build on the LAB cluster with synthetic data:

- all four deal relationship filters **200**, with correct counts including a true
  negative;
- `/intelligence/relationships/Company/{id}` **200**, `Person` **200**;
- a contract with no version **refuses** a signature and is left untouched;
- version → approve → two signatures → `PartiallyExecuted` → `Executed`;
- a draft with an effective date **refuses** an obligation;
- executed with a version **accepts** one, and a receivable is raised;
- the Deals workspace, driven through UI Automation, went from **1 row to 3**, the
  first being `Noa Wexler-Adeyemi / The Salt Road — lead, Harborlight Pictures,
  Terms agreed, Talent employment` — the negotiation that was invisible on
  build 77.

## 6. Test coverage added

**+37 integration, +5 unit, +5 Windows.**

| Suite | Tests | Covers |
| --- | --- | --- |
| `DealRelationshipFilterTests` | 18 | each filter: match, no match, tenant isolation, combined with status and kind, and a guard that none may answer 500 |
| `DealOpenOnlyFilterTests` | 9 | each live status included, each terminal status excluded, omitted parameter unchanged, explicit status preserved, tenant isolation, limit displacement |
| `ContractOperativenessTests` | 6 | signature refused without paper and nothing changed, lawful lifecycle, draft-plus-effective-date refused, executed accepted through to a receivable, retroactive date preserved |
| `CompanyRelationshipIntelligenceTests` | 4 | company answers, person unchanged, tenant isolation, uncomputed kinds still 404 |
| `DealWorkspaceDefaultTests` | 5 | the default asks for live work; `TermsAgreed` stays visible; an explicit status is sent alone; clearing returns to the default |
| `DealsWorkspaceFilterTests` | 5 | the markup default, `Negotiating` still selectable, every live status selectable, "everything" is its own choice |

Two existing tests encoded the old behaviour and were **rewritten, not deleted**:
`TheList_DefaultsToActiveNegotiations` became `TheList_DefaultsToLiveWork`, and
the filter pass-through assertion now pins that an explicit status is sent without
`openOnly`.

## 7. What was deliberately not changed

- **Two adjacent queries** in `ProjectQueries.cs` share the correlated-`Any` shape
  without the `.Value.Value` unwrapping. Both were exercised live and both answer
  **200**, so the failure does not reproduce and they were left alone. No
  speculative repository-wide query rewrite was performed.
- **Signatures still carry no version association.** The model does not record
  which sheet was signed, and this repair did not add it; the check is that paper
  exists. A signature naming its version would be a model change with its own
  decision.
- **Unsigned-but-binding is not modelled** and is not proxied by an effective
  date (ADR-0040 §5).
- Bookings, pencils, recalls, calendars, accounting integrations, payment rails,
  e-signature providers, talent apps, casting-network integration, a generic
  `GET /interactions`, automatic talent-profile creation, AI expansion and broad
  dispatch refactors are all **out of scope** and untouched. They are product
  gaps, not correctness defects.

## 8. What this version still does not claim

Unchanged from build 77 and restated so the repair is not read as broader than it
is: unsigned, no security audit, the empty-403 authorization gap, the 217
unrefactored dispatch sites, the two identifier-only link kinds and the three
deliberately unwired finance dialogs. The product-hypothesis evaluation's verdict
of **THESIS PARTIALLY DEMONSTRATED** is **not** revised by this pass; it was not
re-run.

## 9. Reproducing the validation

```
git fetch --tags
git checkout alpha-004d550
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```

The integration suite needs PostgreSQL 18.6 and the PostgreSQL 18 client tools on
`PATH`; without the client tools the three backup/restore drills fail on a missing
`pg_dump`, which is an environment result and not a product one.
