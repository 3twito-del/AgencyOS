# Audit 002 Phase C — the four dialogs nothing opens

§15. Reconfirmed, not carried forward.

---

## The reconfirmation

For each, whether any source file constructs it, and everywhere its name appears:

| Dialog | Constructed anywhere in `src/` | Named outside its own two files |
| --- | --- | --- |
| `AddIntelligenceSubjectDialog` | **nowhere** | no (only generated XAML output) |
| `CalculateCommissionDialog` | **nowhere** | no |
| `RaiseReceivableDialog` | **nowhere** | no |
| `RecordMonetaryObligationDialog` | **nowhere** | no |

Each exists as a `.xaml` and a `.xaml.cs`, compiles, and is referenced by nothing
but its own generated partial class and the XAML type table. No page holds one,
no command names one, no test names one.

**This is the same answer Audit 001, Phase A and Phase B gave.** Three
independent passes of the scanner, one of which was rewritten between Phase A and
Phase B after two scanner defects were found, agree.

---

## Why this is not simply "delete them"

Three of the four correspond to server capability that exists and has no other
client surface:

| Dialog | The capability it would drive |
| --- | --- |
| `CalculateCommissionDialog` | commission calculation — the Finance page shows commission rules and results, and has no way to run one |
| `RaiseReceivableDialog` | raising a receivable — receivables are listed and allocated against, and none can be created |
| `RecordMonetaryObligationDialog` | recording a monetary obligation — contracts carry obligations and none can be added from the client |

So the finding is not four unused files. It is **three workflows whose interface
was built, never wired up, and is invisible to anyone reading the running
application.** Someone wrote each dialog deliberately; nothing connects it.

`AddIntelligenceSubjectDialog` is the fourth and is different: its one field is a
typed identifier (`IdBox`, classified `DEBUG_ONLY` in the identifier table), and
what it would add is reachable another way.

---

## Status

`AOS-R002-003` — **CONFIRMED**, unchanged. Phase A filed seventeen dialogs with
no opening control; four of those have no opening *path at all*, which is the
strictly worse case and is what this section is about.

No repair is proposed. Whether to wire these up, remove them, or leave them until
the workflow is designed is a decision for whoever owns the roadmap, and §20
forbids making it here.

## Evidence

- `artifacts/reviewer/run-002-phase-c/dialog-state.json`
- the inventory scan in `artifacts/reviewer/run-002-phase-c/rescan`
