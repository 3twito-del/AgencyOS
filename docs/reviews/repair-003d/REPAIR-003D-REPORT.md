# Repair Wave 003D — accessibility and validation association

```
REPAIR WAVE 003D — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

**Wave:** 003D, second of the local post-audit programme
**Admitted:** `AOS-R002-012`; `AOS-R002-011` in part
**Not admitted:** `AOS-R002-010` — owner decision · `AOS-R002-018` — does not reproduce
**Baseline:** executable `ada2310` plus 003C
**Date:** 2026-09-18

[Admission](REPAIR-003D-ADMISSION.md) · [Local work ledger](REPAIR-003D-LOCAL-WORK.md)

---

## What an operator saw, and sees now

| | Before | After |
| --- | --- | --- |
| A person the server refuses to create | **Could not load people** — *firstName must be at most 128 characters.* | **Could not create the person** — *firstName must be at most 128 characters.* |
| A company the server refuses to create | **Could not load companies** — *name must be at most 256 characters.* | **Could not create the company** — *name must be at most 256 characters.* |
| A field with an explanation under it | The explanation is next to the field, and cannot be reached from it | The field declares what describes it |

The first two rows are both measured, through the real Windows client, with the
same probe on both sides: the "before" column is the audit's own captured tree
and the "after" is this wave's.

```
audit   validation.NewPersonDialog  -> [error icon | Could not load people      | firstName must be at most 128 characters.]
003D    read:CommandError           -> [error icon | Could not create the person | firstName must be at most 128 characters.]
```

The sentence never changed. It was already the server's own words, and already
right. Only the heading above it named a different failure.

## `AOS-R002-012` — the wrong thing was named

`PeoplePage.CreatePersonAsync` caught the refusal and wrote it into `ListError`,
the bar the list load uses, whose title is fixed in markup. Each page now has a
bar with no title of its own, titled by whichever command was refused; the list's
bar keeps its load title and keeps reporting loads.

The finding named two sites. A survey found a third: **a single company that
could not be read was reported as the whole directory failing to load.**
Companies had no detail bar at all, so its detail failures went to the list. It
has one now, titled "Could not load this company", which is what `PeoplePage`
already did.

**What is guarded:** a bar whose markup title names a load is written to only
while loading. That rule found the two repaired pages, and cleared a third —
Command Center reports an unconfigured API through a helper called only from its
load, which is a load failure and is correctly titled as one. The rule follows
the call chain rather than reading method names, because reading names alone
would have called that a defect.

## `AOS-R002-011` — the part that needed no decision

No dialog declared `AutomationProperties.DescribedBy` anywhere — 0 of 63. The
product had no shared way to say "this message is about that control".

Five hints already sit under exactly one field and talk about that field. Those
associations are now declared, and the pattern exists in the product:

| Dialog | Field | Hint |
| --- | --- | --- |
| `AddOpportunityTargetDialog` | `ContactBox` | `ContactHint` |
| `AddPackageElementDialog` | `TargetBox` | `TargetHint` |
| `CreateOpportunityDialog` | `SubjectBox` | `SubjectHint` |
| `LinkResearchItemDialog` | `ItemBox` | `ItemHint` |
| `RecordSubmissionDialog` | `MaterialBox` | `SnapshotHint` |

**This does not close the finding.** The manifestation the audit actually drove —
a server refusal — appears at page level after the dialog has closed. There is
nothing inside the dialog to point at, and putting something there would be
deciding `AOS-R002-010` by implementation. That half stays open, and the
dependency is recorded rather than worked around.

Four of the five dialogs were opened live afterwards and their hints read from
the automation tree, because the association runs in the constructor and a
constructor that throws in this product produces a dialog that never appears
rather than an error. `LinkResearchItemDialog` needs a research case selected and
was not driven.

## `AOS-R002-018` — does not reproduce

The finding records the palette announcing `PaletteCommand { Id = go.people, … }`.
Measured on this build:

```
palette rows: 46
rows announcing the record type: 0
offscreen rows (also correct): 36
```

Every captured palette tree in the repository was compared. The two that announce
the record — `run-001` and `run-002-before` — have no node between the container
and its text. Every tree from `run-001r` onward has a `Group` whose class is the
framework's `NamedContainerAutomationPeer`, and announces the command.

That is the mechanism: a template root carrying `AutomationProperties.Name` gets
a peer of its own, and the `ListViewItem` takes its name from that child instead
of falling back to the item's `ToString()`. Naming the root is not the half-fix
the finding describes — it is the fix, and Repair Wave 002's row-label work
reached the palette along with the page lists.

**Nothing in the product was changed.** The finding's suggested repair — an
`ItemContainerStyle` setter — would have been added to something already working.
Two structural tests pin the property instead: all 105 row templates name their
row, and the palette is one of them.

Per §1 this is evidence contradicting an intended repair, and it is reported here
rather than resolved by implementing the change anyway.

## `AOS-R002-010` — not admitted

`ownerDecisionRequired: true`, and §9 keeps it outside this programme. A dialog
that stays open on refusal keeps the operator's typing and holds focus where the
explanation is; it also changes how every dialog in the product reports a server
refusal. `ConnectCompanyDialog` already does it, so the product contains a
precedent — but a precedent is not a decision.

**Nothing about it was changed**, and it blocks the runtime half of
`AOS-R002-011`.

## Local gates

| Gate | Before 003D | After 003D |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,763 | **3,763** |
| Windows | 950 | **968** (+18) |
| Reviewer | 163 | **163** |
| integration (database-free, local) | 13 | **13** |

Integration against `postgres:18.6` is **pending**: it runs in CI, which is not
dispatched during this programme.

## Schema, OpenAPI, contract

No migration, no endpoint, no contract type, no status code, no server change at
all. Every product change is in the Windows client.

## Recorded, not repaired

- **`CompaniesPage`'s detail refusal** could not be driven live: no persona is
  refused a company detail or its timeline, and forcing one would have meant
  destroying LAB data. It is covered structurally.
- **`RecordOfferDialog.TermHintText`** describes a list rather than an input, so
  no association was declared for it.
- **Twenty other dialogs** carry a message bar about a whole submission rather
  than one field. Associating those with any single control would assert
  something false, and they wait on `AOS-R002-010` with the rest.

## Status

```
REPAIR WAVE 003D — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

