# Repair Wave 003A — confirmed correctness, and four dialogs that would not open

**Wave:** AgencyOS Review Repair Wave 003A
**Scope:** `AOS-R002-019`, `AOS-R002-017`. Nothing else.
**Explicitly out of scope:** the three precondition-unachievable dialogs,
`RecordSignatureDialog`, and every 003B–003F item — see §4.
**Branch:** `repair-wave-001`
**Date:** 2026-09-15

Audit 002 is unchanged. Its evidence directories were not written to; this wave's
evidence is under `artifacts/reviewer/run-repair-003a/`, with the matched
pre-repair control in `before/`.

Four workflows could not be started at all — connecting a mailbox, stating a
prediction, recording a source, resolving a prediction — and the operator was
given no feedback of any kind. That is the most consequential thing in any of
Audit 002's open findings, and it is what this wave was for.

---

## 1. Starting product baseline

`6e9b66f` — "An agency can have more than one person in it".

`src/` is byte-identical between `6e9b66f` and the reviewer tip below
(`git diff 6e9b66f..74b09bf -- src` is empty), so reproducing at the tip is
reproducing at the baseline for product code. Confirmed before any measurement,
not assumed.

## 2. Starting Reviewer/audit tip

`74b09bf` — "Correct Phase C's own gate count". Exact-tip CI `34929793549`,
success, both jobs.

## 3. Findings admitted

| Finding | Why admitted |
| --- | --- |
| **`AOS-R002-019`** | The wave's subject. Four dialogs that do not open, no root cause established, `PRODUCT_OPENER_DEFECT` in Phase D. |
| **`AOS-R002-017`** | Admitted after reading the record. See §14. |

## 4. Findings explicitly excluded

Nothing below was touched, and no file belonging to any of them was edited.

| Excluded | Classification |
| --- | --- |
| `ApproveAiActionDialog`, `IngestAttachmentDialog`, `ResolveParticipantDialog` | `PRECONDITION_UNACHIEVABLE` — product-completeness work, not opener defects. No synthetic production capability was invented to close them. |
| `RecordSignatureDialog` | `HARNESS_LIMITATION` — the button exists and is offscreen. **Product layout was not changed to accommodate reviewer automation**, and no generalized scrolling system was built. |
| `AOS-R001-006` | 20 typed-identifier fields across 13 dialogs — 003B |
| `AOS-R001-010` | representation scopes/team API↔UI gap — 003E |
| `AOS-R001-013` | navigation orientation/overflow — 003F |
| `AOS-R002-010/011/012/013` | validation/accessibility family — 003D |
| `AOS-R002-014` | authorization/refusal UX — 003C |
| `AOS-R002-015/016` | navigation/discoverability — 003F |
| `AOS-R002-018` | command palette accessible name — 003D |

**One overlap is recorded rather than repaired.** `AOS-R002-018` is why the audit's
`dialog-runtime` pass could not confirm three of the four repaired dialogs on the
first attempt: the palette row announced its label instead of its bound record, so
the pass declined to press Enter and reported a command it never ran in the same
words as a product that did nothing. The fix for that is in the harness, is
deliberately narrow, and is described in §8 and in
[`REPAIR-003A-RUNTIME.md`](REPAIR-003A-RUNTIME.md). The product-side finding is
untouched.

## 5. `AOS-R002-019` reproduction

All four, on the product baseline, through the real operator path. Full evidence
in [`REPAIR-003A-RUNTIME.md`](REPAIR-003A-RUNTIME.md).

| Dialog | Path used | Result |
| --- | --- | --- |
| `ConnectMailboxDialog` | Communications → Mailboxes, row selected, clicked the product's own "Connect a mailbox" button | **`REPRODUCED`** |
| `CreatePredictionDialog` | Intelligence → Predictions, palette `intelligence.prediction.create` | **`REPRODUCED`** |
| `RecordSourceDialog` | Intelligence → Sources, palette `intelligence.source.record` | **`REPRODUCED`** |
| `ResolvePredictionDialog` | Intelligence → Predictions, prediction selected, palette `intelligence.prediction.resolve` | **`REPRODUCED`** |

In every case the opener was invoked, no dialog appeared, no notice changed —
Communications' standing notice was open before the click and unchanged after it —
and focus did not enter anything. **No divergence: all four reproduce, so they
were not split.**

