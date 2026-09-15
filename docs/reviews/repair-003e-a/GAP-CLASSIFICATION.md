# Repair Wave 003E-A — §0 gap classification

Written before any code, as §0 requires.

**Product baseline:** `51541ea`
**Findings under repair:** `AOS-R002-002`, `AOS-R002-006`

---

## 1. What membership and role capability exists already

| Layer | Capability | State |
| --- | --- | --- |
| **Domain** | `Membership.Grant(org, user, role, grantedBy, now)` | ✅ exists |
| **Domain** | `Membership.Revoke(revokedBy, now)` — sets `Revoked`, stamps who and when, never deletes | ✅ **exists and is unused** |
| **Domain** | `MembershipStatus { Active, Revoked }` | ✅ exists |
| **Domain** | `Membership.EffectivePermissions` — a revoked membership confers nothing | ✅ exists |
| **Domain** | `User.Register(subject, displayName, email, now)` | ✅ exists |
| **Domain** | `User.Suspend` / `Reinstate` / `Deactivate` | ✅ exists |
| **Domain** | `AgencyRole { Observer=1, Member=2, Administrator=3, Owner=4 }` | ✅ exists |
| **Domain** | `RolePermissions.For(role)` — four fixed permission sets | ✅ exists |
| **Permissions** | `memberships.read`, `memberships.grant`, `memberships.revoke` | ✅ all three defined |
| **Application** | `GrantMembershipHandler` | ✅ exists |
| **Application** | revoke handler | ❌ **none** |
| **Application** | list-memberships handler | ❌ **none** |
| **Application** | register-user handler (outside bootstrap) | ❌ **none** |
| **Repository** | `FindByIdAsync`, `FindActiveAsync`, `ListActiveForUserAsync`, `Add` | ✅ exists |
| **Repository** | list by organization | ❌ **none** |
| **API** | `POST /organizations/{id}/memberships` | ✅ exists |
| **API** | any other membership verb | ❌ **none** — no `GET`, `PATCH`, `DELETE` |
| **Contracts** | `GrantMembershipRequest`, `MembershipResponse` | ✅ exist |
| **Client** | any membership method | ❌ **none** |
| **CommandRegistry** | any membership command | ❌ **none** |
| **Windows** | any membership surface | ❌ **none** |

## 2. Server-side capability with no client surface

- `POST /organizations/{id}/memberships` — the one membership endpoint, and the
  Windows client never calls it.
- `memberships.read` is granted to **every** role including Observer, and nothing
  anywhere reads memberships.

## 3. Capability that exists nowhere

- **Listing an organization's members.** No repository method, no handler, no
  endpoint, no client method, no surface. `memberships.read` is a permission
  granted to everyone and exercised by nothing.
- **Revoking a membership.** The domain method is written, tested by nothing, and
  reachable from nothing. `memberships.revoke` is granted to Administrator and
  Owner and exercised by nothing.
- **Registering a user after bootstrap.** `User.Register` is called from exactly
  one place — `BootstrapSystemHandler` — so an organization's population is fixed
  at one at first run.

## 4. The exact operation that blocks each audit state

| Audit profile | Blocked by |
| --- | --- |
| OWNER | not blocked — the bootstrap owner exists |
| ADMIN-LIKE | **no user can be registered**, so no second identity can hold Administrator |
| MEMBER | same |
| OBSERVER | same |
| RESTRICTED | same, plus §4 below |
| NON-MEMBER | not blocked — any unknown subject |

**One missing operation blocks four of six profiles: registering a user.** Granting
a membership is already possible; there is simply nobody to grant one to.

Changing an existing member's role is blocked by a second thing: `GrantMembershipHandler`
refuses when an active membership exists, and nothing revokes.

## 5. What is immutable by design

- **A membership is never edited and never deleted.** The aggregate has no setter
  for `Role`; the comment is explicit that revoking retains the row "so the
  question 'what could this person do last March?' stays answerable".
  **A role change is therefore revoke-then-grant, not an update.** That is the
  existing design and this wave will follow it rather than add a mutable `Role`.
- **Permissions are not per-membership.** `RolePermissions.For(role)` returns one
  of four fixed sets. There is no per-membership permission list and no way to
  express an arbitrary grant.
- **A revoked membership confers nothing**, by construction.

## 6. What is accidental incompleteness

- No list endpoint, though `memberships.read` exists and every role holds it.
- No revoke endpoint, though `Membership.Revoke` and `memberships.revoke` both
  exist.
- No way to register a user, though `User.Register` exists and is complete.
- **No invariant protects the last owner** — see §7.

## 7. A security gap found while classifying, reported before proceeding

