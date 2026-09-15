# Repair Wave 003E-A — membership, roles and audit-unblocking completeness

**Starting baseline:** `51541ea`
**Findings repaired:** `AOS-R002-002`, `AOS-R002-006`
**Date:** 2026-09-15

The §0 classification written before any code is
[`GAP-CLASSIFICATION.md`](GAP-CLASSIFICATION.md).

---

## 1. What was wrong

### `AOS-R002-002` — exact pre-fix state

`User.Register` existed, was complete, and was called from **one place in the
whole repository**: `BootstrapSystemHandler`, the first-run path. No endpoint, no
command, no client method and no dialog created a user afterwards. An
organization's population was therefore fixed at one person at first run.

`POST /organizations/{id}/memberships` existed and required a `userId` that
nothing could produce, so the one membership endpoint could never be used.

### `AOS-R002-006` — exact pre-fix state

Membership was write-once:

- **One verb** in the published contract — `POST` — with no `GET`, `PATCH` or
  `DELETE`.
- `GrantMembershipHandler` refused when an active membership existed:
  *"The user already holds an active membership in this organization."*
- `Membership.Revoke` existed in the domain, was called by nothing, and was
  reachable from nothing.
- `memberships.read` was granted to **every** role including Observer, and
  exercised by nothing.

So a role could not be read, changed or ended, and nobody could be added.

### A third problem, found while classifying

**No invariant protected the last owner.** Nothing in `src/` checked whether
revoking would leave an organization ownerless. It was unreachable only because
nothing called `Revoke` — exposing revocation would have made it reachable, and an
organization with no owner is one nobody can administer again, because granting
the owner role requires being one.

Reported in §7 of the classification before implementation, and fixed here.

---

## 2. Canonical role and permission inventory

Authorization is **role-based**, not per-membership permission-based:
`RolePermissions.For(role)` returns one of four fixed sets.

| Role | Read | Operational write | Sensitive read | Administrative | Manage memberships |
| --- | --- | --- | --- | --- | --- |
| Observer (1) | working record set | none | **no** — no finance, talent notes, strategy, deal economics or contract terms | no | no |
| Member (2) | broad | broad | some | no | no |
| Administrator (3) | broad incl. privileged documents, shared mail, sensitive intelligence | **almost none** | yes | yes — audit, ledger post, AI administration | **yes** |
| Owner (4) | Member ∪ Administrator | Member's | yes | yes + archive, release policy | **yes** |

**An arbitrary custom restricted profile cannot be expressed**, and none was
invented. `Administrator` is used as the RESTRICTED audit profile because it is a
genuinely distinct restricted shape — and the persona probe proves it: an
Administrator is refused `GET /deals` where a Member and even an Observer are
allowed.

---

## 3. What already existed, and what was missing

| | Already existed | Missing |
| --- | --- | --- |
| **Domain** | `Grant`, `Revoke`, `MembershipStatus`, `EffectivePermissions`, `User.Register`, four roles, permission table | last-owner invariant |
| **Permissions** | `memberships.read/grant/revoke` — all three defined | nothing |
| **Application** | `GrantMembershipHandler` | list, revoke, add-member, change-role |
| **Repository** | find by id, find active, list for user, add | list for organization |
| **API** | `POST /memberships` | list, add, revoke, change role |
| **Contracts** | `GrantMembershipRequest`, `MembershipResponse` | member, add, change-role shapes |
| **Client** | nothing | four methods |
| **Windows** | nothing | the whole surface |

---

## 4. What changed