Preconditions were verified against the running API rather than assumed:
`communication-providers` → 1, `communication-accounts` → 1,
`intelligence/predictions` → 3, `intelligence/sources` → 23, `messages` → 0.

## 6. Root cause

**Established, not assumed.** A control declares its starting state in markup — a
slider's `Value="50"`, a combo box item's `IsSelected="True"` — *and* wires the
event that state raises. The parser calls the handler while `InitializeComponent()`
is still running, against a half-built dialog: controls declared further down do
not exist yet, and the constructor has not assigned its own fields. The handler
dereferences one, throws `NullReferenceException`, the parser rethrows it as
`XamlParseException`, and the fire-and-forget `_ = …Async()` dispatch discards it.

| Dialog | Fires during parse | Handler | Missing |
| --- | --- | --- | --- |
| `ConnectMailboxDialog` | `VisibilityBox` default selection | `Update()` | `_providers` field |
| `CreatePredictionDialog` | `ProbabilitySlider Value="50"` | `ApplyProbability()` | `ProbabilityText` |
| `RecordSourceDialog` | `KindBox` default selection | `ApplyKind()`, `UpdatePrimary()` | `UrlBox`, `CustodyBar`, `PublishedPicker`, `TitleBox` |
| `ResolvePredictionDialog` | `OutcomeBox` default selection | `ApplyScoring()` | `ScoringBar` |

Three independent lines of evidence, in
[`REPAIR-003A-ROOT-CAUSE.md`](REPAIR-003A-ROOT-CAUSE.md): the caught exception and
its stack, a measurement of which control was null when the handler ran, and the
parser's own line and column for three of the four.

## 7. Is the root shared?

**`ONE_SHARED_ROOT`** — one mechanism, four independent instances.

There is no shared helper to fix; each dialog reaches a different piece of
not-yet-existing state. So the repair is four call sites and the shared thing is a
rule, which is now a test. No abstraction was introduced for four call sites.

The repository had already answered this question. Fourteen controls in the client
declare load-time state and wire the event it raises; **the ten in dialogs that
open all carry the guard already**, in the same shape. The four that do not open
are exactly the four that do not guard. The repair adopts the existing convention
rather than inventing one — and, deliberately, does **not** touch the other ten.

## 8. Exact code paths changed

Product, client:

| File | Change |
| --- | --- |
| `src/AgencyOS.Windows/Dialogs/ConnectMailboxDialog.xaml.cs` | `Update()` returns while `_providers` is null |
| `src/AgencyOS.Windows/Dialogs/CreatePredictionDialog.xaml.cs` | `ApplyProbability()` returns while `ProbabilityText` is null |
| `src/AgencyOS.Windows/Dialogs/RecordSourceDialog.xaml.cs` | `ApplyKind()` and `UpdatePrimary()` return while the controls below `KindBox` are null |
| `src/AgencyOS.Windows/Dialogs/ResolvePredictionDialog.xaml.cs` | `ApplyScoring()` returns while `ScoringBar` is null |
| `src/AgencyOS.Windows/Pages/IntelligencePage.xaml.cs` | `ResolvePredictionAsync` separates the composition guard from the selection guard, and answers the second one in words |

Product, server (`AOS-R002-017`):

| File | Change |
| --- | --- |
| `src/AgencyOS.Api/Endpoints/M10Endpoints.cs` | `RequireFormBody` refuses a non-form body with `415` **before** `ReadFormAsync` is called, on both upload routes |

No markup changed. No XAML file was edited in this wave.

Harness (§14-allowed):

| File | Change |
| --- | --- |
| `tools/AgencyOS.Reviewer/Runtime/OpenerProbe.cs` | new: drives one named opener and names the outcome, with before/after capture |
| `tools/AgencyOS.Reviewer/Program.cs` | new `opener-probe` mode |
| `tools/AgencyOS.Reviewer/Runtime/DialogPass.cs` | palette confirmation also accepts a command's exact label **when no other command shares it** |

Tests:

