# Audit 002 — mutation workflow matrix

What this audit actually executed, and what it did not.

**Everything below is synthetic**, in the disposable review organization
`01a0a015-c44e-7f0e-9c4c-380e118b5e2b` on the `agencyos_w2` database. No real
email, mailbox, AI provider, external party, payment or private document was
touched.

---

## 1. What was executed

Created through canonical API commands, with the client headers the Windows
client sends, so the release-policy and idempotency middleware ran as they do in
the product:

| Workflow | Verb | Result | Persisted |
| --- | --- | --- | --- |
| Intelligence — source | CREATE | **201** | `01a0a10e-2e11-…` read back |
| Intelligence — thesis | CREATE | **201** | `01a0a10e-2f29-…` read back |
| Intelligence — watchlist | CREATE | **201** | `01a0a10e-302e-…` read back |
| Intelligence — research case | CREATE | **201** | `01a0a110-a563-…` read back |
| Intelligence — signal (with evidence) | CREATE | **201** | `01a0a110-36f3-…` read back |
| Intelligence — prediction | CREATE | **201** (UTC) / **500** (local offset) | see `AOS-R002-001` |

Six creates across one domain, verified by reading the records back and by the
Windows client subsequently showing them — the Intelligence dialogs that had no
record to select before these existed opened afterwards.

## 2. Refusals exercised

| What | Expected | Observed |
| --- | --- | --- |
| Write with no client identity headers | refused | **426** "Client update required" |
| Write with an unknown platform | refused | **426** "No release policy is published for platform 'Windows' on ring 'forge'" |
| `kind: "Publication"` on a source | refused with the valid set | **400**, listing `DocumentVersion, Message, ExternalUrl, ManualObservation, …` |
| `kind: "Rumour"` on a signal | refused with the valid set | **400**, listing the eight valid kinds |
| missing `sensitivity` | refused with the valid set | **400**, listing `Internal, Confidential, SourceSensitive, Restricted` |
| read as an unknown subject | refused | **401** on all seven routes probed |
| timestamp with a non-UTC offset | refused or normalised | **500 unhandled** — `AOS-R002-001` |

Every domain refusal named the valid values. That is good refusal UX and it is
what made the fixture work possible without reading the source.

## 3. What was not executed, and why

| Domain | CREATE | EDIT | TRANSITION | CANCEL | CONFLICT | PERMISSION | Why |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Intelligence | **DONE** | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | Creates done; the rest not reached |
| People / Companies | NOT_REVIEWED | NOT_REVIEWED | N/A | N/A | NOT_REVIEWED | N/A | Dialogs opened and cancelled, not submitted |
| Talent / Representation | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | See §9 of the summary |
| Projects / Roles / Packages | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | NOT_REVIEWED | N/A | Dialogs opened and cancelled |
| Opportunities / Targets | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | NOT_REVIEWED | N/A | Dialogs opened and cancelled |
| Deals / Offers | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | NOT_REVIEWED | N/A | Dialogs opened and cancelled |
| Contracts / Rights / Obligations | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | NOT_REVIEWED | N/A | No contract exists in the fixture |
| Finance | NOT_REVIEWED | NOT_REVIEWED | NOT_REVIEWED | N/A | NOT_REVIEWED | N/A | No receivable or invoice exists |
| Documents / Materials | NOT_REVIEWED | NOT_REVIEWED | N/A | N/A | NOT_REVIEWED | N/A | Not reached |
| Communications | NOT_REVIEWED | N/A | N/A | N/A | NOT_REVIEWED | N/A | Fake provider only; not reached |
| AI approvals | NOT_REVIEWED | N/A | NOT_REVIEWED | NOT_REVIEWED | N/A | N/A | No run exists to approve |

**The honest position: this audit executed mutations in one domain of eleven.**
It opened and cancelled dialogs across nine workspaces, which is the §5 gate, but
`§10`'s matrix across major domains is largely `NOT_REVIEWED`, and `§11`'s
persistence checks, `§12`'s double-submit work and `§13`'s conflict work were not
performed at all. Saying so plainly is worth more than a matrix filled in from
inference.

## 4. Idempotency and conflict — not measured

`§12` and `§13` were not exercised.

What the audit can say from the source: **36 of 61 dialogs** send a fresh
`Guid.NewGuid()` idempotency key with their mutation, and 25 do not. Whether the
25 are reads, non-idempotent by design, or a gap is not established here, and the
difference matters too much to guess at.

Optimistic concurrency is present in the contracts — most requests carry an
`expectedVersion` — and no stale-write conflict was provoked.

## 5. Direct database use

Two reads, both for verification, both identified as such:

1. `select * from release_policies` — to find why every write was refused with
   426. It showed platform `windows-x64`, ring `1`, which is what the audit then
   sent.
2. No writes. No row was inserted, updated or deleted by hand. Where a canonical
   path did not exist — adding a second user, `AOS-R002-002` — the audit stopped
   and recorded the gap rather than manufacturing the state.
