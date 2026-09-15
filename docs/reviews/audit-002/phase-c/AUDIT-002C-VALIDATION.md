# Audit 002 Phase C — where a refused entry is reported

§8. Previously `NOT_REVIEWED`: Phase A opened dialogs but never submitted one it
knew the server would refuse.

The question is not whether a message exists. It is whether the message is
attached to the entry it is about, whether attention moves to it, and whether the
entry survives long enough to be corrected.

---

## Two harness errors, corrected before anything was reported

Both would have produced a finding against a product that was behaving correctly.
They are recorded here rather than quietly fixed, because the pattern — a detector
that is confident and wrong — is the reason Audit 001R exists.

### 1. The probe's "invalid" input was valid

The first pass typed a short value into one field, pressed the commit button, and
recorded that six dialogs closed without saying anything. The obvious reading was
a silent failure.

It was not. The server had answered `201`:

```
people      records containing 'review-': 1   review-012956
companies   records containing 'review-': 1   review-012945
```

`NewPersonDialog` shows six fields and the domain requires one. The dialog closed
because the person had been created. Had this been reported, it would have been a
finding about a workflow that worked.

**Corrected** by typing something the server does refuse — 607 characters into a
name, against a documented limit of 128 — and confirming the refusal at the API
first:

```
firstName must be at most 128 characters.        400
name must be at most 256 characters.             400
```

### 2. The refusal reader was looking in the wrong kind of node

With real invalid input, the reader reported that `NewPersonDialog` still said
nothing. Reading the saved tree directly contradicted it:

```
firstName must be at most 128 characters.
```

— present, visible, and in the server's exact words. The reader required a
`Group`, because that is how an `InfoBar`'s frame arrives; the message inside it
is a `Text` node.

**Corrected** in `Detectors.Refusal`, with controls pinning the exact string
(`ValidationDetectorTests.Refusal_ReadsTheMessageAndNotOnlyItsFrame`).

### 3. A closed dialog was being read as a refusal

The classifier treated "the dialog is gone" as "the entry was rejected".
Reproducing `ChangeProjectStageDialog`'s submission against the API:

```
POST projects/{id}/stage   reason of 607 characters   ->  204
```

A reason has no length limit. The dialog closed because the stage had changed.
**Corrected**: a verdict now requires a refusal to have been *observed*, and
`Validation_ADialogThatClosedWithoutARefusalSimplyWorked` pins it.

---

## What the detector was proven against first

Per §16, neither judgement is trusted on a zero. Ten controls, both directions:

| Control | Asserts |
| --- | --- |
| `Prose_ReadsAnInfoBarsComplaint` | a complaint in a group is found |
| `Prose_IgnoresWhatCannotBeSeen` | an offscreen message is not counted |
| `Prose_IsComparableBetweenTwoReadings` | standing guidance is not read as news |
| `Refusal_ReadsTheMessageAndNotOnlyItsFrame` | the real server string is matched |
| `Refusal_IsNotEverythingOnThePage` | page content is not read as a refusal |
| `Validation_ADisabledCommitButtonIsAGate` | a gate is not an error placement |
| `Validation_ADialogThatClosedWithoutARefusalSimplyWorked` | success is not a finding |

---

## Results

39 dialogs were reachable for the probe; 8 could not be reopened during it and
claim nothing.

| Verdict | Count | What it means here |
| --- | ---: | --- |
| `MANUAL_GATE` | **25** | The commit button stays disabled until the form is complete. There is no wrong entry to report, because one cannot be submitted. |
| `UNASSOCIATED` | **3** | The entry was refused, the dialog had already closed, and the refusal appears on the page behind it. |
| `INCONCLUSIVE` | **4** | The probe's entry was accepted, or the field truncated it before it could be refused. Nothing was refused, so nothing is classified. |
| `ASSOCIATED` | **0** | — |
| `VISUALLY_NEAR_ONLY` | **0** | — |

### The three that were refused

| Dialog | What happened | Refusal, verbatim | Entry |
| --- | --- | --- | --- |
| `NewPersonDialog` | closed | `firstName must be at most 128 characters.` | **lost** |
| `NewCompanyDialog` | closed | `name must be at most 256 characters.` | **lost** |
| `AddProjectRoleDialog` | closed | `That did not happen` | **lost** |

In all three, focus returned to the button that opened the dialog — correct in
itself, and it means the operator is looking at the opener while the explanation
is elsewhere on the page.

---

## Why `ASSOCIATED` is not reachable by any dialog

Not a count of what happened to be observed. **No dialog in the product declares
an accessible association at all**:

```
dialog markup files:                                   63
declaring AutomationProperties.DescribedBy anywhere:    0
labelling inputs with Header=:                         60
```

Labelling is thorough — 60 of 61 dialogs with inputs name every field with a
`Header`. What is missing is the second link: nothing points from a field to the
message about it. A screen-reader user who reaches the field is told its name and
nothing about why it was rejected.

This is why proximity was excluded from the classifier by construction. §8 says
not to infer an association from the layout, and here the layout is the only
place one could be inferred from.

---

## Findings

| Id | Severity | Statement |
| --- | --- | --- |
| `AOS-R002-010` | S3 | A refused entry is reported after the dialog has closed, and the typed entry goes with it. Demonstrated on three dialogs; the operator must retype from nothing. |
| `AOS-R002-011` | S3 | **One shared-root accessibility finding**, not sixty. Structurally, no dialog declares an association between a message and the field it concerns (0 of 63), so the product has no shared pattern for it. Three runtime manifestations are confirmed. The rest is a manual gate — see the scope below. |
| `AOS-R002-012` | S4 | On People and Companies a refused **create** is announced under the title *"Could not load people"* / *"Could not load companies"*, which names a different failure from the one that occurred. The message beneath it is correct. |
| `AOS-R002-013` | S4 / observation | `POST /people` accepts `not-an-email` in the email field (`201`). Whether an address should be checked at the boundary is a design question with more than one reasonable answer, so none is proposed. |

### The scope of `AOS-R002-011`, stated exactly

Because §20 forbids reading a static absence as a runtime failure:

| | |
| --- | --- |
| **Structural, proven** | 0 of 63 dialogs declare `AutomationProperties.DescribedBy`. This is a complete reading of the markup and it is product-wide. |
| **Runtime, confirmed** | **3** dialogs were driven to a real server refusal and none associated it. |
| **Manual gate** | **25** dialogs could not be driven to a refusal at all, because their commit button stays disabled until the form is complete. Their behaviour under a screen reader is **unmeasured**, not defective. |
| **Not claimed** | "63 confirmed inaccessible validation errors." The mechanism is absent everywhere; the effect is proven on three. |
| **Also not claimed** | "No accessibility problem." The association mechanism is absent, which is a real structural gap whatever the per-dialog effect turns out to be. |

Whether a screen reader conveys enough without the association is not answerable
from the automation tree. It needs a real screen-reader session and stays
**MANUAL_GATE**.

The 25 gated dialogs are **not** a finding. A form that cannot be submitted
incomplete is a defensible design, and the inconsistency between 25 gated and 3
ungated dialogs was investigated and explained: the three ungated ones have
exactly one genuinely required field, which the gate would have to know about the
server to enforce.

---

## Evidence

- `artifacts/reviewer/run-002-phase-c/validation.json` — the full pass
- `artifacts/reviewer/run-002-phase-c/validation-final/` — the pass after both corrections
- `artifacts/reviewer/run-002-phase-c/validation-markup.json` — the per-dialog markup reading
- `artifacts/reviewer/run-002-phase-c/evidence/validation.*/` — trees and captures, before and after each submission