| File | Change |
| --- | --- |
| `tests/AgencyOS.Tests.Windows/Dialogs/LoadTimeHandlerTests.cs` | new: the root-cause rule, over all client markup and code-behind |
| `tests/AgencyOS.Tests.Windows/Dialogs/OpenerRefusalTests.cs` | new: both paths for the four openers |
| `tests/AgencyOS.Tests.Reviewer/OpenerOutcomeTests.cs` | new: the outcome vocabulary, positive and negative controls |
| `tests/AgencyOS.Tests.Reviewer/PaletteMatchTests.cs` | new: controls for the palette match |
| `tests/AgencyOS.Tests.Integration/DocumentTests.cs` | `AOS-R002-017`: three media types, the version route, and a negative control |

## 9. Why the fix is minimal

- No domain-model change, no schema change, no permission-model change.
- No navigation redesign, no dialog-framework rewrite, no new abstraction.
- No markup edited. The declarative default stays where a reader can see it.
- The guard is the one the repository already uses in ten comparable dialogs, so
  the wave adds a convention's enforcement rather than a second convention.
- The other ten dialogs carrying the same markup pattern were **not** touched:
  they are guarded, they open, and changing them would be future-proofing beyond
  the evidence.

**What was deliberately not done.** The silent `_ = …Async()` dispatch is the
second necessary condition for this defect, and there are 218 such sites in the
client. Making every one of them report an unexpected failure would touch every
page, which §4 and §13 forbid. It is recorded in §26 as an observation for a later
wave, and this wave instead removes the *cause* at the four sites and adds a test
that stops the cause returning anywhere.

## 10. ConnectMailbox fake-provider evidence

`FakeCommunicationProvider` only. **No real OAuth, no real mailbox, no external
provider, no send.**

| Check | Result |
| --- | --- |
| Provider discovery works | `GET communication-providers?redirectUri=…` → `200`, one provider: `Fake`, `isConfigured: false`, `authorizationUrl: null`, `scopes: null` |
| Selected mailbox/context preserved | The row "Test Mailbox, Review Owner, Error" was selected before the click and still selected in the captured tree after it |
| Dialog opens when prerequisites exist | Yes — focus lands on `ProviderBox` |
| Missing-provider refusal still produced | **Not drivable here.** `FakeCommunicationProvider` is registered unconditionally, so `providers.Count` is never 0 on this machine. The `Notice("No provider is configured", …)` path is unchanged code and is asserted structurally by `OpenerRefusalTests`. Stated rather than implied. |
| No provider secret surfaced | The opened dialog's tree carries no token, no credential and no secret. The only value shown is the redirect URI the client itself sent. `PrimaryButton` and `ConsentButton` are both **disabled**, because the single provider is not configured — so nothing can be committed |
| No tenant boundary weakened | No change to organization scoping, in the client or the API |

Communications was not redesigned.

## 11. Intelligence semantics preserved

Opener correctness only. Unchanged: `Source ≠ Truth`, `Prediction ≠ Fact`,
research/intelligence authorization, the existing state machines, the existing
audit behaviour. No request shape, no field, no classification and no scoring rule
was altered; the three dialogs build exactly the requests they built before.

The one behavioural addition is a refusal that did not exist: running
`intelligence.prediction.resolve` with no prediction selected now says **"Choose a
prediction first"** instead of returning in silence. That adds no semantics — it
reports the precondition the handler already enforced.

## 12. Negative/precondition tests

| Opener | Valid precondition | Invalid precondition |
| --- | --- | --- |
| `ConnectMailboxDialog` | dialog opens (runtime + `OpenerRefusalTests`) | `providers.Count == 0` → `Notice(…)` — structural, not drivable at run time |
| `CreatePredictionDialog` | dialog opens (runtime) | none beyond page load; asserted as having none |
| `RecordSourceDialog` | dialog opens (runtime) | none beyond page load; asserted as having none |
| `ResolvePredictionDialog` | dialog opens with a prediction selected (runtime) | **runtime**: no selection → "Choose a prediction first", and `ResolvePredictionDialog` does not open |
| `POST /documents` | multipart → `201` | non-form → `415`; multipart missing a field → still `400` |

Every one of these fails on `6e9b66f`, and that was checked by running them
against the baseline rather than asserted:

