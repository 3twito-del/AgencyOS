# Progressive Hook Presets

Only `ConfigChange` auditing is enabled by default.

Do not enable expensive hooks prematurely.

Suggested progression:

## M0–M2
- ConfigChange audit only.

## M3–M6
Optionally enable a lightweight Stop hook that runs `scripts/Invoke-AgencyOS.ps1 verify-fast`.

## M7–M9
Add targeted verification around deal/finance modules, preferably as explicit tasks/skills rather than running the entire suite after every edit.

## BETA/RC
CI, not Claude hooks, is the authoritative gate for:
- integration tests;
- migration tests;
- security scans;
- signing;
- SBOM/provenance.

Hooks assist the agent; they do not replace CI.
