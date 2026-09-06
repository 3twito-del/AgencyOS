---
description: Review a proposed AgencyOS architecture change against the project's frontier-but-disciplined engineering constitution.
---

Read `CLAUDE.md`, `docs/02_ARCHITECTURE.md`, `docs/10_ENGINEERING_STANDARDS.md`, and `prompts/999_ARCHITECTURE_GUARD.md`.

Evaluate:
- current measured problem;
- smallest viable solution;
- why existing stack is insufficient;
- correctness/security/data consequences;
- operational cost;
- release ring of first introduction;
- rollback/removal path.

Output one of:
- ACCEPT
- ACCEPT IN FORGE/LAB ONLY
- HOLD
- REJECT

If accepted, draft an ADR. Do not implement until the ADR decision is approved when the change is substantial.