```
LoadTimeHandlerTests   4 failed / 79 passed   (exactly the four dialogs, each naming its own mechanism)
OpenerRefusalTests     1 failed /  7 passed   (ResolvePredictionAsync returns without telling the operator why)
DocumentTests          4 failed /  1 passed   (415 expected, 500 received; the negative control passed on both)
```

## 13. "Nothing happened" regression coverage

Two layers, both narrow, neither a traversal framework.

**In CI.** `LoadTimeHandlerTests` removes the cause at its source: a handler the
markup can run during load must not reach a name declared later in the document or
an instance field without first declining to run. It reads shipped markup and
code-behind, covers all 63 dialogs and every page, and is deterministic.

**In the harness.** `opener-probe` gives the verdict a vocabulary, so a correct
refusal and a silent failure stop sharing a word:

```
OPENED · REFUSED_WITH_FEEDBACK · DISABLED · MISSING_OPENER · FAILED_WITH_VISIBLE_ERROR
                                    versus
                       INVOKED_NO_OBSERVABLE_OUTCOME
```

`OpenerOutcomeTests` holds a positive and a negative control for each, including
the one that matters most: a notice that was *already open* before the invocation
is not an answer to it. That case is not hypothetical — the Communications page
shows a standing notice, and the baseline run exercises it exactly.

**The probe's notice reader had to be corrected before any of this was true.** Its
first version matched an `InfoBar` by the short class name and read the bar's
`Name`. A running tree reports an open `InfoBar` as a `StatusBar` whose class name
is the fully qualified `Microsoft.UI.Xaml.Controls.InfoBar`, with no accessible
name of its own and its title and message as child elements — so the reader found
no notices anywhere, on any page, and would have reported a correct refusal as the
silence it exists to detect. Caught by reading a captured tree rather than trusting
the code, corrected, and pinned by tests built in the shape the tree actually
produces. The runtime evidence in this report was re-taken afterwards.

One limitation is stated rather than papered over: **UI Automation does not expose
an `InfoBar`'s severity.** The only severity signal in the tree is a standard icon
whose accessible name is in the operating system's display language, which this
harness has already been caught trusting once. So `FAILED_WITH_VISIBLE_ERROR` is
keyed on the title every AgencyOS page gives a failure — a string in this
repository, not one from the shell — and if that copy ever changes the verdict
degrades to `REFUSED_WITH_FEEDBACK`, which is still "the product answered".

**The rule was mutation-tested, not just baseline-tested.** Failing on `6e9b66f`
only proves it recognises four dialogs it was written against. Two mutations were
applied to the repaired tree and reverted:

| Mutation | Result |
| --- | --- |
| Remove the guard from `CreatePredictionDialog.ApplyProbability()` | **fails**, naming `ProbabilityText` |
| Add a *new* dialog with the defect and nothing else — a combo box with a default selection, a handler, and a `TextBlock` below it | **fails**, naming `PickBox.SelectionChanged` and `EchoText` |

The second is the one that matters: the rule catches a dialog it has never seen,
so it is a rule and not a list of four.

## 14. `AOS-R002-017` admission decision

**ADMITTED.** Read from `docs/reviews/audit-002/phase-c/AUDIT-002C-FINDINGS.json`
before any decision, not inferred.

| | |
| --- | --- |
| **Exact symptom** | `POST /api/v1/organizations/{id}/documents` answers `500 "An error occurred while processing your request."` to any content type that is not multipart. Isolated by the audit: an empty multipart body answers `400`, a well-formed multipart missing a field answers `400 "'sensitivity' is required."`, a complete one answers `201`. Only the content type produces the `500`. |
| **Severity** | S3, `ConfirmedDefect`, `Always` reproducible |
| **Root evidence** | `artifacts/reviewer/run-002-phase-c/mutations.json`. Confirmed here: `ReadFormAsync` throws `InvalidOperationException` when the body is not a form, and that exception carries no status, so it fell through to the unhandled path |
| **User impact** | Not reachable from the Windows client, which sends multipart. Reachable by anything else that talks to the API — and a `500` tells a caller to retry a request that will never succeed, while logging their mistake as an integrity failure of ours |
| **Repair category** | Correctness. Server-side, application layer |
| **Dependency** | None. It touches nothing `AOS-R002-019` touches |
| **Belongs in 003A?** | **Yes.** Confirmed correctness defect, low ambiguity, `ownerDecisionRequired: false`, not accessibility or polish, not one of the three precondition-unachievable workflows, not `RecordSignatureDialog`. Phase C's own proposal says it travels with 003A |

