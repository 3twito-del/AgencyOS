---
description: Implement one AgencyOS roadmap milestone as the smallest coherent vertical slice. Use when the user asks to start, continue, or complete a milestone.
---

Read:
- `CLAUDE.md`
- `docs/03_ROADMAP.md`
- the relevant module specifications
- the relevant numbered prompt in `prompts/`

Then:
1. State the milestone and its acceptance criteria.
2. Check the current repository status.
3. Do not reopen settled architecture unless blocked by a real incompatibility.
4. Implement only the requested milestone.
5. Add/update tests.
6. Run `pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify`.
7. Update roadmap status.
8. Record an ADR only for a real architectural decision.
9. Stop and report what is complete, what failed, and the next exact step.

Never silently expand into a later milestone.
