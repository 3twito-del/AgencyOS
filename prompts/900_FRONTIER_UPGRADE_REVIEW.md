# Prompt 900 — Frontier Upgrade Review

Use this periodically when a new SDK/runtime/database/toolchain version appears.

Do not upgrade blindly.

For each candidate:
1. identify current pinned version and proposed version;
2. classify source: GA / Preview / Experimental / Nightly / Daily / HEAD;
3. identify concrete AgencyOS benefit;
4. identify compatibility risks;
5. place it in the correct release ring;
6. create an isolated branch/environment;
7. run promotion gates;
8. record benchmark/migration/test results;
9. recommend PROMOTE / HOLD / REJECT;
10. update `config/version-policy.yaml` only after approval.

A higher version number alone is not sufficient justification for ALPHA/STABLE promotion.
