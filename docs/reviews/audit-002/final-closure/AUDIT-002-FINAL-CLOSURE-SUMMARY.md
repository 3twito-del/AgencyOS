# Audit 002 — final closure slice

**Scope:** the four dialogs Audit 002 could not open. Nothing else was run,
re-measured or repaired.

**Product baseline:** `ba077a3` — **unchanged by this slice.**
**Reviewer tip on entry:** `739b020`.

---

## The result

```
AUDIT 002 COMPLETE — NO ADDITIONAL PRODUCT REPAIRS APPLIED IN FINAL CLOSURE
```

Fourteen of fourteen §30 requirements met. **57 of 57 currently reachable dialogs
opened.**

Two of the four were opened. Two were reclassified out of the current reachable
surface after tracing every layer — not for convenience, and not without proof.

| Dialog | Was | Final | Opened? |
| --- | --- | --- | :-: |
| `RecordSignatureDialog` | `HARNESS_LIMITATION` | `AUDIT_FIXTURE_GAP` | **yes** |
| `ResolveParticipantDialog` | `PRECONDITION_UNACHIEVABLE` | `AUDIT_FIXTURE_GAP` | **yes** |
| `ApproveAiActionDialog` | `PRECONDITION_UNACHIEVABLE` | `INTENTIONALLY_EXTERNAL_PRECONDITION` | no |
| `IngestAttachmentDialog` | `PRECONDITION_UNACHIEVABLE` | `INTENTIONALLY_EXTERNAL_PRECONDITION` | no |

**No dialog was found to be a missing current product capability.** That was the
one outcome that would have kept the audit open, and it did not occur.

---

## What each turned out to be

**`ResolveParticipantDialog` was never blocked by a missing capability.** Phase D
established that no route creates an *inbound* message, which is true and answers
a question this dialog does not ask: its guard reads
`ParticipantList.SelectedItem`, and `AddParticipant` has two callers, the second
being AgencyOS sending a message. Connect a mailbox, compose, queue; the
background worker sends it through the fake provider and records the sent message
with its recipients as participants. All canonical routes, no direct database
write, no in-process scripting. Then it opened.

**`RecordSignatureDialog` was never blocked at all.** It has a second opener — the
palette's `contract.signature.record` — which the audit did not try for this
dialog. With the precondition present it opens at 1600x1000 with no harness change
of any kind.

The scroll fix §9 anticipated was **not** what was wrong, and measuring first is
what showed that: `ScrollItemPattern.ScrollIntoView()` was called and no ancestor
of `SignatureButton` reports itself scrollable. Nothing can scroll it. It is
clipped — which turned a harness limitation into a product finding.

**`ApproveAiActionDialog` and `IngestAttachmentDialog` need an external actor.** An
AI approval exists because a model asked to run a write tool; a message attachment
exists because a mailbox synchronisation brought one in. AgencyOS deliberately
exposes no route for either, and adding one would have been fabrication in both
cases — a client-supplied tool request in the first, fabricated correspondence in
the second. Neither was added.

---

## Two new findings, filed and not repaired

Both surfaced from driving real mutations through the real routes, which no
earlier phase had done on these paths.

### `AOS-R002-020` — a mailbox visibility the server refuses

**S3, confirmed, always.** `ConnectMailboxDialog` offers three visibilities:

```xml
<ComboBoxItem Content="Only me"                        Tag="Private" IsSelected="True" />
<ComboBoxItem Content="Members granted mailbox access" Tag="Shared" />
<ComboBoxItem Content="Everyone in the organization"   Tag="Organization" />
```

`MailboxVisibility` has two members, `Private` and `Shared`. Choosing the third:

```
POST …/communication-accounts  {"visibility":"Organization", …}
→ 400  "Visibility 'Organization' is not valid. Expected one of: Private, Shared."
```

