# AgencyOS ALPHA 0.1.0, build 89

Reality Closure wave 6: the last counters that stated a population they had never
counted.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **89** |
| Executable commit | **`01c41ea`** |
| Tag | **`alpha-01c41ea`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 88, `alpha-ecef456` |

**No contract change, no migration, no new endpoint and no domain change.** One
presentation primitive, six view models, seven pages, two tests added. Nothing
under `src/AgencyOS.Contracts/`, `src/AgencyOS.Api/`, `src/AgencyOS.Domain/`,
`src/AgencyOS.Application/` or `src/AgencyOS.Infrastructure/` was touched.

## 2. The executable commit is not the branch tip

```
git diff --stat 01c41ea..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

F-01 was the census's first and largest finding: a count or total built from
whatever the local collection happened to hold, so a failed load printed a
confident zero directly beneath the error bar explaining that the load had failed.
Wave 1 took the six money totals, wave 2 the four deadline claims. **This build
takes the rest.**

| Where | What it said on a failed load |
| --- | --- |
| Documents, Projects, Talent, Communications ×3, AI | `0 documents`, `0 project(s)`, `0 shown · 0 client(s)`, `0 messages`, `0 of your runs` |
| Command Center | a confident **`0`** under each of *open tasks*, *people* and *companies* |
| Organization | a membership count that survived a failed refresh as though current |

Ten empty-state notices on those same surfaces carried the matching defect — a
population that loaded empty once and then failed to refresh is still empty and no
longer known, so *Nothing here* opened beside the error bar. They are repaired with
the counters they sit beside.

## 4. Normalizing the list changed it

The register's remaining band still listed **Contracts** and **Deals**, which wave
2 had already closed — the same double-count across severity bands that wave 2 had
to correct once already. It did not list the **Communications desk counts** at all,
and those turned out to be the worst of the group: wave 2 gated the sentence
*"Whether anything needs attention is unavailable."* and left the three counts
directly above it ungated, so one panel could disclaim and assert in the same
breath.

Seventeen instances, not eighteen. **Five of them are not F-01**, and are
reclassified rather than repaired:

- **Talent's** *"No summary recorded."* and its two representation descriptions are
  optional fields on a record that has loaded. Absence there is canonical business
  data, not a failure to look. They were caught by a scan because the strings
  contain the word *no*.
- **Intelligence's** research and interaction counts come from projections the page
  refuses to render without, so no false zero is reachable. They are stale after a
  failed refresh — but so is every other line in those panels, and repairing the
  counter alone would imply the rest was fresh.

## 5. The Command Center needed a decision, not a gate

*Totals unavailable.* does not fit a slot that showed `46`. The slot takes an **em
dash** instead — the product's existing way of writing *no value here*, and it
reads as absence rather than as zero.

The announcement is not the glyph. A reader given `—` hears punctuation or silence,
so the seen and the spoken channels would disagree about whether the product knows
the answer:

| State | Seen | Announced |
| --- | --- | --- |
| loaded | `46` | `46 open tasks` |
| loading | `—` | `open tasks still loading` |
| failed or never asked | `—` | `open tasks unavailable` |

The defect was in the view model, not the page: `OpenTaskCount` returned
`_view?.OpenTaskCount ?? 0`.

**The headline's scope was not changed.** It counts the whole tenant; the three
lists below it are the overdue, due-soon and unscheduled windows. That difference
is deliberate, it is recorded as F-05, and making the numbers match would have been
a false repair.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35787574785` and canonical Nightly run `35787594404`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,983 |
| Windows | 1,326 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification drove the real Windows client through every state the family
turns on. With every load failing, no repaired surface showed a digit anywhere and
the *Nothing here* notice stayed shut. On an empty tenant every surface gave its
truthful zero. With three people and two companies on file the headlines read `3
people` and `2 companies` — and when the server was stopped and **Refresh**
pressed, they read `people unavailable` and `companies unavailable`, with the stale
`3` gone from the screen entirely. Restarting the server and pressing Refresh
restored both.

The full evidence, including one observation about a blank desk summary after
navigating with the server down, is in
`artifacts/operational-alpha/reality-closure/WAVE-06-REMAINING-AUTHORITY-CLOSURE.md`.

## 7. Verdict

**F-01 is closed.** Every instance the census recorded — twenty-eight across
fourteen pages and six waves — now has a terminal status, and none was accepted as
an intentional semantic to get there.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 89.