**The repair.** `RequireFormBody` checks `HasFormContentType` and refuses with
`415 Unsupported Media Type` **before** the form binder is asked to bind. Repair
Wave 001.5's rule is honoured exactly: the exception is not caught and relabelled,
because catching it would also relabel whatever else it ever means. `415` rather
than `400` because nothing about the request is malformed — the caller's fix is to
send the body as multipart, not to correct a field. Both upload routes read the
body the same way, so both had the defect and both are repaired and asserted.

## 15. Schema change

**NONE.** No migration, no entity, no column.

## 16. OpenAPI change

**NONE.** The document was regenerated and compared against the one produced
before this wave: **identical**. 263 paths, 187 schemas, OpenAPI 3.1.1.

The `415` adds no response metadata, exactly as the endpoint's existing `400`,
`403`, `404` and `413` refusals add none: they are reported by the problem-details
handler rather than declared per route. So the contract does not move, and §20's
"STOP and explain" does not trigger.

## 17. Contract change

**NONE.** API contract version stays `14`.

## 18. Security/tenant regression

| Check | Result |
| --- | --- |
| Correct organization context | Unchanged. No client or server code touching organization scoping was edited |
| Authorization still enforced server-side | Unchanged. `RequireAuthorization(DocumentsWrite)` still runs before the handler, and the `415` is raised inside the handler, after it |
| Non-member cannot exploit the opener | The openers only construct a dialog; every mutation still goes through the API, which refuses a non-member. `AuthorizationTests`, `CommunicationProviderAuthorizationTests` and `MembershipAdministrationTests` all green |
| Inaccessible record does not become visible | Nothing was added to any list, query or projection |
| Wrong-tenant target refused | Unchanged, and covered by the existing tenant-isolation suite |
| No raw secret or provider token leaks | Verified in the captured tree of the opened `ConnectMailboxDialog`: no token, no credential, no secret. §10 |
| Authorization not weakened to make a dialog appear | Nothing in the repair touches authorization. The `415` **strengthens** a refusal: an unauthenticated or unauthorized caller is still refused first, and only an authorized caller reaches the media-type check |

## 19. Unit count

**3,755** — unchanged.

## 20. Windows count

**824**, from 733. `LoadTimeHandlerTests` contributes 83 (one per client markup
file with code-behind), `OpenerRefusalTests` 8.

## 21. Reviewer count

**146**, from 124. `OpenerOutcomeTests` 13, `PaletteMatchTests` 9.

## 22. Integration count

**821 / 821 passed in CI**, from 816. Four new assertions for `AOS-R002-017` —
three content types on the document route and one on the version route — plus one
negative control, all in the existing `DocumentTests`.

A local run showed 818 passed and 3 failed: `BackupRestoreDrillTests`, all three
reporting `pg_dump is not on PATH. AgencyOS backup requires the PostgreSQL 18
client tools.` **This machine has no PostgreSQL 18 client tools.** The same three
fail identically on the unmodified tree — checked rather than assumed — and all
three pass in CI, which settles it: an environment gap here, not a regression.

## 23. `postgres:18.6` evidence

**CI run `35012620677`, job "Integration tests (PostgreSQL 18.6)" — success,
821 of 821**, on the final executable tip `6cc0920`. That is the authoritative
integration evidence for this wave. The earlier run `35008321026` was green on
`bb7f424` with the same counts, before the harness correction in §13.

Local integration runs used PostgreSQL **19beta3** and are LAB evidence only, per
§21. Nothing in this report rests on them.

## 24. TLA+ result

**4 / 4.** `OfflineWriteQueue`, `OutboundSend`, `AiApproval`,
`LocalInferenceLease`. No error found in any, locally and in CI.

### Nightly

Not strictly affected — Nightly runs on a schedule from the default branch, and
this work is on `repair-wave-001` — but it was dispatched on this ref rather than
reasoned about, because the wave touches an API endpoint and the NIGHTLY ring
gates its artifact on the database suite.

**Run `35009570540`: success, both jobs.** Integration tests against
`postgres:18.6`, and "Nightly artifact (Windows)" — the packaged build.

