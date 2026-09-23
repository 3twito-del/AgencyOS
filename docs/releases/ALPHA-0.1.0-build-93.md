# AgencyOS ALPHA 0.1.0, build 93

Reality Closure wave 10: the screen says which population it is counting.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **93** |
| Executable commit | **`47d857891031ff95ce5326e713e2efb46f50df98`** |
| Tag | **`alpha-47d8578`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 92, `alpha-a7378c4` |

**No contract change, no migration, no endpoint, no domain change and no query
change.** One caption, one sentence, one presentation type, one corrected source
comment.

## 2. The executable commit is not the branch tip

```
git diff --stat 47d857891031ff95ce5326e713e2efb46f50df98..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. The numbers were never wrong

The Command Center headline counts every open task in the agency. The three lists
beneath it are windows onto that population:

| Figure | Population |
| --- | --- |
| Headline | every task with `State == Open`. No date filter at all. |
| Overdue | open, due before now |
| Due soon | open, due within the next 7 days |
| Unscheduled | open, with no due date |

The windows are disjoint, so nothing is listed twice. They are also **not
exhaustive**: an open task due further ahead than the seven-day horizon belongs
to none of them and is still counted in the headline. That fourth category had no
home on the screen, and it is the entire gap — the `46` above `22` rows recorded
at build 83 was 24 tasks due more than a week out.

**Both numbers were correct. Neither said what it counted.**

## 4. Why that mattered

The caption read `open tasks`, directly above three task lists, which reads as
*the* open tasks. An operator had two available readings and both were wrong:
that the page contradicted itself, or that the lists were the whole of the work
shown in three groups.

The build-82 blind operator took the second, scanned the rows, believed they had
seen everything, and answered 2 of 5.

## 5. What changed

The caption now reads **`open tasks in total`**, and one sentence above the lists
says what they cover and what sits outside them:

> **Needing attention**
> Overdue, due within 7 days, or with no date set. 3 more open tasks are
> scheduled further ahead and are not listed here.

"Needing attention" is the page's own vocabulary — its empty state already reads
*"Nothing needs attention."*

The sentence is decided in `WindowScope`, in the client presentation layer, for
the reason wave 6 put `SummaryAuthority` there: meaning does not belong in a
page, and a proposition no test can execute is one nobody can check. The page
renders it and sets it as the accessible name, so the seen page and the spoken
page cannot differ about what is covered.

## 6. What deliberately did not change

Not one row moved. No task was added to a list to make the headline agree, no
task was removed from the count, the horizon is still seven days, the overdue and
undated definitions are untouched, the three lists were not merged, and no
fourth list, filter, pagination or projection was added.

**A headline that counted only what is listed would answer a different question.**
An operator needs to know both how much open work exists and what needs doing
this week. Forcing the two to agree would have destroyed one of the answers.

## 7. A count nobody has is still not zero

The remainder is arithmetic over two figures the page already holds — not a new
query. It is stated only where the projection arrived.

With the server stopped, the headline announces `open tasks in total unavailable`
and the sentence names the windows and claims nothing else. No figure, no zero,
and no *"every open task is listed here"* — which is a completeness claim an
unloaded page has not established. That is wave 6's rule applied to a number this
page computes rather than one it was given.

## 8. The comment that was wrong

`PeopleSliceQueries` claimed *"three disjoint buckets, so a task appears exactly
once and the counts add up."* They cannot add up. That sentence is the likeliest
reason the page was built as though the lists rendered the headline — a page
written against a stated invariant that was not true. It is corrected; behaviour
is untouched.

## 9. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35805908384` and canonical Nightly run `35805910564`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,037 |
| Windows | 1,376 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 944 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Integration is unchanged at 944 on purpose: nothing below the presentation layer
moved, so there was nothing new for a database to answer.

### Live, against a fixture where the numbers cannot coincide

10 open tasks; 2 overdue, 4 due soon, 1 unscheduled — **7 rows** — and **3 open
tasks due in 30, 45 and 60 days**, which are in no window at all.

| | Build 92 | Build 93 |
| --- | --- | --- |
| Headline | `10 open tasks` | `10 open tasks in total` |
| Scope sentence | *(no such element)* | `Overdue, due within 7 days, or with no date set. 3 more open tasks are scheduled further ahead and are not listed here.` |

Both readings are automation names, so the distinction reaches a screen-reader
operator rather than depending on layout. The three lists still hold 2, 4 and 1
rows, still announce their own names, and `Complete selected task` still works.

## 10. Verdict

**F-05 is closed** — as the disclosure gap it was reclassified to be, not as an
authority defect. Twelve of the seventeen root findings are now closed, and the
API contract has not moved once across the whole programme.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build
93.
