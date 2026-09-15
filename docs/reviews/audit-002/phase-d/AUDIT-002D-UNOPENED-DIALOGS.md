# Audit 002 Phase D — the eight, reconciled

§1 and §9. Phase C's *summary* named two harness limitations and three
inconclusive openers — five of eight. The other three were in Phase C's coverage
document but not in its summary list, which is the arithmetic this reconciles.

The three unaccounted for were `ApproveAiActionDialog`, `ConnectMailboxDialog`
and `RecordSignatureDialog`, all classified `PRECONDITION_NOT_MET` in
`AUDIT-002C-COVERAGE.md`.

**Two of those three classifications were wrong, and Phase D corrects them.**

---

## 1. Phase C's entry state, verified rather than carried

| Dialog | Workspace | Normal opener | Phase C said | Phase D verified |
| --- | --- | --- | --- | --- |
| `ApproveAiActionDialog` | AI | `ApproveButton` | `PRECONDITION_NOT_MET` | **confirmed** — 0 approvals, 0 policies |
| `ConnectMailboxDialog` | Communications | `ConnectButton` / `mailbox.connect` | `PRECONDITION_NOT_MET` | **WRONG** — providers = 1 |
| `RecordSignatureDialog` | Contracts | `SignatureButton` | `PRECONDITION_NOT_MET` | **WRONG** — precondition was absent but is creatable |
| `IngestAttachmentDialog` | Communications | `IngestButton` | `HARNESS_LIMITATION` | **WRONG** — 0 messages exist |
| `ResolveParticipantDialog` | Communications | `message.participant.resolve` | `HARNESS_LIMITATION` | **WRONG** — 0 messages exist |
| `CreatePredictionDialog` | Intelligence | `intelligence.prediction.create` | `INCONCLUSIVE` | resolved |
| `ResolvePredictionDialog` | Intelligence | `intelligence.prediction.resolve` | `INCONCLUSIVE` | resolved |
| `RecordSourceDialog` | Intelligence | `intelligence.source.record` | `INCONCLUSIVE` | resolved |

Phase C's `ConnectMailboxDialog` classification was an **inference from reading
the handler**, not a measurement: the handler shows a notice when
`providers.Count == 0`, and nobody asked the server how many providers there
were. There is one — the `FakeCommunicationProvider`, which
`InfrastructureServiceCollectionExtensions` registers unconditionally and
documents as *"not a test-only stub"*. My earlier probe got a `400` because it
omitted the required `redirectUri` parameter, and I read that as "no providers".

That is the Phase C error this phase exists to catch, and it is mine.

---

## 2. Reachability, revalidated (§2)

A dialog is reachable only if a real product path exists. Re-checked from source
and from the running tree:

| Dialog | Real operator path | Verdict |
| --- | --- | --- |
| `ApproveAiActionDialog` | `ApproveButton` on the AI page's Approvals tab | `REACHABLE_WITH_PRECONDITION` |
| `ConnectMailboxDialog` | **"Connect a mailbox" button, found and clicked on the Mailboxes tab** | `CONFIRMED_REACHABLE` |
| `RecordSignatureDialog` | `SignatureButton`, present in the tree on every Contracts tab | `REACHABLE_WITH_PRECONDITION` |
| `IngestAttachmentDialog` | `IngestButton`, inside a selected message's Attachments tab | `REACHABLE_WITH_PRECONDITION` |
| `ResolveParticipantDialog` | palette `message.participant.resolve`, inside a selected message | `REACHABLE_WITH_PRECONDITION` |
| `CreatePredictionDialog` | palette `intelligence.prediction.create` | `CONFIRMED_REACHABLE` |
| `ResolvePredictionDialog` | palette `intelligence.prediction.resolve` | `CONFIRMED_REACHABLE` |
| `RecordSourceDialog` | palette `intelligence.source.record` | `CONFIRMED_REACHABLE` |

**None was misclassified as reachable.** The denominator stays 59, and no
dialog is moved out of it to make the arithmetic close.

None of the three palette-only dialogs has a keyboard accelerator — checked, all
`gesture: none` — so the palette is genuinely the only path, which is the same
condition the ten working dialogs on that page are under.

---

## 3. Preconditions (§3)

