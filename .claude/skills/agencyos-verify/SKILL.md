---
description: Verify AgencyOS after implementation or refactoring using the canonical project verification entrypoint.
disable-model-invocation: true
---

Run:

`pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify`

Then summarize:
- environment/toolchain result;
- build;
- unit tests;
- integration tests if available;
- formatting/static analysis if available;
- failures and exact failing command.

Do not declare work complete when verification fails.
