# Technology Matrix — Snapshot 2026-09-06

This file is a snapshot, not a permanent ceiling. The policy is frontier-tracking with promotion gates.

| Domain | FORGE/LAB | ALPHA real-data baseline |
|---|---|---|
| .NET | latest official daily/main; public edge = SDK 11.0.100-preview.7 (full 11.0.100-preview.7.26381.103) | .NET 10.0.11 LTS / SDK 10.0.400 |
| C# | 15 + preview compiler features | 14 unless promoted |
| F# | 10 | 10 |
| ASP.NET Core | 11 preview/daily | 10.0.11 |
| EF Core | 11 preview/daily | 10.x compatible |
| Windows App SDK | 2.4.1-experimental | 2.4.0 |
| Windows SDK | 10.0.28000.2705 | newest validated compatible Windows SDK |
| PostgreSQL | git HEAD/development snapshot; 19 Beta 3 | 18.6 |
| pgvector | master only in FORGE; public 0.8.6 | 0.8.6 when introduced |
| Python | CPython main; 3.15.0rc2 official prerelease | supported stable Python |
| Rust | nightly | stable unless nightly-only feature explicitly approved |
| TypeScript | 6.0 | 6.0 for required web surfaces |
| Node.js | 26.8.1 Current for LAB tooling | supported LTS where preferable |
| Visual Studio | 2026 Insiders 18.10 branch | 2026 18.9.2 stable side-by-side |
| Temporal .NET SDK | latest tested; snapshot reference 1.18.0 | only when M12+/workflow need exists |

## Update rule

FORGE tracks upstream, not fixed version numbers.

A newer upstream version is admitted to FORGE automatically/manual-assisted, then promoted only after:
1. build;
2. unit tests;
3. integration tests;
4. DB migration tests;
5. Windows UI smoke tests;
6. data-integrity checks;
7. performance regression check;
8. rollback verification when relevant.

## Important

"Newest" is not equivalent to "best for canonical data". FORGE exists so AgencyOS can remain technologically aggressive without making its canonical data experimental.
