# Owner decisions — all resolved

**All four are now decided and implemented locally.** The option analysis below is
kept exactly as it was written, before the decisions were taken: it is the record
of what was weighed, and erasing it would leave the decisions looking obvious in
hindsight.

**Date written:** 2026-09-18 · **Date resolved:** 2026-09-18
**Baseline:** the local tree after 003C–003F and the `AOS-R002-010` work

## The decisions

| Finding | Decision | State |
| --- | --- | --- |
| `AOS-R002-013` | **ACCEPT CURRENT BEHAVIOR — NO FORMAT VALIDATION YET** | resolved · no product change |
| `AOS-R002-014` | **HUMAN CAPABILITY LANGUAGE — MACHINE PERMISSION ID REMAINS IN EXTENSION** | resolved · implemented |
| `AOS-R002-015` | **KEEP ORGANIZATION IN SETTINGS — ADD COMMAND PALETTE ACCESS** | resolved · implemented |
| `AOS-R002-016` | **ENTER COMMITS ORDINARY PRIMARY ACTION — EXPLICIT SAFETY EXCEPTIONS** | resolved · implemented |

`AOS-R002-010` was the fifth, decided and implemented earlier; see
[the programme report](LOCAL-POST-AUDIT-REPAIR-REPORT.md).

What was implemented for each is recorded in §11 and §12 of that report, and the
keyboard convention has its own
[65-dialog inventory](../repair-003g/AOS-R002-016-DIALOG-INVENTORY.md).

---

## The analysis, as it stood before the decisions

## `AOS-R002-013` — an email address the server stores as typed

**Exact current behaviour.** `POST /organizations/{id}/people` with
`email: "not-an-email"` answers **201** and stores the string. The domain checks
length and nothing else: `Email = Ensure.OptionalMax(email, nameof(email), 320)`
in `Person.cs`. The same is true everywhere an address is stored — `User.cs`,
`CommunicationAccount.cs`, `CommunicationMessage.cs`, `OutboundDispatch.cs`, all
`Ensure.NotBlankMax(..., 320)`.

**Exact user-facing problem.** None today, and the finding says so: AgencyOS does
not send email, so nothing fails. The problem is deferred — a later mail
integration inherits whatever is in the column, and discovers it when a send
fails or a match misses.

**Why implementation was held.** `ownerDecisionRequired: true`, confidence
`Observation` rather than a defect, and `"expected": "Not established"`. The
finding's own risk note is the reason: a check added later refuses data that is
already stored, and agency contact records legitimately hold partial and unusual
values.

**Existing product precedent.** **There is no format validation anywhere in
AgencyOS.** `Ensure` has three guards — not blank, maximum length, optional
maximum — and no regular expression appears in the domain at all. The product's
established position is that it stores what it is told and constrains only size.

| | Option A | Option B | Option C |
| --- | --- | --- | --- |
| | Leave it | Validate on write | Mark it, do not refuse it |
| What happens | An address is a string of at most 320 characters, as now | `Ensure.Email` refuses anything without a plausible shape, at every one of the five sites | The address is stored as given; a derived flag or read-side note says it does not look like an address |
| Domain | Unchanged | A new guard kind, and the first format rule in the domain | A new derived read, no new invariant |
| Security | Unchanged | Unchanged | Unchanged |
| Accessibility | Unchanged | A new refusal an operator must be able to correct — which now works, after `AOS-R002-010` | Unchanged |
| Existing rows | Untouched | **Already-stored addresses become unfixable through any write path that revalidates them** | Untouched |
| Files | none | `Ensure.cs`, `Person.cs`, `User.cs`, `CommunicationAccount.cs`, `CommunicationMessage.cs`, `OutboundDispatch.cs` | a read model or projection, plus its surface |
| API/schema/contract | no change | no contract change; behaviour changes from 201 to 400 for some input | no contract change if the flag is not published; **a published flag is a contract change** |

**Recommendation from precedent only.** Precedent points at **A or C**, not B.
Every other field in this domain is constrained by size alone, and B would make
the email field the single exception while leaving four other address-bearing
types to follow or not. If the eventual mail integration is the real motivation,
the decision is better taken then, with that integration's requirements known.

---

## `AOS-R002-014` — a `403` that names an internal permission string

**Exact current behaviour.** `PermissionDeniedException` builds
`$"Permission '{permission}' is required."` — for example *"Permission
'finance.payments.read' is required."* — and the handler publishes the string
again as the `requiredPermission` extension on the problem.

