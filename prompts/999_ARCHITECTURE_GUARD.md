# Prompt 999 — Architecture Guard

Run this before any major architecture change.

Question:
"Does this change solve a current measured limitation, enforce correctness, unlock a defined roadmap capability, or materially improve Windows-native UX?"

If no:
- reject the addition.

If yes:
- write an ADR comparing the smallest viable alternatives;
- state operational cost;
- state removal/rollback path;
- state which release ring receives it first.

This prompt exists to prevent 'frontier' from degenerating into decorative complexity.
