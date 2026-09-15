# Audit 002 Phase C — five people, eleven domains, what each may do

§4 and §6. Phase A and Phase B could not answer this: nothing in the product
could change a membership, so every persona was the same persona. Repair Wave
003E-A made membership a command, and this is the first reading taken with real
roles.

---

## The personas

| Persona | Role | How it was made |
| --- | --- | --- |
| `OWNER` | Owner | the fixture's founding member |
| `MEMBER` | Member | `POST /organizations/{id}/members` |
| `OBSERVER` | Observer | same, then never changed |
| `RESTRICTED` | Administrator | same |
| `AUTH_NON_MEMBER` | — | a real, authenticated user of **another** organization |

The fifth is the one Phase B could not build. Phase B's "non-member" was
unauthenticated, which is a different question; §0 of this phase was opened to
settle whether that mattered, and it did.

---

## The matrix

Each cell is the status the server returned.

| Domain | Verb | Route | Owner | Member | Observer | Administrator | Non-member |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| People | GET | `people` | 200 | 200 | 200 | 200 | **403** |
| People | POST | `people` | 400¹ | 400¹ | **403** | 400¹ | **403** |
| Relationships/Tasks | GET | `tasks` | 200 | 200 | 200 | 200 | **403** |
| Talent/Representation | GET | `talent` | 200 | 200 | 200 | 200 | **403** |
| Projects/Packages | GET | `projects` | 200 | 200 | 200 | **403** | **403** |
| Projects/Packages | POST | `projects` | 201 | 201 | **403** | **403** | **403** |
| Opportunities | GET | `opportunities` | 200 | 200 | 200 | **403** | **403** |
| Deals | GET | `deals` | 200 | 200 | 200 | **403** | **403** |
| Contracts | GET | `contracts` | 200 | 200 | 200 | **403** | **403** |
| Finance | GET | `payments` | 200 | 200 | **403** | 200 | **403** |
| Documents | GET | `documents` | 200 | 200 | **403** | 200 | **403** |
| Communications | GET | `communication-accounts` | 200 | 200 | **403** | 200 | **403** |
| Intelligence | GET | `intelligence/theses` | 200 | 200 | **403** | 200 | **403** |
| Intelligence | POST | `intelligence/sources` | 201 | 201 | **403** | **403** | **403** |
| AI | GET | `ai/runs` | 200 | 200 | **403** | 200 | **403** |
| Membership | GET | `members` | 200 | 200 | 200 | 200 | **403** |

¹ `400 firstName must not be blank.` — the probe's deliberately empty body. The
permission check had already passed, which is the point: a refusal on content is
a different answer from a refusal on authority, and the server gives them in the
right order.

### Four shapes, not four sizes

Observer and Administrator are not "Member with less". They are orthogonal:

- **Observer** reads the representation work — people, projects, opportunities,
  deals, contracts — and is refused finance, documents, communications,
  intelligence and AI.
- **Administrator** is the mirror image: finance, documents, communications,
  intelligence and AI, and refused projects, opportunities, deals and contracts.

That is a real distinction and worth preserving: a bookkeeper and a junior agent
need different things, and neither is a weaker version of the other.

---

## §6 — how a refusal reads

Every refusal in the matrix takes one form:

```
Permission 'people.read' is required.
Permission 'intelligence.write' is required.
Permission 'finance.payments.read' is required.
```

**Assessed against ADR-0038.** The four cases it distinguishes:

| Case | Observed | Correct per ADR-0038 |
| --- | --- | --- |
| Authenticated, no permission, record exists | `403 Permission 'x' is required.` | yes — permission is decided before existence |
| Authenticated, no permission, record absent | `403`, identical wording | yes — indistinguishable from the above, which is the requirement |
| Authenticated, has permission, record absent | `404 Opportunity '…' was not found.` | yes — existence may be disclosed once authority is established |
| Another organization entirely | `403 Permission 'people.read' is required.` | yes |

The cross-tenant reads confirm the fourth row from the outsider's own side:

```
GET .../{other org}/people    403  Permission 'people.read' is required.
GET .../{other org}/deals     403  Permission 'deals.read' is required.
GET .../{other org}/members   403  Permission 'memberships.read' is required.
```

**These are not normalised into one behaviour, and should not be.** §6 is
explicit about that. A 404 after a successful permission check is more useful
than a 403, and a 403 before one leaks nothing. The distinction is load-bearing.

### What is weak about the wording

`Permission 'intelligence.write' is required.` is precise and is written for
whoever is reading the log, not for whoever is at the keyboard. It names an
internal permission string, does not say who could grant it, and does not say
what the person was trying to do.

Recorded as `AOS-R002-014` (S4). No wording is proposed — several are reasonable,
and choosing one is a design decision rather than an audit finding.

---

## Membership is visible to everyone in the organization

`GET members` returns `200` for all four roles including Observer. Every member
can see who else is a member and what role they hold; only Owner and
Administrator can change it.

Examined and **not** filed as a finding. Knowing who your colleagues are is not
privileged information inside an organization, and the alternative — a member who
cannot tell who to ask for access — is worse.

---

## Evidence

- `artifacts/reviewer/run-002-phase-c/role-matrix.json`
- `artifacts/reviewer/run-002-phase-c/non-member.json`