**Exact user-facing problem.** The operator learns they were refused and not what
to do. The sentence names an internal identifier, does not say what was being
attempted, and does not say who could grant it.

**Why implementation was held.** `ownerDecisionRequired: true`. The finding's own
note: *"the wording is a design decision and several are reasonable."* Each option
tells a refused caller a different amount about the organization.

**Existing product precedent.** `AOS-R002-007`, repaired in 003C, is the same
shape and was resolved this way: the sentence was rewritten for the person reading
it — *"This project has changed since you last saw it…"* — while the machine-
readable identifiers stayed on the exception and in the problem's extensions.
Nothing that read `entityId` lost anything. `requiredPermission` already exists
here, so the same split is available.

| | Option A | Option B | Option C |
| --- | --- | --- | --- |
| | Keep the permission string | Describe the capability | Say who could grant it |
| Example | *"Permission 'finance.payments.read' is required."* | *"Reading payments is not part of your role."* | *"Reading payments is not part of your role. An administrator can change that."* |
| For the operator | Precise, quotable to an administrator, meaningless on its own | Says what was refused in words they use | Says what to do next |
| Security | Unchanged | Unchanged | **Tells somebody just refused something about how this organization is administered** — mild, and real |
| Machine readers | Unchanged (`requiredPermission`) | Unchanged | Unchanged |
| Accessibility | Unchanged | Unchanged | Unchanged |
| Files | none | `PermissionDeniedException.cs`, plus a permission→words map | the same, plus whatever decides who can grant |
| API/schema/contract | no change | no contract change; `detail` prose changes | no contract change; `detail` prose changes |

**Recommendation from precedent only.** Precedent points at **B**. It is exactly
what 003C did for `AOS-R002-007`: human sentence, machine data preserved. C adds
a second decision — who counts as able to grant — and that answer is not in the
repository today; the role model would have to be consulted, and saying it aloud
to a refused caller is a disclosure the product has never made.

---

## `AOS-R002-015` — the organization surface is the settings destination

**Exact current behaviour.** `OrganizationPage` is mounted on the
`NavigationView` settings slot (`IsSettingsVisible="True"`), not as a workspace.
It therefore has no entry in `AgencyOsWorkspaces.All` — which lists **17
workspaces**, each with a tag and an access key — no accelerator, and no palette
command. The pane reaches it in the ordinary way the framework provides.

**Exact user-facing problem.** Discoverability, not capability. Somebody who
reaches for the palette to manage members will not find it there. The finding is
explicit that **no operator capability is blocked**, and that the expected
behaviour is *not established as a contract*: `CommandRegistryTests.
EveryWorkspaceHasANavigationCommand` asserts over `AgencyOsWorkspaces.Tags` and
so says nothing about a destination that is not a workspace.

**Why implementation was held.** `ownerDecisionRequired: true`. Whether
membership administration is a workspace or a setting is a statement about what
the product is, not a defect in how it works.

**Existing product precedent.** Two, pulling opposite ways. Every one of the 17
workspaces is a tagged `NavigationViewItem` with an access key and a palette
command — that is the product's shape for a place you work. ADR-0032 decided that
destinations are named rather than numbered, and that **the palette lists only
what it can dispatch**; a settings destination that is not a workspace is
consistent with that, since nothing is advertised that does not work.

| | Option A | Option B | Option C |
| --- | --- | --- | --- |
| | Leave it in settings | Promote it to a workspace | Keep settings, add a palette command that navigates there |
| Palette | absent | present, like the other 17 | present |
| Accelerator | none | a new access key, from the remaining letters | none |
| Pane | settings gear | an 18th destination — and 17 already do not fit (`AOS-R001-013`) | settings gear |
| Security | Unchanged; the page enforces its own permissions either way | Unchanged | Unchanged |
| Accessibility | The gear is named by the operating system's display language, which is why the reviewer finds it by automation id | A named destination, announced in the product's own words | Unchanged for the pane; the palette entry is in the product's words |
| Files | none | `AgencyOsWorkspaces.cs`, `AgencyOsCommands.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` | `AgencyOsCommands.cs`, `MainWindow.xaml.cs` |
| API/schema/contract | no change | no change | no change |

