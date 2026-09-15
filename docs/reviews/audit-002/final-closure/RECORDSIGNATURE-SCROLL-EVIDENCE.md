# `RecordSignatureDialog` — what was actually wrong

§9, §10 and §11. The brief expected a Reviewer scroll fix. Measuring it first
produced a different answer, and a product finding that was hiding behind the
harness's own shortcoming.

---

## 1. What Phase D recorded

> `RecordSignatureDialog` — **`HARNESS_LIMITATION`** — precondition created and
> verified, and `SignatureButton` is *present in the tree but offscreen on every
> tab*; the pass does not scroll a detail pane into view.

The precondition and the offscreen button are both correct. The conclusion drawn
from them is not.

## 2. The measurement

`TargetReach` classifies a named control into five states and, when something can
scroll it, scrolls it. Run against `SignatureButton` on the Contracts page with a
contract selected:

### At 1600x1000 — the size every previous pass used

```
before      : TARGET_OFFSCREEN_SCROLLABLE
              present with no bounding rectangle, so it is clipped rather than
              scrolled past (bounds none)
after reveal: TARGET_OFFSCREEN_NOT_SCROLLABLE
              nothing in its ancestry could scroll it into view (via ScrollItemPattern)
```

`ScrollItemPattern.ScrollIntoView()` was called on the button itself, and no
ancestor reported `HorizontallyScrollable` or `VerticallyScrollable`. **There is
nothing to scroll.** The button row is a plain horizontal `StackPanel` in
`Grid.Row="1"`; the page's `ScrollViewer`s are inside the tab items below it.

The neighbouring buttons place it exactly. From Phase D's own tree:

| Control | Bounds | Offscreen |
| --- | --- | :-: |
| `NewButton` | `1122,483,161,48` | no |
| `VersionButton` | `1295,483,174,48` | no |
| `ReconcileButton` | `1481,483,125,48` | no |
| `SignatureButton` | **none** | **yes** |
| `NoticeButton` | **none** | **yes** |

`ReconcileButton` ends at x=1606 and the window's right edge is 1642. The two
remaining buttons need about 296px between them — at 1920 they measure 194 and
102 — and have 36.

### At 1920x1080

```
SignatureButton -> TARGET_ONSCREEN   bounds 1617,390,194,48
```

### At 2400x1200

```
SignatureButton -> TARGET_ONSCREEN   bounds 1617,327,194,48
```

**So it is width, and only width.** No scroll would ever have revealed it,
because nothing scrolls.

## 3. What that means

Two separate facts, and Phase D's single label hid both.

**The dialog was never blocked.** `RecordSignatureDialog` has a second opener —
the command palette's `contract.signature.record`, "Record signature". Audit 002
tried the button and classified the dialog on that attempt. With the precondition
present, the palette opens it at 1600x1000 with no harness change whatsoever:

```
OPENED  RecordSignatureDialog  PALETTE  focus-in=yes escape-closed=yes a11y=0
```

So the dialog's disposition was an **audit gap**, not a harness limitation.

**The button is a product finding.** At 1600x1000 two commands — record a
signature, record a notice — cannot be clicked by anybody. They have no bounding
rectangle, nothing scrolls them into view, and there is no overflow affordance.
That is `AOS-R002-021`, filed and not repaired here.

It is not `AOS-R001-013`/`AOS-R001R-001`, which are about the navigation pane:
that pane scrolls, and every destination in it is reachable. This does not scroll
and these are not.

## 4. The dialog, operated (§11)

Reached through the real interface, by the real command, against the real
precondition. Not constructed directly.

**Precondition** — contract `Audit 002B — synthetic engagement`, three required
signatories, none signed:

```
Synthetic Pictures              requiredSignatory=True  signedAt=None
Audit 002D synthetic guarantor  requiredSignatory=True  signedAt=None
Review Agency (synthetic)       requiredSignatory=True  signedAt=None
```

**Opened and worked**:

| | |
| --- | --- |
| Title | Record a signature |
| Opened by | command palette, `contract.signature.record` |
| Initial focus | `ComboBox PartyBox` |
| Focus entered the dialog | yes |
| Focus escaped the dialog | no |
| Tab | 14 stops |
| Shift+Tab | 14 stops, did not escape |
| Buttons | Cancel · Record (disabled) · Signed on |
| Default action | Close |
| Escape closed it | yes |
| Focus restored | yes |
| Accessibility observations | none |

Screenshots: `screenshots/RecordSignatureDialog-open.png`,
`screenshots/RecordSignatureDialog-after-escape.png`.
Tree: `ui-trees/dialog.RecordSignatureDialog.json`.

## 5. What was changed in the Reviewer

`TargetReach` — one class, one file. It takes a control the caller names, says
which of five states it is in, and scrolls it only when something in its ancestry
reports that it can. It discovers nothing and walks nothing.

It is kept even though `RecordSignatureDialog` did not need it, because it is what
produced the product finding: the difference between "the harness did not scroll"
and "nothing can scroll this" is the difference between blaming the instrument and
reporting the product, and no previous pass could tell them apart.

Controls: `TargetReachTests`, six of them.
