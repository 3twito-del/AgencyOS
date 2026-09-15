# Audit 002 Phase D — per-dialog closure evidence

One section per dialog Phase D attempted. Each records the opener that was used,
the precondition that was verified, and what the product did.

Phase D attempted only the eight. The other 51 are settled in
[`../phase-c/AUDIT-002C-DIALOG-MATRIX.md`](../phase-c/AUDIT-002C-DIALOG-MATRIX.md)
and were not re-run.

---

## `ConnectMailboxDialog` — `PRODUCT_OPENER_DEFECT`

| | |
| --- | --- |
| Workspace | Communications, Mailboxes tab |
| Opener | `ConnectButton`, labelled *"Connect a mailbox"*; also `mailbox.connect` |
| Guard | `AppServices.Api is { }`, `_mailboxes is not null`, `providers.Count > 0` |
| Precondition | **verified present** — `GET communication-providers?redirectUri=…` returns `200` with one provider, using the client's own redirect URI (`http://localhost:5173/oauth/callback`) verbatim; `GET communication-accounts` returns one mailbox |
| Level 1 | `clicked "Connect a mailbox" — and nothing appeared` |
| Level 2 | palette by identifier: `typing "Connect mailbox" left "Connect mailbox" (2 shown) at the top — and nothing appeared` |
| After | `ErrorBar present: False` — the page's `Notice(...)` writes into `ErrorBar` and opens it, so the *"No provider is configured"* path was **not** taken |
| Opened | **no** |

---

## `CreatePredictionDialog` — `PRODUCT_OPENER_DEFECT`

| | |
| --- | --- |
| Workspace | Intelligence, Predictions tab |
| Opener | `intelligence.prediction.create` — no button, no accelerator |
| Guard | `_api is null \|\| _predictions is null` |
| Precondition | **verified present** — `_predictions` is constructed during page init and the Predictions tab rendered three rows |
| Level 1/2 | `Predictions: ran but nothing appeared`, on the tab whose guard is satisfied |
| Opened | **no** |

Ten of thirteen sibling dialogs on the same page open through the same palette
mechanism, including `RecordForecastDialog`, which guards on the *same list*.

---

## `RecordSourceDialog` — `PRODUCT_OPENER_DEFECT`

| | |
| --- | --- |
| Workspace | Intelligence |
| Opener | `intelligence.source.record` — no button, no accelerator |
| Guard | `_api is null \|\| _sources is null` |
| Precondition | **verified present** — `_sources` is constructed during page init; the Sources tab rendered 13 rows |
| Level 1/2 | ran on **all nine** Intelligence tabs; nothing appeared on any of them |
| Independent check | the tab probe, separate machinery, agrees: *"run intelligence.source.record: the palette ran it / a dialog appeared: no"* |
| Opened | **no** |

This one has no tab-dependent or selection-dependent guard at all. There is no
state in which it should decline to open.

---

## `ResolvePredictionDialog` — `PRODUCT_OPENER_DEFECT`

| | |
| --- | --- |
| Workspace | Intelligence, Predictions tab |
| Opener | `intelligence.prediction.resolve` |
| Guard | `_api`, `_predictions`, `PredictionList.SelectedItem` |
| Precondition | **verified present** — three predictions exist, each `GET intelligence/predictions/{id}` returns `200`, and the probe confirmed a row selected |
| Level 1/2 | `Predictions: ran but nothing appeared`, `Watchlists: ran but nothing appeared` |
| Opened | **no** |

The handler awaits `GetPredictionAsync` before constructing the dialog; that call
was tested directly and returns `200` for every prediction in the tenant.

---

## `ApproveAiActionDialog` — `PRECONDITION_UNACHIEVABLE`

| | |
| --- | --- |
| Workspace | AI, Approvals tab |
| Opener | `ApproveButton` |
| Guard | `_approvals?.Selected is { } approval` |
| Precondition | **absent** — `GET ai/approvals` → 0, `GET ai/policies` → 0 |
| Can it be created? | Not directly. The approval routes are `GET /approvals`, `GET /approvals/{id}` and `POST /approvals/{id}/decision`; an approval exists only because an AI run requested a tool that needs one |
| Opened | **no**, and correctly so — there is nothing to approve |

---

## `IngestAttachmentDialog` — `PRECONDITION_UNACHIEVABLE`

| | |
| --- | --- |
| Workspace | Communications, a selected message's Attachments tab |
| Opener | `IngestButton` |
| Guard | `_messageDetail is not null` **and** `AttachmentList.SelectedItem` |
| Precondition | **absent** — `GET messages` → 0 |
| Can it be created? | **No.** Every message route operates on an existing message; none creates one. Messages arrive only by mailbox synchronisation, the sole registered provider is the fake one, and it reports `isConfigured: false` |
| Opened | **no** |

The two-level tab walk did reach `Messages > Attachments` in Phase D, so the
navigation limitation Phase C recorded is gone. What remains is that there is
nothing to select.

---

## `ResolveParticipantDialog` — `PRECONDITION_UNACHIEVABLE`

Identical in shape to the above: guards on `_messageDetail` and
`ParticipantList.SelectedItem`, and no message exists or can be created.

---

## `RecordSignatureDialog` — `HARNESS_LIMITATION`

| | |
| --- | --- |
| Workspace | Contracts |
| Opener | `SignatureButton`, labelled *"Record signature"* |
| Guard | `_detail?.Contract is { }` **and** `OutstandingSignatories.Count > 0` |
| Precondition | **created and verified** — the tenant's only contract was `Executed` with both parties signed, so Phase D added one required signatory through the canonical route: `POST contracts/{id}/parties` with `role: "Guarantor"`, `isRequiredSignatory: true` → `200`. Outstanding signatories went **0 → 1** |
| Level 1 | `"Record signature" was present but not on screen; SignatureButton was present but not on screen` — on **every one of the ten Contracts tabs** |
| Level 2 | the guard short-circuit was corrected so button openers are always tried; the button is still offscreen |
| Opened | **no** |

The control exists in the automation tree and is never on screen. It lives in the
contract detail pane, and the pass does not scroll a pane into view before
reaching for a control inside it.

**That is the reviewer's shortcoming, not the product's**, and it is recorded as
such. A human operator would scroll.

## Evidence

- `artifacts/reviewer/run-002-phase-d/detail/dialog-runtime.json`
- `artifacts/reviewer/run-002-phase-d/mailbox-probe/`
- `artifacts/reviewer/run-002-phase-d/{buttons,signature,palette-fixed}/`
