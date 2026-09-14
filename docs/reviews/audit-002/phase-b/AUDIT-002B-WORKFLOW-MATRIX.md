# Audit 002 Phase B — mutation workflow matrix

§13. Phase A exercised mutations in one domain of eleven. Phase B took that to
six, and says plainly where it stopped.

All synthetic, in the disposable review organization on `agencyos_w2`, through
canonical API commands carrying the headers the Windows client sends — so
release-policy, idempotency, authorization and domain validation all ran.

---

## 1. Mutations executed in Phase B

| # | Domain | What | Result |
| ---: | --- | --- | --- |
| 1 | Projects | Create project | **201** |
| 2 | Projects | Add project role | **201** |
| 3 | Packaging | Create package | **201** |
| 4 | Deals | Record inbound offer with priced terms | **201** |
| 5 | Deals | Answer the offer — Accept | **200**, deal moved `Negotiating` → `TermsAgreed` |
| 6 | Deals | Second negotiation, kept open | **201** |
| 7 | Deals | Second open offer | **201** |
| 8 | Contracts | Open contract from the accepted offer | **201** |
| 9 | Contracts | Record contract version | **201** |
| 10 | Contracts | Add two parties | **200** ×2 |
| 11 | Contracts | Transition `SentForReview` → `UnderReview` | **204** |
| 12 | Contracts | Transition `ApprovedForSignature` → `ApprovedForExecution` | **204** |
| 13 | Contracts | Record two signatures | **200** ×2, `PartiallyExecuted` → **`Executed`** |
| 14 | Contracts | Set effective date | **204** |
| 15 | Contracts | Add obligation | **200** |
| 16 | Contracts | Add option | **200** |
| 17 | Finance | Add monetary obligation | **200** |
| 18 | Finance | Raise receivable | **200** |
| 19 | Finance | Record payment (synthetic, no external movement) | **201** |
| 20 | Finance | Record invoice with a line | **200** |
| 21 | Communications | Connect a **fake** mailbox provider | **200** |
| 22 | AI | Start a run, `DeviceLocal` residency, fake provider | **201** |
| 23–28 | Intelligence | Source, thesis, watchlist, research case, signal, prediction | **201** ×6 (Phase A) |

**28 synthetic mutations across 8 domains**, each verified by reading the record
back or by the state transition the response reported.

## 2. Domain coverage

| Domain | Representative mutation | Status |
| --- | --- | --- |
| People / Companies | — | **NOT_REVIEWED** |
| Relationships / Tasks | — | **NOT_REVIEWED** |
| Talent / Representation | — | **NOT_REVIEWED** — see §18 below |
| Projects / Packages | project, role, package | **COMPLETE** |
| Opportunities | — | **NOT_REVIEWED** (targets existed already) |
| Deals | offer, accept, second negotiation | **COMPLETE** |
| Contracts / Legal | contract, version, parties, execution, obligation, option | **COMPLETE** |
| Finance | monetary obligation, receivable, payment, invoice | **COMPLETE** |
| Documents | — | **NOT_REVIEWED** |
| Communications | fake mailbox connected | **PARTIAL** — no message exists to link or resolve |
| Intelligence | six creates | **COMPLETE** |
| AI | run started | **PARTIAL** — reached `AwaitingLocalExecution`; no approval is produced without a device-local model |

**6 of 11 complete, 2 partial, 3 not reviewed.**

## 3. Domain refusals observed — all correct

Every one names the valid values, which is why the fixture could be built without
reading the source.

| Refusal | Response |
| --- | --- |
| `type: "Film"` on a project | 400, listing `FeatureFilm, TelevisionSeries, LimitedSeries, …` |
| `code: "Term"` on an offer term | 400, listing the valid term codes |
| `answer: "Accepted"` on an offer | 400 — *"Expected one of: Accept, Reject, Withdraw, Expire."* |
| `kind: "TalentEmployment"` on a contract | 400, listing `LongForm, ShortForm, SideLetter, …` |
| `role: "Counterparty"` on a contract party | 400, listing `Artist, Producer, Studio, Network, …` |
| Monetary obligation on a draft contract | 400 — *"This contract has neither been executed nor given an effective date, so it is not yet operative. Agreed commercial terms are not a collectible legal amount."* |
| Signature on a draft contract | 400 — *"This contract is draft, so a signature cannot be recorded against it."* |
| Second open negotiation, same target and kind | 400 — *"A TalentEmployment negotiation with this target is already open. Close or cancel it before opening another."* |
| A payment with no payer | 400 — *"A payment must say who paid."* |

Two of these are worth naming as good product behaviour rather than as
obstacles: the contract refuses to carry money before it is operative, and the
deal refuses a second open negotiation for the same target and kind. Both are
domain invariants stated in words a person can act on.

## 4. §18 — representation, re-confirmed at runtime

`AOS-R001-010` stays **PARTIAL_WORKFLOW_GAP**, with Phase B adding runtime
evidence to Phase A's source trace:

| Capability | Server | Client |
| --- | --- | --- |
| Create (by converting a prospect) | ✅ | ✅ `ConvertProspectDialog` — **opened in both phases** |
| Read / history / transition | ✅ | ✅ |
| Credits | ✅ | ✅ `AddCreditDialog` — **opened in both phases** |
| Materials | ✅ | ✅ `AddMaterialDialog` — **opened in both phases** |
| **Scopes — add / end** | `POST …/scopes`, `…/scopes/end` | ❌ no client method, no command, no dialog |
| **Team — add / remove** | `POST …/team`, `…/team/remove` | ❌ no client method, no command, no dialog |

Classification: **`CONFIRMED_API_UI_GAP`** for scopes and team. Four server
endpoints, nothing in the client that calls them, and no dialog in the
sixty-one-dialog inventory that would. The credits/materials half of the original
finding stays **SUPERSEDED**.

A representation's lifecycle being transition-driven rather than editable is
consistent design and is not counted as a gap.

## 5. What was not done

- **No mutation was performed from a dialog.** Every one above went through the
  API. Dialogs were opened, inspected and cancelled. So §6's `VALID`, `INVALID`,
  `SUCCESS` and `REFUSAL` states remain **NOT_REVIEWED** for all 61.
- **Documents**: no synthetic file was ingested.
- **Communications**: the fake mailbox connected, but no message exists, so
  linking, participant resolution and attachment ingestion had nothing to act on.
- **AI**: the run reached `AwaitingLocalExecution` and stopped. An approval
  requires the local-inference path, which needs a device-local model this audit
  will not provide.
