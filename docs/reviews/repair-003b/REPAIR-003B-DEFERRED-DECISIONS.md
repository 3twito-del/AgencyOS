# Repair Wave 003B — the six fields that were not repaired

§17 and §18. One note per deferred field. **No design is chosen here.** Each note
says what is wrong, what the plausible answers are, and what evidence would settle
it.

---

## The four owner/lead fields

`CreateDealDialog.OwnerIdBox` · `CreateContractDialog.OwnerIdBox` ·
`CreateOpportunityDialog.OwnerIdBox` · `CreatePackageDialog.LeadIdBox`

All four are the same question, so they get one note and must be decided
together — four dialogs answering it differently would be worse than four
dialogs asking it badly.

| | |
| --- | --- |
| **Referenced entity** | An internal user of this organization |
| **Why the raw identifier is bad** | It is a `UserId`. No surface in the client displays one, so the operator cannot know it. The field is required on three of the four, which means the whole dialog is unusable without a value nobody can obtain. |
| **What changed since Audit 002** | The blocker used to be that there was no directory. `GET /organizations/{id}/members` now returns every active member with display name and role, closed by Repair Wave 003E-A. The question is answerable; it was not before. |

**Plausible design A — a picker over the member list.**
Same shape as the fourteen fields this wave repaired: a `ComboBox` of members by
display name and role. Consistent, cheap, and already proven here.
*Against:* it makes "who owns this" a decision on every create, including the
common case where the answer is obviously the person doing the work.

**Plausible design B — default to the acting user, with the picker to change it.**
The field is pre-filled with whoever is signed in and can be changed. Most
creates need no thought; handing work to somebody else stays possible.
*Against:* AgencyOS has no notion of "the acting user" in the client today — the
subject is a development header, and the member list would have to be searched
for the current identity. It also silently assigns ownership, which for a deal or
a contract is a business fact somebody may want to have chosen deliberately.

**Context constraints.**
- `CreateOpportunityDialog`, `CreateDealDialog` and `CreateContractDialog`
  **require** an owner; `CreatePackageDialog` requires a lead. None is optional,
  so "leave it empty" is not among the answers.
- Ownership appears in filters — `GET /opportunities?ownerUserId=` — so whatever
  is chosen is load-bearing beyond the create.
- The member list is organization-scoped and permission-scoped already.

**Evidence that would settle it.** Whether an agency's opportunities are usually
owned by whoever opens them, or routinely assigned to somebody else. That is a
fact about how the owner's agency works and nobody here has it.

---

## `LinkRecordDialog.TargetIdBox`

| | |
| --- | --- |
| **Referenced entity** | Any of **fourteen** kinds: Person, Company, TalentProfile, Material, Project, Package, Opportunity, Submission, Deal, Offer, Contract, ContractVersion, Invoice, Payment |
| **Why the raw identifier is bad** | Same as the rest: the operator files a document or a message against a record and has to know its identifier. |
| **Why it is not repaired here** | Below. |

**The structural problem.** Thirteen of the fourteen kinds have a flat
tenant-scoped list. `ContractVersion` does not — it exists only nested under
`GET /contracts/{id}` → `.Versions`, so it needs a two-level cascade no other kind
needs.

That leaves three shapes, and none is obviously right:

**A — support all fourteen, cascading for `ContractVersion`.**
Complete. *Against:* a kind-keyed table of fourteen endpoints and fourteen label
rules is architecture-by-configuration, which §6 forbids in as many words, and
one arm behaves unlike the other thirteen.

**B — support the thirteen and keep a typed identifier for `ContractVersion`.**
*Against:* §7. An admitted field that still asks for a GUID is not repaired.

**C — reduce the kind list to the ones operators actually file against.**
*Against:* it removes product capability, which is a design decision Audit 002
did not settle.

**The dialog's own documentation is the strongest evidence that this is a design
question rather than an implementation one:**

> The fourteen targets are the ones the database can actually enforce a foreign
> key against.

The list was derived from what the schema can enforce, not from what an operator
does. Whether filing a document against an `Offer` or a `Payment` is a real
workflow has never been asked.

**Evidence that would settle it.** Which kinds are actually linked in practice.
One period of real use would answer it; nothing in the current fixture can.

---

## `AddIntelligenceSubjectDialog.IdBox`

| | |
| --- | --- |
| **Classification** | `DEBUG_ONLY` — unchanged |
| **Verified, not assumed** | `grep -rn "AddIntelligenceSubjectDialog" src/AgencyOS.Windows --include=*.cs` returns the class declaration and its constructor. **Nothing constructs it.** |
| **Corroboration** | Audit 002 lists it among the four unreachable dialogs in `coverage.json`, and the final closure slice left that disposition in place. |

An ordinary operator cannot open this dialog, so there is no operator-facing
raw-identifier defect in it to repair. §18 is explicit that turning it into a
picker would be productising a surface nobody can reach, and that if the
classification is ever shown to be wrong the right response is to stop and
reclassify rather than to alter it.

**It is left exactly as it was.**

---

## `AOS-R002-020` — recorded, not decided

§19. Not this wave's finding and not the same root cause, recorded here only so
the pending decision is not lost.

`ConnectMailboxDialog` offers "Everyone in the organization"; `MailboxVisibility`
declares `Private` and `Shared` only, and the server answers `400`.

- **Option A** — remove the unsupported choice from the dialog.
- **Option B** — add `Organization` to the domain enum and to the rules that
  decide who may read a mailbox.

Option B is an authorization-model change. **Neither is chosen here**, and
nothing in `ConnectMailboxDialog` was touched by this wave.
