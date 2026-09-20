# AgencyOS ALPHA 0.1.0, build 79

The blind-handoff operability repair: the three findings the blind retest of
build 78 left open, repaired and validated.

**Date:** 2026-09-20

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **79** |
| Executable commit | **`329d8db626a3ba5c503767d7c1800fbefa31612a`** |
| Tag | **`alpha-329d8db`** |
| API contract version | **16** (was 15) |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 78, `alpha-004d550` |

## 2. The executable commit is not the branch tip

Every gate below ran against `329d8db6`. Commits after it are documentation only.

```
git diff --stat 329d8db626a3ba5c503767d7c1800fbefa31612a..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

None of the three was a missing capability. All three were the product knowing
something and not saying it.

### A — the Deal page asserted that no contract existed

Reproduced at the data level first: a deal at `TermsAgreed` whose detail carries
**no contract field at all**, while `GET /contracts?dealId=` returned an
**Executed** contract.

The banner was a fixed sentence in `DealsPage.xaml`, rendered `Severity="Success"`
whenever a deal reached `TermsAgreed`:

> "This records that the parties agreed terms. No contract has been drafted,
> signed or executed, and AgencyOS does not track that yet."

Its own comment gave the reason — *"M7 has no way to know whether any contract
exists (ADR-0021)"* — which was **true when M7 shipped**. M8 added contracts and
neither the sentence nor the deal read model was revisited.

**The repair needed no API change.** `ListContractsAsync(dealId:)` already
existed, so the page asks. `ContractStanding` takes the statuses the server
reports for that deal and says only what they support, in M8's own words. There is
no second contract taxonomy and no contract state duplicated in the client.

Two decisions worth stating:

- **An absent contract is stated, not celebrated.** It renders informational, not
  green. The absence of an error is not an achievement, and green is what made the
  old sentence persuasive.
- **An unknown contract state produces silence.** The one thing that must never
  happen on this surface is reporting an absence, so a state this build has no word
  for says nothing rather than falling back to "none".

### B — a human name found nothing

Searching a represented client's name returned **0** from Deals, Contracts and
Projects. The case was discoverable only by somebody who already knew what it was
called, which is exactly what a handoff does not have.

Each list searched only its own text — documented as such, so widening them is a
**recorded contract change** and not a silent one. Each now also reaches the
relationships its own model carries:

| List | Also searches |
| --- | --- |
| Deals | the counterparty on its target, and the represented client its pursuit is about |
| Contracts | its parties — people, companies, and a party recorded only by an external name |
| Projects | the people currently attached to it |

Written as subqueries of identifiers rather than nested `Any()`, so the whole
thing stays one statement and the limit still applies to a filtered set.

**Nothing was invented to make this work.** A person linked to a project *only*
through an opportunity is still not found through the project, because that is not
a relationship the project model has. Stated as a limitation rather than faked.

### C — "what should I do now" was not a product answer

The task existed the whole time: open, dated, prioritised, carried on the deal,
contract and talent read models with its `DueAt`. No surface said it was next.

`NextAction` orders tasks that already exist by the only rule the domain supports
without inventing importance:

> Open tasks only. Earliest due date first — which puts overdue before upcoming,
> because overdue dates are earlier. Undated last. **A tie is declared, not
> broken:** two tasks due the same day are two tasks, and choosing between them
> would be a judgement nothing in the domain supports.

No task system, no case entity, no ranking, no task duplicated. It is offered on
the negotiation and on the client — the two surfaces that already carried the
tasks — through one shared banner so both say it the same way.

## 4. The API contract moved to 16

Widening what a documented field searches is a capability addition, and every step
in `ApiContract`'s history is additive and every one incremented.

| | |
| --- | --- |
| What changed | the documented meaning of `search` on deals, contracts and projects |
| Backward compatibility | a caller that searches a title gets exactly what contract 15 gave it, pinned by `OrdinaryTitleSearchIsUnchanged` |
| `MinimumSupported` | **1**, unchanged |
| Published document | **263 paths, 187 schemas** — both unchanged; no parameter was added |
| Schema | unchanged; no migration |

Three search placeholders were reworded, because a box that says it searches a
name while also searching people is the same class of defect as the banner.

## 5. What validated it

### CI — run [35482958441](https://github.com/3twito-del/AgencyOS/actions/runs/35482958441), build 79

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,868 / 3,868** |
| Windows tests | **1,054 / 1,054** |
| Reviewer tests | **163 / 163** |
| Integration, **PostgreSQL 18.6** | **916 / 916** |
| API contract | OpenAPI **3.1.1**, **263 paths, 187 schemas** |
| Formal (TLC) | **4 / 4** |
| Release artifact | **111 artifacts**, contract **16**, manifest verified |

### Nightly — run [35483436970](https://github.com/3twito-del/AgencyOS/actions/runs/35483436970), tag `nightly-329d8db`

| Gate | Result |
| --- | --- |
| Integration, **PostgreSQL 18.6** | **916 / 916** |
| Unit tests | **3,868 / 3,868** |
| Nightly artifact (Windows) | **published** |

### Live verification

Against a fresh synthetic scenario on the LAB cluster:

- **A:** on the exact deal shape that showed the lie — `TermsAgreed` with an
  executed contract — `'Contract executed' is on screen`, and `nothing named
  'Commercial terms agreed' is in the tree`.
- **B:** from the human name alone, deals **1**, contracts **1**, projects **1**
  (the last after attaching the person to the project, which is the project
  model's own person relationship).
- **C:** `'Next action' is on screen` on the client's page.

## 6. Coverage added

**+8 integration, +31 unit, +6 Windows.**

| Suite | Tests | Covers |
| --- | --- | --- |
| `HumanNameDiscoveryTests` | 8 | client name → deal, counterparty name → deal, client → contract, attached person → project, unrelated person matches nothing, tenant isolation, title search unchanged, case-insensitivity |
| `ContractStandingTests` | 11 | an executed contract is never reported absent; **no contract state can produce the old sentence**; none/draft/executed are three different messages; a missing contract is not a success; an ended instrument is not green; unknown states say nothing |
| `NextActionTests` | 12 | open-only, overdue first, earliest future, undated last, ties declared, empty titles rejected, counts exclude completed work |
| `DealPaperTruthTests` | 6 | the sentence cannot return; the banner carries no fixed wording or severity; both surfaces use the shared next-action banner |

## 7. What this version does not claim

- **B's UI proof is server semantics plus the view-model wiring test, not
  keystrokes.** The review harness cannot type into an `AutoSuggestBox` — `focus:`
  and `set:` both refuse it. That is a tooling limit, recorded rather than papered
  over.
- **A project reachable only through an opportunity is still not found by a
  person's name** (§3B).
- Everything build 78 did not claim still stands: unsigned, no security audit, the
  empty-403 authorization gap, the 217 unrefactored dispatch sites, the two
  identifier-only link kinds, the three deliberately unwired finance dialogs.
- **The blind-handoff verdict is unchanged at THESIS PARTIALLY DEMONSTRATED.**
  It was not re-run, and this repair does not revise it. The other 24 findings the
  blind operator recorded remain open.

## 8. Reproducing the validation

```
git fetch --tags
git checkout alpha-329d8db
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```

The integration suite needs PostgreSQL 18.6 and the PostgreSQL 18 client tools on
`PATH`; without them the three backup/restore drills fail on a missing `pg_dump`,
which is an environment result and not a product one.