### Gate summary — authoritative, from CI run `35012620677` on `6cc0920`

| Gate | Before | After |
| --- | --- | --- |
| build | 0 warnings / 0 errors | **0 / 0** |
| unit | 3,755 | **3,755** |
| Windows | 733 | **824** |
| reviewer | 124 | **146** |
| integration | green vs `postgres:18.6` | **821 / 821 vs `postgres:18.6`** |
| OpenAPI | 263 paths / 187 schemas | **263 / 187** |
| API contract | 14 | **14** |
| TLA+ | 4 / 4 | **4 / 4** |

## 25. Runtime before/after evidence

[`REPAIR-003A-RUNTIME.md`](REPAIR-003A-RUNTIME.md), with screen captures and
automation trees under `artifacts/reviewer/run-repair-003a/` and the summaries
mirrored into [`evidence/`](evidence/) because `artifacts/` is not in Git.

| Dialog | Before | After |
| --- | --- | --- |
| `ConnectMailboxDialog` | `INVOKED_NO_OBSERVABLE_OUTCOME` | **`OPENED`**, focus in `ProviderBox` |
| `CreatePredictionDialog` | `INVOKED_NO_OBSERVABLE_OUTCOME` | **`OPENED`**, focus in `StatementBox` |
| `RecordSourceDialog` | `INVOKED_NO_OBSERVABLE_OUTCOME` | **`OPENED`**, focus in `KindBox` |
| `ResolvePredictionDialog` | `INVOKED_NO_OBSERVABLE_OUTCOME` | **`OPENED`**, focus in `OutcomeBox` |
| `ResolvePredictionDialog`, nothing selected | `INVOKED_NO_OBSERVABLE_OUTCOME` | **"Choose a prediction first"**, and the dialog correctly does not open |

Audit 002's own evidence directories were not overwritten. This wave wrote only to
`artifacts/reviewer/run-repair-003a/`.

## 26. Audit 002 narrow closure recheck

[`AUDIT-002-NARROW-RECHECK.md`](AUDIT-002-NARROW-RECHECK.md). Four dialog rows,
nothing else. Coverage moves **51 / 59 → 55 / 59** on the same denominator: no
dialog was reclassified and none was moved out of the reachable set.

**Recorded, not repaired**, for a later wave to decide on:

1. **The silent fire-and-forget dispatch.** 218 sites in the client where an
   unexpected exception is discarded without a word. Removing the cause at four
   sites does not remove the class.
2. **`ConnectMailboxDialog` validates `ArgumentNullException.ThrowIfNull(providers)`
   after `InitializeComponent()`**, so a null argument would surface as a parse
   error rather than as itself. Harmless today; wrong order.
3. **The palette row's accessible name is not stable between runs** — sometimes the
   bound record, sometimes the plain label. That is `AOS-R002-018`'s territory and
   is why the harness match needed widening.

## 27. Remaining Audit 002 blockers

| Dialog | Classification |
| --- | --- |
| `ApproveAiActionDialog` | `PRECONDITION_UNACHIEVABLE` |
| `IngestAttachmentDialog` | `PRECONDITION_UNACHIEVABLE` |
| `ResolveParticipantDialog` | `PRECONDITION_UNACHIEVABLE` |
| `RecordSignatureDialog` | `HARNESS_LIMITATION` |

```
AUDIT 002 REMAINS OPEN
```

No §30 closure is claimed.

## 28. Confirmation: no 003B–003F work occurred

None. No file belonging to `AOS-R001-006`, `AOS-R001-010`, `AOS-R001-013`,
`AOS-R002-010`, `AOS-R002-011`, `AOS-R002-012`, `AOS-R002-013`, `AOS-R002-014`,
`AOS-R002-015`, `AOS-R002-016` or `AOS-R002-018` was edited. No dialog outside the
four was changed. No Reviewer traversal system was evolved: no navigation rewrite,
no dialog discovery engine, no automatic scrolling, no manual-gate replacement.

Phase D's `BoundedCapture` and disk-safety gate are untouched, product logging is
unchanged, and the conclusion that the 235 GB incident was audit capture behaviour
rather than AgencyOS product output stands — nothing found in this wave bears on
it.
