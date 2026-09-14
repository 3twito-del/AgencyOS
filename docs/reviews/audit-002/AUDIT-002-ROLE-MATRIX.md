# Audit 002 — role matrix

## The short version

Multi-role review could not be performed, and the reason is a product finding
rather than an audit shortcut.

**AgencyOS has no path to add a second person to an organization.** `User.Register`
is called from exactly one place in the repository — `BootstrapSystemHandler`, the
first-run bootstrap — and `POST /organizations/{id}/memberships` requires a
`userId` that nothing else can produce. There is no dialog, no command, no client
method and no endpoint that creates a user after bootstrap.

So an organization holds exactly one identity: the owner it was created with.
There is no Member, no Observer, no Administrator to review as, and none can be
made through any canonical path. Recorded as `AOS-R002-002`.

The audit did not write users into PostgreSQL to work around this. Manufacturing
an identity the product cannot create would have produced a role matrix
describing a system that does not exist.

---

## What was exercised

| Profile | Available | Exercised | Evidence |
| --- | --- | --- | --- |
| **OWNER** | yes — the bootstrap owner | **COMPLETE** | Every dialog, every workspace, every mutation in this audit ran as `w2-owner`. |
| **ADMINISTRATOR** | no | **BLOCKED** | Role exists in `AgencyRole` (value 3). No identity can hold it. |
| **MEMBER** | no | **BLOCKED** | Role exists (value 2). No identity can hold it. |
| **OBSERVER** | no | **BLOCKED** | Role exists (value 1). No identity can hold it. |
| **RESTRICTED PERMISSION PROFILE** | no | **BLOCKED** | Permissions are granted to memberships; no second membership can exist. |
| **NON-MEMBER** | yes — any unknown subject | **COMPLETE** | See below. |

## Non-member refusal

Seven representative routes, each requested twice: once as the bootstrap owner,
once as a subject the identity store has never seen.

| Route | Owner | Unknown subject |
| --- | --- | --- |
| `people` | 200 | **401** |
| `deals` | 200 | **401** |
| `contracts` | 200 | **401** |
| `documents` | 200 | **401** |
| `intelligence/theses` | 200 | **401** |
| `communication-providers?redirectUri=…` | 200 | **401** |
| `finance/receivables` | 404 | 404 |

The refusal is authentication rather than authorization: `DevelopmentAuthentication`
declines an unknown subject outright, with the comment that an unknown subject
"is not an identity. It is not silently upgraded into one by provisioning a user
on the fly." Nothing leaked: no route answered differently in a way that revealed
whether the organization or its records exist.

`communication-providers` is the route Repair Wave 001.6 closed. It still refuses
a caller who is not a member, which is what that wave established.

`finance/receivables` answers 404 to both, so it says nothing either way about
authorization; it is listed rather than quietly dropped.

## What this does not establish

- Nothing about how the client behaves for a Member or an Observer: whether
  actions are hidden, disabled, or offered and refused on submit. That is §14's
  substance and it is **NOT_REVIEWED**.
- Nothing about permission revocation against an open screen (§15). It needs two
  identities, or one whose grants can be changed, and neither is reachable.
- The M13 residual-risk position is unchanged and unexamined here: content already
  rendered to a workstation cannot be recalled, and no finding in this audit
  claims otherwise.