| Dialog | Required state | Exists? | How established |
| --- | --- | --- | --- |
| `ApproveAiActionDialog` | a pending AI approval | **no** | `GET ai/approvals` → 0; `GET ai/policies` → 0 |
| `ConnectMailboxDialog` | ≥1 provider, mailbox list loaded | **yes** | `GET communication-providers?redirectUri=…` → 200, 1 provider, using the client's own redirect URI verbatim |
| `RecordSignatureDialog` | ≥1 outstanding required signatory | **created** | `POST contracts/{id}/parties` with `isRequiredSignatory: true` → 200; outstanding went 0 → 1 |
| `IngestAttachmentDialog` | a message with an attachment | **no** | `GET messages` → 0 |
| `ResolveParticipantDialog` | a message with a participant | **no** | `GET messages` → 0 |
| `CreatePredictionDialog` | none beyond page load | **yes** | `_predictions` is constructed in page init |
| `ResolvePredictionDialog` | a selected prediction | **yes** | `GET intelligence/predictions` → 3, each detail → 200 |
| `RecordSourceDialog` | none beyond page load | **yes** | `_sources` is constructed in page init |

### Why messages cannot be created

`IngestAttachmentDialog` and `ResolveParticipantDialog` both need a message.
Every message route in the API operates on an existing message:

```
GET    /messages
GET    /messages/{id}
POST   /messages/{id}/links
POST   /messages/{id}/participants/{participantId}
DELETE /messages/{id}/links/{linkId}
```

**There is no route that creates an inbound message.** Messages arrive only by
mailbox synchronisation, the only registered provider is the fake one, and it
reports `isConfigured: false`. Creating one would mean writing to PostgreSQL
directly, which §3 forbids.

That is a genuine `PRECONDITION_UNACHIEVABLE`, established from the route table
rather than from a failed attempt.

---

## 4. The attempt hierarchy, and where it stopped (§4)

| Level | What it is | Used |
| :-: | --- | --- |
| 1 | normal automated opener through the real UI | **yes, for all eight** |
| 2 | narrow corrected reviewer opener, mechanism understood first | **yes** — three corrections, below |
| 3 | manual operator opening | **not available** — the reviewer is the only interface driving this machine |
| 4 | reviewer observe-only after manual opening | not reached |

No dialog was constructed directly, no private constructor was called, no
internal method was invoked and counted as coverage.

---

## 5. Final disposition

| Dialog | Disposition |
| --- | --- |
| `ConnectMailboxDialog` | **`PRODUCT_OPENER_DEFECT`** — `AOS-R002-019` |
| `CreatePredictionDialog` | **`PRODUCT_OPENER_DEFECT`** — `AOS-R002-019` |
| `RecordSourceDialog` | **`PRODUCT_OPENER_DEFECT`** — `AOS-R002-019` |
| `ResolvePredictionDialog` | **`PRODUCT_OPENER_DEFECT`** — `AOS-R002-019` |
| `ApproveAiActionDialog` | **`PRECONDITION_UNACHIEVABLE`** — no AI approval exists and none can be created without running an AI action that requests one |
| `IngestAttachmentDialog` | **`PRECONDITION_UNACHIEVABLE`** — no inbound message can be created through any route |
| `ResolveParticipantDialog` | **`PRECONDITION_UNACHIEVABLE`** — same |
| `RecordSignatureDialog` | **`HARNESS_LIMITATION`** — precondition created and verified, and `SignatureButton` is *present in the tree but offscreen on every tab*; the pass does not scroll a detail pane into view |

**8 = 4 + 3 + 1.** No remainder.

---

## 6. The evidence behind `AOS-R002-019`

This is the one that became a product finding, so it is worth being exact.

```
Mailboxes: clicked "Connect a mailbox" — and nothing appeared
```

The reviewer found the product's own button, on the right tab, with a mailbox
row selected, and invoked it. Then, separately, the tab probe ran the palette
command from the same state and captured the tree immediately afterwards:

```
select the Mailboxes tab: selected
rows on the tab: 1 (first: Test Mailbox, Review Owner, Error)
select a row: 'Test Mailbox, Review Owner, Error' is selected
run mailbox.connect: the palette ran it
a dialog appeared: no

ErrorBar present: False
```

`Notice(...)` on that page writes into `ErrorBar` and opens it. The bar is absent
from the tree, so the *"No provider is configured"* path was **not** taken —
which is consistent with the server reporting one provider for the client's exact
redirect URI.

So the handler passed its guard and did not reach its own refusal, and nothing
appeared and nothing was said.

**What is not claimed.** The root cause is not established, and a human operator
has not clicked the button with a mouse. The invocation was UI Automation
`Invoke` on the real control, which raises the same click the product handles,
but it is not the same as a person and the finding says so.

## Evidence

- `artifacts/reviewer/run-002-phase-d/detail/` — the run that named the opener's own reason
- `artifacts/reviewer/run-002-phase-d/mailbox-probe/` — the targeted Mailboxes observation
- `artifacts/reviewer/run-002-phase-d/signature/`, `buttons/`, `palette-fixed/`
