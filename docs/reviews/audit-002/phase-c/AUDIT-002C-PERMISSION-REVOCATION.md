# Audit 002 Phase C — what happens after a permission is taken away

§5. Previously impossible: nothing in the product could change a membership, so
the sequence could not be run at all. Repair Wave 003E-A made it a command, and
this is the first time it has been observed.

---

## The sequence

Read with permission → reduce the role → read again → write → read something
still permitted → end the membership → read anything.

| Step | Result |
| --- | --- |
| Added as a Member | `201` |
| Reads intelligence as a Member | `200` — content delivered to the client |
| Reads documents as a Member | `200` |
| Owner reduces them to Observer | `200` |
| **New read of the same material** | **`403`** |
| **New write** | **`403`** |
| Reads what an Observer may still read (`people`) | `200` |
| Owner ends the membership | `204` |
| Reads anything at all | **`403`** |

| Verdict | |
| --- | --- |
| `NEW_READ_BLOCKED` | **yes** |
| `NEW_WRITE_BLOCKED` | **yes** |
| `REDUCTION_IS_TARGETED` | **yes** — not a blanket lockout |
| `REVOCATION_BLOCKS_EVERYTHING` | **yes** |
| `STALE_ALREADY_DISCLOSED_CONTENT_REMAINS` | **yes**, and correctly so |

The reduction being *targeted* is the part worth dwelling on. After losing
intelligence access the same person still reads `people` at `200`. A system that
locked them out of everything would look safer in a test and would be wrong: the
role changed, it was not revoked, and the server enforces exactly the difference.

---

## The M13 truth, preserved

Content already delivered to a client cannot be recalled. The thesis this
persona read as a Member was in their process's memory before the reduction, and
no server-side change reaches it.

**This is not a new product defect and is not filed as one.** §5 says so
explicitly and it is right: the residual risk is a property of having disclosed
something, not of how the disclosure was later revoked. What is testable is
whether the *server* stops answering, and it does — immediately, on the next
request, with no grace period and no cached authorization.

What would have been a defect is a token or session that kept working after the
role changed. It does not.

---

## The last-owner invariant

Repair Wave 003E-A's §0 classification reported that the product had no
protection against an organization losing its last owner, and treated it as a
correctness issue before writing any code. The invariant now lives in the domain:

```csharp
if (Role == AgencyRole.Owner && otherActiveOwners == 0)
    throw new DomainException(
        "This is the organization's only owner. Give somebody else the owner "
            + "role first, then end this membership.");
```

Audit 002 confirms it holds at the boundary as well as in the aggregate, through
`MembershipAdministrationTests`. It is recorded here because the audit asked for
the membership surface to be reviewed, not because the audit repaired anything.

---

## Findings

None. Every property §5 asks about holds.

## Evidence

- `artifacts/reviewer/run-002-phase-c/permission-revocation.json`
