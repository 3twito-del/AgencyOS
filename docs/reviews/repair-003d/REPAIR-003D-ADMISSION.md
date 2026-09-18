# Repair Wave 003D — admission

**Theme:** accessibility and validation association
**Canonical contents:** `AOS-R002-011` as the product-wide root, plus
`AOS-R002-010`, `AOS-R002-012`, `AOS-R002-018`
**Baseline:** the working tree as 003C left it, executable `ada2310` plus 003C
**Date:** 2026-09-18

The wave map says 003D "needs the owner decision about where a refusal is shown
before the association can point at anything". That is true of part of it and not
of the rest, so each finding is admitted on its own evidence.

---

## What this wave admits

| Finding | Disposition | Why |
| --- | --- | --- |
| `AOS-R002-012` | **Admitted and repaired** | A create the server refused was shown under "Could not load people". Nothing had failed to load. No decision is needed to stop saying so. |
| `AOS-R002-011` | **Partly admitted** | A hint sitting under one field describes that field whatever else is decided. Five such associations are declared. The manifestation the audit drove — a refusal — has nothing inside the dialog to point at yet. |
| `AOS-R002-018` | **Not admitted — does not reproduce** | Measured live: 46 of 46 palette rows announce their command. The recorded evidence predates Repair Wave 002. |
| `AOS-R002-010` | **Not admitted — owner decision** | `ownerDecisionRequired: true`, and §9 keeps it outside this programme. Nothing about it was changed. |

---

## `AOS-R002-018` — measured, not repaired

The finding records a palette row announcing
`PaletteCommand { Id = go.people, Title = Go to People, Category = Navigate,
Shortcut = Ctrl+2 }`, and reads the cause as the name being set on the template's
root rather than on the item container, with an `ItemContainerStyle` setter as
the suggested repair.

**The markup is still exactly as the finding describes it** — `MainWindow.xaml`
line 279 sets `AutomationProperties.Name` on the template's root `Grid`, and
there is no `ItemContainerStyle`. **The symptom is gone anyway.**

```
palette rows: 46
containers announcing PaletteCommand{…}: 0
containers offscreen: 36     (offscreen rows announce their command too)
"PaletteCommand" anywhere in the captured tree: 0
```

Measured at the container, not the template root: every one of the 46
`ListItem` / `ListViewItem` containers carries the command's title.

### Why it no longer happens

Every captured palette tree in the repository, oldest to newest:

| Run | Inner node | First row announces |
| --- | --- | --- |
| `run-001` | *(none)* | `PaletteCommand { Id = go.command-center, … }` |
| `run-002-before` | *(none)* | `PaletteCommand { Id = go.command-center, … }` |
| `run-001r` | `Group` | `Go to Command Center` |
| `run-002` | `Group` | `Go to Command Center` |
| `run-repair-003a` | `Group` | `Go to Command Center` |
| `run-repair-003d-local` | `Group` | `Go to Command Center` |

The two trees that announce the record have **no intermediate node** between the
container and the three text blocks. The ones that announce the command have a
`Group` whose class is `NamedContainerAutomationPeer`. That class is not in this
product — it is what the framework creates for a panel carrying
`AutomationProperties.Name`.

So naming the template root is not a half-measure that leaves the container
wrong. It is what fixes the container: a named root gets a peer, and
`ListViewItem` takes its name from that child instead of falling back to the
item's `ToString()`. Repair Wave 002's row-label work reached the palette along
with the page lists, and the finding's diagnosis describes what happens when the
root is anonymous — which is what `run-001` and `run-002-before` captured.

**The finding was recorded against evidence the repair had already overtaken.**
Phase D cites `run-002-phase-c/final/evidence/`, which contains no palette tree;
the quoted string matches `run-001` and `run-002-before` exactly.

### What was done instead of a repair

Nothing in the product. Two structural tests pin the property that makes it work,
because the audit believed it broken and nothing was stopping it from breaking:
all 105 row templates name their row, and the palette is one of them.

---

## `AOS-R002-011` — what is decision-free, and what is not

The finding has two halves.

**Structural:** no dialog declared `AutomationProperties.DescribedBy` anywhere —
0 of 63 — so the product had no shared way to associate a message with the
control it is about. Labelling was already thorough; the second link was missing.

**Runtime:** three dialogs were driven to a real server refusal and none
associated it.

The runtime half is blocked. Those refusals appear at page level *after* the
dialog has closed, which is `AOS-R002-010` — the owner decision. There is nothing
inside the dialog for a field to point at, and inventing one would be deciding
`AOS-R002-010` by implementation.

The structural half is not blocked where a message already belongs to exactly one
field. Five hints do:

| Dialog | Field | Hint |
| --- | --- | --- |
| `AddOpportunityTargetDialog` | `ContactBox` | `ContactHint` |
| `AddPackageElementDialog` | `TargetBox` | `TargetHint` |
| `CreateOpportunityDialog` | `SubjectBox` | `SubjectHint` |
| `LinkResearchItemDialog` | `ItemBox` | `ItemHint` |
| `RecordSubmissionDialog` | `MaterialBox` | `SnapshotHint` |

Each sits directly beneath its field and talks about that field. Declaring the
association states something already true on screen.

`RecordOfferDialog.TermHintText` was considered and left alone: it describes a
list, not an input. The remaining named bars in 20 other dialogs are about a
whole submission rather than one field, and associating them with any single
control would assert something false.

**This does not close `AOS-R002-011`.** It establishes the pattern the finding
says the product lacks, on the cases that do not depend on a decision.

---

## `AOS-R002-010` — not admitted

`ownerDecisionRequired: true`, and §9 of the programme keeps owner decisions
outside it. The choice is real: a dialog that stays open on refusal keeps the
operator's typing and holds focus where the explanation is, and it changes how
every dialog in the product reports a server refusal.

`ConnectCompanyDialog` already cancels its own close and shows the refusal inline,
so a precedent exists in the product — but a precedent is not a decision, and
adopting it across 63 dialogs is exactly the change the finding's own risk note
describes.

**Nothing about it was changed.** It blocks the runtime half of `AOS-R002-011`,
and that is recorded rather than worked around.