**`Membership.Revoke` has no last-owner protection.** Nothing anywhere in the
repository checks whether revoking a membership would leave an organization
without an owner — no domain invariant, no handler check, no database constraint.
Searching for `last owner`, `LastOwner`, `only owner` and `ownerless` across
`src/` returns one unrelated comment about theses.

Today this is unreachable, because nothing calls `Revoke`. **The moment this wave
exposes revocation it becomes reachable**, and an administrator could revoke the
only owner and leave an organization nobody can administer — including nobody who
can grant the owner role back.

Per §3, this is treated as a correctness issue and the invariant is added as part
of this wave. Refusal is preferred over creating an ownerless organization.

The same applies to self-demotion: an owner revoking their own membership is the
same hazard by another route.

## 8. What the change requires

| | |
| --- | --- |
| **Schema change** | **none** — `memberships` and `users` tables already carry every column needed, including `status`, `revoked_at`, `revoked_by` |
| **Domain change** | **minimal and additive** — one invariant guarding the last owner. No new aggregate, no new field, no mutable role |
| **API change** | **additive** — list memberships, revoke a membership, register-and-grant a member |
| **Contract version** | **no bump** — additive paths only; `apiContract` stays 13 unless the current policy requires otherwise |
| **Client change** | **additive** — client methods, three commands, one settings surface |

## 9. Role → audit-profile mapping (§4)

Authorization is **role-based**, not per-membership permission-based. Four roles,
four fixed permission sets, composed in one reviewable table.

| Role | Read | Write | Sensitive read | Administrative | Manage memberships | Manage roles |
| --- | --- | --- | --- | --- | --- | --- |
| **Observer** (1) | working record set only | none | **no** — no finance, no talent notes, no strategy, no deal economics, no contract terms | no | no | no |
| **Member** (2) | broad | broad operational | some — talent notes, deal economics and strategy, contract terms, privileged documents | no | no | no |
| **Administrator** (3) | broad, incl. privileged documents, shared mail, sensitive intelligence | **almost none operational** — people, companies, relationships, interactions, tasks, talent, representation, prospects only | yes | yes — audit read, ledger post, AI administration | **yes** | **yes** |
| **Owner** (4) | everything Member and Administrator hold | everything Member holds | yes | yes, plus archive and release policy | yes | yes |

Mapping onto the audit's profiles:

| Audit profile | Real role | Honest? |
| --- | --- | --- |
| OWNER / ADMIN-LIKE | `Owner`, and `Administrator` as a second shape | ✅ |
| MEMBER | `Member` | ✅ |
| OBSERVER / READ-ONLY | `Observer` | ✅ |
| **RESTRICTED** | `Administrator` | ✅ **truthfully** — it is a genuinely restricted shape: broad read including sensitive material, and almost no operational write. It is not an arbitrary custom profile. |
| NON-MEMBER | any unknown subject | ✅ |

**An arbitrary custom restricted profile cannot be expressed today** and this wave
will not invent one. §4 says to use the strongest truthful existing combinations;
`Administrator` is a real, distinct, restricted permission shape and is used as
the RESTRICTED profile with that stated caveat.

## 10. Scope overlap declared (§13)

One interaction with another finding, declared rather than absorbed:

**A new navigation destination would worsen `AOS-R001-013`.** The pane already
shows 13 of 17 destinations at 1600×1000 and does not follow the selection. Adding
an 18th would make that measurably worse.

So this wave will **not** add a workspace. It will use the `NavigationView`'s own
settings slot — currently `IsSettingsVisible="False"` — which sits in the pane's
footer region rather than in the destination list. That keeps the destination
count at 17 and leaves `AOS-R001-013`'s numbers untouched.

No other finding is touched. `AOS-R001-006`, `AOS-R001-010`, `AOS-R001-020`,
`AOS-R002-001`, `-003`, `-004`, `-005`, `-007`, `-008` and `-009` are out of scope
and will not be repaired here.

## 11. What this wave will build

Minimum to make role state administrable and auditable:

1. **Domain** — a last-owner invariant, refusing a revocation that would leave an
   organization without an active owner.
2. **Repository** — list active memberships for an organization.
3. **Application** — `ListMembershipsHandler`, `RevokeMembershipHandler`,
   `RegisterMemberHandler` (register a user and grant them a membership in one
   authorized command).
4. **API** — `GET /organizations/{id}/memberships`,
   `POST /organizations/{id}/memberships/{membershipId}/revoke`,
   `POST /organizations/{id}/members` (register-and-grant).
5. **Client** — matching methods and three commands.
6. **Windows** — an Organization settings page listing members with a human name,
   role and status, and dialogs to add a member and change or end their access.
   **No raw identifier is typed by the operator** (§6): a member is chosen from
   the list, a role from a labelled selector, and a new person is described by
   name, email and identity-provider subject — which is what an identity provider
   issues, not a GUID AgencyOS mints.

Nothing beyond this.
