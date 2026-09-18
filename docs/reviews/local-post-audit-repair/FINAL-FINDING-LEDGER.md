# Final finding ledger

Every finding and observation raised by Audit 001, Audit 001R, Audit 002, the
repair waves 003A–003F, the owner-decision passes and this final pass — each with
exactly one disposition. **Nothing is left unknown, forgotten or implicitly open.**

**Date:** 2026-09-18 · **Tree:** the cumulative local repair tree
**Authoritative validation:** pending

| Disposition | Count |
| --- | --- |
| `REPAIRED` | **24** |
| `ACCEPTED_CURRENT_BEHAVIOR` | **4** |
| `INTENTIONALLY_UNSUPPORTED` | **2** |
| `DEFERRED_NO_REPRODUCED_DEFECT` | **1** |
| `DESIGN_DECISION_REQUIRED` | **0** |
| Closed by the audit itself (harness error or superseded) | **4** |
| **Total distinct findings** | **35** |

Plus **5 observations** raised by this programme, listed at the end.

---

## Repaired

| Finding | Where | What was done |
| --- | --- | --- |
| `AOS-R001-010` | 003E | Four representation endpoints gained a client caller; proved live, version 4 → 8. |
| `AOS-R001-013` / `AOS-R001R-001` | 003F | The navigation pane follows the selection. Measured offscreen → on screen with the fix disabled and enabled. |
| `AOS-R001-020` | audit | Reclassified `NO_DEFECT`: `sync.now` is an `Invoke`, and the harness expected a navigation. |
| `AOS-R002-001` | 003A.2 | A non-UTC timestamp is normalized once at the API boundary. Re-verified this pass: `09:30+03:00` → `06:30Z`. |
| `AOS-R002-005` | cumulative | 3 of 61 dialogs were named by a test. Now every one of the 65 is exercised — by the raw-identifier guard, the Enter convention guard, eight structural suites and the runtime sweep. |
| `AOS-R002-007` | 003C | The `409` names the kind of record in words and says what to do. |
| `AOS-R002-008` | 003C | A body that could not be read names the field and what was expected. |
| `AOS-R002-010` | owner pass | A recoverable refusal keeps the dialog, its values, its context and its focus. |
| `AOS-R002-011` | 003D + owner pass | Five field↔hint associations, then the refusal association the decision unblocked. |
| `AOS-R002-012` | 003D | A refused create is headed by what was refused. |
| `AOS-R002-014` | owner pass | `"Reading people is not part of your role."` — 78 capabilities, machine id preserved. |
| `AOS-R002-015` | owner pass | A palette command reaches Organization settings; still not an 18th workspace. |
| `AOS-R002-016` | owner pass | `Enter` commits the ordinary primary action; four classified exceptions. |
| `AOS-R002-017` | 003A | `POST /documents` no longer answers `500` to a non-multipart content type. |
| `AOS-R002-019` | 003A.1 | Four dialogs that invoked and showed nothing. |
| `AOS-R002-020` | **this pass** | The unsupported "Everyone in the organization" visibility is gone. |
| `AOS-R002-021` | 003F | Two contract commands that could not be clicked at 1600x1000. |
| `AOS-R002-022` | 003A.1 | The layout cycle that closed the client, at 11 sites. |
| `AOS-R002-023` | 003A.1 | Talent-profile identifiers mapped to person identifiers. |
| `AOS-R002-024` | 003C | Five client surfaces show the server's reason, not the problem's title. |
| `AOS-R002-025` | **this pass** | A missing required nested object is a `400` naming it, not a `500`. |
| `AOS-R002-026` | **this pass** | An instant field offers date **and** time, both editable. |
| `AOS-R002-003` | **owner pass** | The Intelligence workspace gained one visible launcher exposing its 16 authoring commands, built from the command registry; `RecordInvoiceDialog` gained the button its siblings already had. |
| `AOS-R001-006` | 003B + final pass | 20 typed-identifier fields → 19 repaired. Four owner/lead fields became member pickers; twelve of `LinkRecordDialog`'s fourteen kinds became record pickers. |

## Accepted current behaviour

| Finding | Why |
| --- | --- |
| `AOS-R002-013` | Owner decision: an address is bounded by length and not syntax-checked, as every other string in this domain is. Revisitable when a mail capability needs deliverable addresses. |
| `AOS-R002-018` | Does not reproduce: 46 of 46 palette rows announce their command. The recorded evidence predates Repair Wave 002. |
| `AOS-R001-006` — `AddIntelligenceSubjectDialog.IdBox` | Nothing constructs the dialog; the runtime sweep reports it `UNREACHABLE_NOTHING_CONSTRUCTS`. Productising an unreachable dialog for consistency is work nobody can use. |
| Representation with no talent profile | **Reachable after all.** The person stays in the People directory, and the Prospects workspace lists the converted prospect when "Open only" is cleared — measured live, the list grew 2 → 4 and the row read `A, Review Owner, Converted`. |

## Intentionally unsupported

| Finding | Why |
| --- | --- |
| The three unwired finance dialogs | The obligations capability was never built; M13 chose not to advertise a command that dispatches nowhere (ADR-0032). |
| `AOS-R001-006` — `LinkRecordDialog`, two of fourteen kinds | A material belongs to a person and a version to a contract; neither has a list of its own. The kinds are **not removed** — they still take an identifier, and the dialog says why. |

## Deferred, no reproduced defect

| Observation | Evidence |
| --- | --- |
| The 230 fire-and-forget dispatch sites **as a pattern** | Classified, not refactored: 74 loads whose view model owns the error state, 4 background best-effort, 2 openers, 150 command dispatches. No reproduced defect is attributable to the pattern itself — 003A's opener defect was initialization, 003A.1's crash was a layout cycle. **One concrete sub-class was reproduced and repaired** (below); the rest stands. |

## Design decision required

**None.** `AOS-R002-003` was the last, and the owner decided it: the palette stays
an accelerator, and an ordinary authoring capability must have a discoverable
in-product entry point. It is repaired above.

`AOS-R002-002` was closed by 003E-A.

## Closed by the audit itself as harness error

`AOS-R002-004` (focus entry), `AOS-R002-009` (palette command "does nothing"),
and `AOS-R001R-002`/`-003`/`-004`'s reviewer defects. Recorded by Audit 002; not
product defects.

---

## Observations raised by this programme

| # | Observation | Disposition |
| --- | --- | --- |
| 1 | A `404` names the identifier the caller supplied — load-bearing for existence hiding | accepted, noted in 003C |
| 2 | A missing required query parameter answers with the framework's own sentence | recorded |
| 3 | The `contract` gate was not idempotent | **repaired this pass** |
| 4 | A policy-level `403` has no ProblemDetails body, so it reads `"Forbidden"` rather than the capability sentence | **new, recorded** |
| 5 | A `Guarded(...)` dispatch hides the real command from a source scanner | **new, recorded** — it produced two false positives when re-measuring `AOS-R002-003` |

### On observation 4

`AOS-R002-014`'s capability language reaches refusals raised by the application
guard, which produce a problem document. A refusal from the **endpoint
authorization policy** returns an empty `403`, so the client falls back to
`"Forbidden"`. Making the policy emit a problem document is an authorization
pipeline change and was out of scope here.

### On the fire-and-forget sub-class that was repaired

Thirteen page commands called the server inside a dispatched, unawaited task with
no handling. Reproduced as an observer: `CreateThesisDialog` closed, nothing was
created, **no error appeared anywhere**, and focus landed on the navigation pane.
Now guarded on both pages and pinned by a structural test with positive and
negative controls. The other 217 sites were not touched.
