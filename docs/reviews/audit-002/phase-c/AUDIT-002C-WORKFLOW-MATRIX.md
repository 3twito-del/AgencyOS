# Audit 002 Phase C — a real mutation in every major domain

§11 of the Phase C brief, §5 of the original §30 gate. This is the cumulative
position across all three phases, not Phase C alone, because the gate is about
the audit rather than about one pass.

Nothing here is "a surface was opened". Every row is a state change the server
accepted, with the response it gave and how it was verified.

---

## 1. Domain coverage

| # | Domain | Representative mutation | Phase | Result | Verified by |
| ---: | --- | --- | :-: | --- | --- |
| 1 | People / Companies | create a person | C | **201** | read back `200` |
| 1 | People / Companies | create a company | C | **201** | read back `200` |
| 2 | Relationships / Tasks | record an interaction (one participant) | C | **201** | response body |
| 2 | Relationships / Tasks | create a task | C | **201** | response body |
| 3 | Talent / Representation | create a talent profile | C | **201** | response body |
| 4 | Projects / Packages | create project, add role, create package | B | **201** ×3 | read back |
| 5 | Opportunities | create an opportunity | C | **201** | response body |
| 6 | Deals | record offer, accept it, second negotiation | B | **201**, **200**, **201** | `Negotiating` → `TermsAgreed` |
| 7 | Contracts | open, version, parties, two transitions, two signatures, effective date, obligation, option | B | **201**/**200**/**204** ×9 | `PartiallyExecuted` → **`Executed`** |
| 8 | Finance | monetary obligation, receivable, payment, invoice | B | **200**/**200**/**201**/**200** | read back |
| 8 | Finance | record a payment (Incoming, BankTransfer) | C | **201** | response body |
| 9 | Documents | upload a document (multipart) | C | **201** | `documentId`, `versionId`, `contentHash` |
| 10 | Communications | connect a **fake** mailbox provider | B | **200** | read back |
| 11 | Intelligence | source, thesis, watchlist, research case, signal, prediction | A/B | **201** ×6 | read back |
| 11 | Intelligence | record a source | C | **201** | response body |
| 12 | AI | start a run, `DeviceLocal` residency, fake provider | B | **201** | read back |
| — | Membership | add a member | C | **201**, then **400** on a repeat | `GET members` |

**All eleven major domains have a real mutation behind them.** Membership is
listed separately because it is a capability Repair Wave 003E-A created rather
than one of the original eleven.

Cumulative synthetic mutations across the audit: **28 from Phases A and B, plus
9 succeeding in Phase C** — 37, each verified by reading the record back or by
the state transition the response reported.

---

## 2. Every mutation used a real product path

No record in this matrix was written to PostgreSQL directly. Every one went
through the published API with the client identity headers the product requires:

```
X-AgencyOS-Platform: windows-x64
X-AgencyOS-Channel: forge
X-AgencyOS-Client-Version: 0.1.0
X-AgencyOS-Api-Contract: 14
```

The tenant is the FORGE review organization, and every value is synthetic. No
real person, company, project or financial movement is named anywhere in it.

---

## 3. The refusals collected on the way

Seven attempts were refused before succeeding, every one because the probe
guessed a value the domain does not accept. **None is a finding** — each was the
reviewer being wrong — and each refusal named the valid set without the source
having to be read:

```
Type '' is not valid. Expected one of: Meeting, Call, Email, …
Kind 'Casting' is not valid. Expected one of: TalentEngagement, ProjectMarket, …
Method 'Wire' is not valid. Expected one of: BankTransfer, Cheque, Card, Cash, …
Disciplines 'Acting' is not valid. Expected one of: Actor, Writer, …
An interaction must involve at least one participant.
A payment of nothing records a movement that did not happen.
That person is already in this organization. Change their role instead.
```

The last three are the domain speaking rather than a schema complaining. *"A
payment of nothing records a movement that did not happen"* explains why the rule
exists; *"Change their role instead"* says what to do next. This is the standard
the rest of the system's refusals are measured against in `AOS-R002-014`, and it
is set from inside the product.

---

## 4. One refusal that reports a consequence rather than a cause

`POST /opportunities` with no owner:

```
404  Membership '00000000-0000-0000-0000-000000000000' is not an active member
     of this organization.
```

The request omitted `ownerUserId`, so it bound to the default. Noted, **not
filed**: it is the same shape as `AOS-R002-008` and `AOS-R002-014` and adds
nothing new to either.

---

## 5. One finding

`POST /organizations/{id}/documents` is multipart, and any other content type
produces **500**. Isolated rather than assumed — the full table is in
[`AUDIT-002C-MUTATIONS.md`](AUDIT-002C-MUTATIONS.md). Filed as `AOS-R002-017`
(S3).

## Evidence

- `artifacts/reviewer/run-002-phase-c/mutations.json`
- `artifacts/reviewer/run-002-phase-b/` (Phase B sequences)