| | |
| --- | --- |
| **Schema** | **none.** `memberships` and `users` already carried every column, including `status`, `revoked_at`, `revoked_by`. No migration. |
| **Domain** | One invariant. `Membership.Revoke` now takes `otherActiveOwners` and refuses when the membership is an owner's and that count is zero. No new aggregate, no new field, no mutable role. |
| **API** | Four additive paths: `GET /organizations/{id}/members`, `POST …/members`, `POST …/members/{membershipId}/revoke`, `POST …/members/{membershipId}/role`. |
| **Contract** | **13 → 14.** Every previous additive slice incremented, and `MinimumSupported` stays 1. `build/Version.props` moved with it. |
| **OpenAPI** | **260 → 263 paths, 185 → 187 schemas.** |
| **Client** | Four methods. No new command and no new palette entry. |
| **Windows** | One page, two dialogs, and the `NavigationView` settings slot enabled. |

### Declared scope interaction (§13)

**No navigation destination was added.** The pane already shows 13 of 17
destinations at 1600×1000 and does not follow the selection (`AOS-R001-013`); an
eighteenth would have made that measurably worse. The Organization screen lives in
the `NavigationView`'s own settings slot, which sits in the pane's footer region
rather than in the destination list. **The destination count is still 17.**

No other finding was touched.

---

## 5. Owner safety

| Question | Answer | Proved by |
| --- | --- | --- |
| Can the last owner remove themselves? | **No** — 400, *"This is the organization's only owner. Give somebody else the owner role first, then end this membership."* | `TheOnlyOwner_CannotBeRemoved` |
| Can the last owner demote themselves? | **No** — a role change is a revocation, so the same rule bites | `TheLastOwner_CannotBeDemoted` |
| Can an admin remove the only owner? | **No** — the rule is about the organization, not the caller | same domain invariant |
| Once there is a second owner? | **Yes**, the first may leave | `OnceThereIsASecondOwner_TheFirstCanLeave` |
| Is the last member protected? | **No, deliberately** — an organization with one owner and nobody else is a small agency, not a broken state | `TheLastOfAnyOtherRole_CanBeRevoked` |

## 6. Privilege escalation

| Attempt | Result | Proved by |
| --- | --- | --- |
| Administrator creates an owner | **403** | `AnAdministrator_CannotMakeAnOwner` |
| Administrator promotes somebody to owner | **403** | `AnAdministrator_CannotPromoteToOwner` |
| Administrator builds a team below themselves | **201**, allowed | `AnAdministrator_CanAddAMember` |
| Member adds anybody, at any role | **403** ×4 | `AMember_CannotAddAnybody` |
| Member changes a role | **403** | `AMember_CannotChangeARole` |
| Observer ends a membership | **403** | `AnObserver_CannotEndAMembership` |
| Observer reads the list | **200**, allowed — every role holds `memberships.read` | `AnObserver_CanReadTheMembersList` |

Only an owner may create an owner. That check sits in the handler, applied to both
the add and the change-role paths.

## 7. Cross-tenant

| Attempt | Result | Proved by |
| --- | --- | --- |
| Read another organization's members | **403** | `ACaller_CannotReadAnotherOrganizationsMembers` |
| Add yourself to another organization | **403** | `ACaller_CannotAddThemselvesToAnotherOrganization` |
| Revoke a real membership through *your* organization's route | **404** | `AMembership_CannotBeRevokedThroughTheWrongOrganization` |
| Stranger lists or mutates | **401** ×2 | `ANonMember_CannotListOrChangeMembership` |

The third matters most: without the handler's check that the membership belongs to
the organization the caller was authorized for, holding `memberships.revoke`
anywhere would have revoked memberships everywhere. It answers 404 rather than
403, so nothing is disclosed about a membership in an organization the caller
cannot see.

Refusals are decided **before** the organization is looked at, so a caller outside
it learns nothing about whether it exists (ADR-0038).

## 8. Audit

Uses the existing append-only recorder. No parallel mechanism.

| Change | Recorded |
| --- | --- |
| Registering a user | `user.registered` — display name and identity-provider subject. **No token or credential.** |
| Granting | `membership.granted` — user, role, and whether the user was newly registered |
| Revoking | `membership.revoked` — user, **prior role**, new status |
| Role change | **both** — a `membership.revoked` carrying the prior role, and a `membership.granted` carrying the new one |

