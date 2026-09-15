# Audit 002 Phase C — a real mutation in every domain

§7. Phase B reached six of eleven domains and said so. This finishes the rest.

Every success is verified by reading the record back where a read endpoint
exists, because a `201` is not evidence that anything persisted.

---

## Results

| Domain | Mutation | Result |
| --- | --- | --- |
| People/Companies | create a person | **201**, read back **200** |
| People/Companies | create a company | **201**, read back **200** |
| Relationships/Tasks | record an interaction | **201** |
| Relationships/Tasks | create a task | **201** |
| Talent/Representation | create a talent profile | **201** |
| Projects/Packages | create a project | **201** |
| Opportunities | create an opportunity | **201** |
| Intelligence | record a source | **201** |
| Finance | record a payment | **201** |
| Membership | add a member | **201** (and **400** on a repeat, correctly) |
| Documents | upload a document | **201** |

With Phase B's Deals, Packaging, Contracts and Communications sequences, **all
eleven domains have a real mutation behind them.**

---

## Every refusal on the way was informative

Seven attempts were refused before succeeding, every one because the probe
guessed a value. Each refusal named the valid set, and none needed the source to
be read:

```
Type '' is not valid. Expected one of: Meeting, Call, Email, …
Kind 'Casting' is not valid. Expected one of: TalentEngagement, ProjectMarket, …
Method 'Wire' is not valid. Expected one of: BankTransfer, Cheque, Card, Cash, …
Disciplines 'Acting' is not valid. Expected one of: Actor, Writer, …
An interaction must involve at least one participant.
A payment of nothing records a movement that did not happen.
That person is already in this organization. Change their role instead.
```

The last three are worth singling out. They are not schema complaints; they are
the domain speaking. *"A payment of nothing records a movement that did not
happen"* explains why the rule exists, and *"Change their role instead"* tells
the caller what to do next. This is the standard the rest of the system's
refusals are being measured against in `AOS-R002-014`, and it is set from inside
the product.

**No finding is filed about any of these.** Every one was the probe being wrong.

---

## One refusal that is not informative

`POST /opportunities` without an owner answered:

```
404  Membership '00000000-0000-0000-0000-000000000000' is not an active member
     of this organization.
```

The request omitted `ownerUserId`, so it bound to the default. The message
reports the consequence — an all-zero identifier is not a member — rather than
the cause, which is that a required field was missing. Noted, not filed: it is
the same shape as `AOS-R002-008` and `AOS-R002-014` and adds nothing new.

---

## One finding

`POST /organizations/{id}/documents` is multipart. Anything else answers **500**.

Isolated rather than assumed:

| Request | Result |
| --- | --- |
| JSON body, `Content-Type: application/json` | **500** An error occurred while processing your request. |
| JSON body, `Content-Type: text/plain` | **500** same |
| empty body, `Content-Type: multipart/form-data` | 400 `An upload carries exactly one file.` |
| multipart with a boundary and no parts | 400 same |
| well-formed multipart, missing a field | 400 `'sensitivity' is required.` |
| well-formed multipart, wrong enum | 400 `Kind 'Note' is not valid. Expected one of: Contract, ContractDraft, …` |
| complete multipart | **201** |

Only the content type produces a 500. Everything else about the endpoint refuses
properly, which is what makes this precise rather than a general complaint.

Filed as `AOS-R002-017` (S3). Not reachable from the Windows client, which sends
multipart; reachable by anything else that talks to the API, and a 500 tells a
caller to retry something that will never work.

Repair Wave 001.5's rule applies to whoever fixes it: **do not catch the
exception and relabel it**. The content type should be refused before the form
binder is asked to bind.

## Evidence

- `artifacts/reviewer/run-002-phase-c/mutations.json`