**Recommendation from precedent only.** Precedent points at **C**. It answers the
finding's actual complaint — the palette does not offer it — without adding an
18th item to a pane that already cannot show 17, and without contradicting
ADR-0032, because the command would dispatch somewhere real. B is the tidier
model and makes `AOS-R001-013` measurably worse.

---

## `AOS-R002-016` — `Enter` commits in some dialogs and cancels in others

**Exact current behaviour.** Measured on this tree: **65 dialogs, 28 declare
`DefaultButton="Primary"`, 36 declare `"Close"`, 1 declares `"Secondary"`.** So
`Enter` records an interaction and discards a signal; it creates a project and
cancels a thesis.

**Exact user-facing problem.** A keyboard operator cannot build a habit. The same
gesture saves work in one dialog and throws it away in the next, and nothing
distinguishes them.

**Why implementation was held.** `ownerDecisionRequired: true`, and the risk runs
in the dangerous direction: changing a default changes what an existing habit
does, **towards committing something that used to be discarded**.

**Existing product precedent.** One deliberate case is documented in the finding
itself: `ApproveAiActionDialog`'s primary is *"Approve and run"* and its default
is deliberately **not** that — a considered choice that consequence should not be
one keystroke away. That is a rule, if the owner wants it to be one. The remaining
64 follow no stated rule. Note also that `AOS-R002-010`'s decision has already
made `Enter` safer: a dialog that commits and is refused now keeps everything.

| | Option A | Option B | Option C |
| --- | --- | --- | --- |
| | Leave it | `Enter` always commits, except where consequence is irreversible | `Enter` never commits; commit is an explicit click or `Ctrl+Enter` |
| Dialogs changed | 0 | **36 change** (Close → Primary), minus whatever the exception keeps | **28 change** (Primary → Close) |
| Direction of surprise | none | towards **committing what used to be discarded** | towards discarding what used to commit — an operator loses a keystroke, not data |
| Habit | impossible | learnable, with a stated exception | learnable, no exception needed |
| Accessibility | Unchanged | Unchanged | Unchanged; a second gesture must be documented and reachable |
| Files | none | 36 `.xaml` files, plus a written rule and a guard test | 28 `.xaml` files, plus a gesture and a guard test |
| API/schema/contract | no change | no change | no change |

**Recommendation from precedent only.** Precedent does not choose between B and
C, and it is worth saying so plainly rather than inventing a preference. What
precedent does supply is the shape of the exception — `ApproveAiActionDialog` —
and the evidence that **whichever is chosen should be enforced by a structural
test**, because this split is what an unenforced convention looks like after 65
dialogs. If the owner weights *"no keystroke should be able to commit something
the operator did not mean"* above habit, that is C; if habit above it, that is B
with the irreversible-action exception written down.

---

## What none of these are

None of the four is blocked on anything technical, and none is waiting on further
measurement. Each is a product statement the repository cannot make for itself:

- `AOS-R002-013` — what an address means to this agency.
- `AOS-R002-014` — how much a refusal tells somebody about the organization.
- `AOS-R002-015` — whether administering the agency is a place you work.
- `AOS-R002-016` — what `Enter` means.

---

## What each decision changed

| Finding | Product change | Tests | Where it is recorded |
| --- | --- | --- | --- |
| `AOS-R002-013` | **none** — current semantics accepted | none added; see the note below | this file, and §12 of the programme report |
| `AOS-R002-014` | `PermissionCapability.cs` (new), `PermissionDeniedException.cs` | 11 unit | §12 of the programme report |
| `AOS-R002-015` | `AgencyOsCommands.cs`, `MainWindow.xaml.cs` | 7 unit | §12 of the programme report |
| `AOS-R002-016` | 36 dialog defaults, of 65 classified | 8 Windows | [the 65-dialog inventory](../repair-003g/AOS-R002-016-DIALOG-INVENTORY.md) |

### On `AOS-R002-013` and tests

No test was added, deliberately. A test asserting that `"not-an-email"` is stored
would pin *accidental* behaviour and make the eventual mail integration harder to
build: whoever adds deliverable-address requirements would have to delete a test
that looks like an invariant.

This disposition is **accepted current product semantics**, not a defect left
unfixed. The domain constrains an address by length and nothing else, as it does
every other string, and there is no format rule anywhere in AgencyOS to be
inconsistent with.

**It may be revisited when a real mail-sending capability depends on
syntactically valid, deliverable addresses.** That capability should establish its
own requirements — what it needs to send, what it does with an address it cannot
use — rather than inheriting a guess made before it existed.