Not closed: authoritative CI, `postgres:18.6` integration and Nightly are not run
during this programme.

---

## Addendum — `AOS-R002-010` resolved by the owner

**Date:** 2026-09-18, after this wave's report was written. Recorded here rather
than by rewriting the wave: what follows changes the disposition, not the history.

The owner decided `AOS-R002-010`:

> For a recoverable server refusal arising from a mutation initiated inside a
> dialog, **the dialog stays open**, preserving entered values, selected entities,
> operator context and keyboard/focus usability; the message is shown inside the
> dialog and, where it concerns a field, associated with that field.

That removes the blocker recorded above for the runtime half of `AOS-R002-011`,
and both were implemented locally. See
[the programme report](../local-post-audit-repair/LOCAL-POST-AUDIT-REPAIR-REPORT.md)
for the full account.

| Finding | Was | Now |
| --- | --- | --- |
| `AOS-R002-010` | not admitted — owner decision | **OWNER DECISION RESOLVED / LOCAL IMPLEMENTATION COMPLETE** |
| `AOS-R002-011` | partly admitted; refusal half blocked | **LOCAL IMPLEMENTATION COMPLETE** |

### What this changed about `AOS-R002-012`

`AOS-R002-012`'s repair stands and is still used. The page-level bar titled for
what was refused is now the **terminal** path: a refusal the dialog cannot answer
— an invalid session, a parent that is gone — closes the dialog and is reported
there, under the same correct heading. A recoverable refusal no longer reaches the
page at all, because it no longer leaves the dialog.