An operator connecting a mailbox and choosing the broadest option is refused after
filling the whole form. Only discoverable now, because Repair Wave 003A made the
dialog openable and this slice ran the mutation behind it.

**Not repaired here** — §20 forbids product change in this slice. Client-layer,
low ambiguity; the design question is whether the option or the enum is wrong, and
that is an owner decision.

### `AOS-R002-021` — two contract commands unreachable at 1600x1000

**S3, confirmed, always.** The Contracts detail command bar is a horizontal
`StackPanel` with no scrollable ancestor and no overflow affordance. At
1600x1000:

| Control | Bounds | Offscreen |
| --- | --- | :-: |
| `NewButton` | `1122,483,161,48` | no |
| `VersionButton` | `1295,483,174,48` | no |
| `ReconcileButton` | `1481,483,125,48` | no |
| `SignatureButton` | **none** | **yes** |
| `NoticeButton` | **none** | **yes** |

No bounding rectangle at all, and nothing can scroll them into view. At 1920x1080
`SignatureButton` is at `1617,390,194,48` and fine. Both commands remain reachable
by the command palette, so no workflow is lost — but their buttons cannot be
clicked at a common window size.

Distinct from `AOS-R001-013` / `AOS-R001R-001`, which concern the navigation pane:
that pane scrolls and every destination in it is reachable.

**Not repaired here.** Layout; belongs with wave 003F.

---

## What was changed

**Product code: nothing.** `git diff ba077a3 -- src` is empty for this slice.

**Reviewer:** one class, `TargetReach`, plus a `reach-probe` mode that drives it.
It classifies a named control into five states and scrolls it only when something
reports that it can. Kept even though `RecordSignatureDialog` did not need it,
because it is what produced `AOS-R002-021`: telling "the harness did not scroll"
apart from "nothing can scroll this" is the whole difference between blaming the
instrument and reporting the product.

**A scope judgment, stated plainly.** §9 allowed one narrowly justified
`RecordSignatureDialog` fix. `TargetReach` is that fix — it is what was needed to
know what was wrong, even though the answer was that no fix was needed. A second
capability was built during the slice (a nested-tab opener) and then **removed**,
because the canonical pass turned out to reach the nested tab on its own once the
precondition existed. The Reviewer ends this slice with one new class and one new
mode.

**Tests:** `TargetReachTests`, six.

---

## Validation

Exact-tip CI **`35020654036`** on the final tip `0bb0547` — **success, both jobs.**

| Gate | Before | After |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | **3,755** |
| Windows | 824 | **824** |
| reviewer | 146 | **152** |
| integration | 821 / 821 vs `postgres:18.6` | **821 / 821 vs `postgres:18.6`** |
| OpenAPI | 263 paths / 187 schemas | **263 / 187** |
| API contract | 14 | **14** |
| TLA+ | 4 / 4 | **4 / 4** |

Reviewer +6: `TargetReachTests`. Every other count is unchanged, which is what a
slice that touched no product code should produce.

Interactive dialog evidence is LAB evidence: local PostgreSQL 19beta3, synthetic
tenant, fake providers only. The CI gate is `postgres:18.6` and is separate.

---

## Evidence

| File | What |
| --- | --- |
| `AUDIT-002-FINAL-PRECONDITION-DECISIONS.md` | the three precondition dialogs, traced and classified |
| `AUDIT-002-FINAL-DIALOG-INVENTORY.md` | all 63, one disposition each |
| `AUDIT-002-FINAL-SECTION30.md` | the fourteen requirements, re-scored |
| `RECORDSIGNATURE-SCROLL-EVIDENCE.md` | what was actually wrong, and the dialog operated |
| `reproductions/MESSAGE-WITH-PARTICIPANT.md` | the canonical path Audit 002 missed |
| `runtime.json` | consolidated machine record |
| `screenshots/`, `ui-trees/`, `logs/` | captures |

Audit 002's own directories were not written to. This slice wrote only under
`run-002-final-closure/`.