Proved by `MembershipChanges_AreAudited` and `ARoleChange_IsAuditedAsBothHalves`.
The actor is recorded on every one.

## 9. Concurrency

The `Membership` aggregate **carries no version**, so optimistic concurrency does
not apply to it and none was added — adding versioning would be a schema change
beyond this wave's minimum.

The domain's own rule stands in for it: a membership already revoked cannot be
revoked again. A stale client submitting a second revoke gets **400**, not a
silent success. Proved by `EndingTheSameMembershipTwice_IsRefusedTheSecondTime`.

A role change is one transaction. Leaving the client to revoke and then grant
would let a failure between them take somebody's access away and give nothing
back.

## 10. Idempotency

Consistent with the rest of the application:

- `AddOrganizationMemberAsync` accepts an idempotency key, and the Windows page
  passes one, so a replay does not create two memberships.
- Adding the same person twice is refused by the domain regardless — **400**, and
  the list still shows two people, not three
  (`AddingTheSamePersonTwice_IsRefused`).
- Revoke and change-role carry no key, matching how the rest of the client treats
  interactive commands the user can simply repeat.

## 11. The Windows surface

**Settings → Organization.** A list of people, each with a name, an email, a role
in words, and a note where one is warranted — "you", or "the only owner".

- **No raw identifier anywhere.** A member is chosen from the list. A role is
  chosen from a labelled selector. A new person is described by name, email and
  **sign-in name** — the identity-provider subject, which is what a directory
  shows and what a provider issues, not a GUID AgencyOS mints.
- Each role selector **says what the role means**, because "Administrator" here is
  oversight with almost no operational write, which is not what it means
  elsewhere.
- Ending access asks twice, and says the record is kept.
- **Refusals are shown in the server's own words.** "This is the organization's
  only owner. Give somebody else the owner role first" is more useful than
  anything the client could substitute.
- The list re-reads after every mutation. A role change ends one membership and
  grants another, so the identifier the page held is no longer live.
- Buttons enable from **what is selected**, never from a guess about what the
  caller may do. The server decides, and its refusal is displayed.

## 12. Audit personas now constructible

Created through the product's own commands by `reviewer personas`, verified
against the running server:

| Persona | Real role | `members` | `people` | `deals` |
| --- | --- | ---: | ---: | ---: |
| OWNER | Owner | 200 | 200 | 200 |
| MEMBER | Member | 200 | 200 | 200 |
| OBSERVER | Observer | 200 | 200 | 200 |
| **RESTRICTED** | Administrator | 200 | 200 | **403** |
| NON-MEMBER | — | 401 | 401 | 401 |

Four genuinely distinct authority shapes. The Administrator's 403 on `deals` is
the documented model working: oversight without operational read.

**Still unsupported:** an arbitrary custom permission profile. AgencyOS has four
fixed role grants and no per-membership permission list, and none was invented.

## 13. Gates

| Gate | Result |
| --- | --- |
| `dotnet build AgencyOS.sln` | **0 warnings, 0 errors** |
| Unit | **3,755** (was 3,749) |
| Windows | **733** (was 715) |
| Reviewer | **89** |
| Integration | **816** (was 790) — 26 new membership tests |
| OpenAPI | **3.1.1; 263 paths; 187 schemas** |
| API contract | **14** (was 13); minimum supported still 1 |
| TLA+ | **4 / 4** |

The Windows count rose by 18 without a line of test code: the accessibility and
layout theories run per XAML file, and the three new files pass them.

## 14. What was not touched

No other Audit 002 finding was repaired. `AOS-R001-006`, `AOS-R001-010`,
`AOS-R001-013`, `AOS-R001-020`, `AOS-R002-001`, `-003`, `-004`, `-005`, `-007`,
`-008` and `-009` are untouched and remain open.

**Audit 002 remains OPEN.** This wave removes the product blocker behind two of
its six unmet §30 gates — multi-role evidence and role/authorization UX — but does
not itself perform that audit work.
