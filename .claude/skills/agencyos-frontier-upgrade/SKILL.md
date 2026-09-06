---
description: Evaluate and stage a new SDK, compiler, database, runtime, AI, Windows API, or infrastructure version without endangering AgencyOS canonical data.
---

Read:
- `config/version-policy.yaml`
- `docs/04_TECHNOLOGY_MATRIX.md`
- `docs/05_RELEASE_ENGINEERING.md`
- `prompts/900_FRONTIER_UPGRADE_REVIEW.md`

For the candidate:
1. classify GA/Preview/Experimental/Nightly/Daily/HEAD;
2. identify concrete AgencyOS benefit;
3. place in FORGE/LAB/NIGHTLY/ALPHA/BETA/STABLE;
4. isolate data/environment;
5. define promotion gates;
6. run available compatibility tests;
7. recommend PROMOTE/HOLD/REJECT.

Never promote to ALPHA solely because the version number is newer.
