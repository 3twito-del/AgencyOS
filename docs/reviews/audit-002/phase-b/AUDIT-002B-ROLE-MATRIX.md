# Audit 002 Phase B — role matrix

§8, §9 and §10. All three remain **BLOCKED**, and Phase B established the reason
more precisely than Phase A did.

---

## Why

Phase A found that no user can be created after bootstrap (`AOS-R002-002`).
Phase B adds the other half: **membership itself is write-once and cannot be read,
changed or revoked** (`AOS-R002-006`).

| | |
| --- | --- |
| Membership verbs in the published contract | `POST` only — no `GET`, `PATCH` or `DELETE` |
| `GrantMembershipHandler` when a membership exists | refuses: *"The user already holds an active membership in this organization."* |
| Ways to create a user after bootstrap | none — `User.Register` is called only by `BootstrapSystemHandler` |

So an organization has exactly one identity, holding exactly one role, and
neither can change. There is no Member, Observer, Administrator or restricted
profile to review as, and the owner's own role cannot be reduced to simulate one.

**The audit did not demote the owner.** With one identity and no revoke path,
that change would have been irreversible and would have ended the audit.

## What was exercised

| Profile | Available | Exercised | Evidence |
| --- | --- | --- | --- |
| **OWNER** | yes | **COMPLETE** | Every dialog, every mutation, every probe in Phases A and B ran as `w2-owner`. |
| **ADMINISTRATOR** | no | **BLOCKED** | `AgencyRole` value 3. No identity can hold it. |
| **MEMBER** | no | **BLOCKED** | `AgencyRole` value 2. |
| **OBSERVER** | no | **BLOCKED** | `AgencyRole` value 1. |
| **RESTRICTED** | no | **BLOCKED** | Permissions attach to memberships; no second membership can exist. |
| **NON-MEMBER** | yes | **COMPLETE** | Any unknown subject. |

## Non-member refusal, and one cross-tenant probe

Seven routes as the owner and as a subject the identity store has never seen:
**200 / 401** on all seven, including `communication-providers`, the route Repair
Wave 001.6 closed. `finance/receivables` answered 404 to both and so says nothing
either way; it is listed rather than quietly dropped.

Phase B added a write against an organization the caller is not a member of:

```
POST /organizations/01a0a015-0000-…/deals  ->  403  "Permission 'deals.write' is required."
```

Refused on permission before existence was considered, which is ADR-0038's rule.
Nothing disclosed whether that organization exists.

## §9 and §10 — not performed

| Question | Status |
| --- | --- |
| Is an action hidden, disabled, or visible and refused, per role? | **BLOCKED** |
| Is the refusal understandable? | **BLOCKED** |
| Does the UI leak the existence of an inaccessible record? | **PARTIAL** — the API does not (403 before existence); the client was not tested per role |
| Does a stale screen keep an action after permission is revoked? | **BLOCKED** — nothing can revoke |
| Does navigation reauthorize? | **BLOCKED** |

The M13 residual-risk position is unchanged and untested here: content already
rendered to a workstation cannot be recalled, and nothing in this audit claims
otherwise.
