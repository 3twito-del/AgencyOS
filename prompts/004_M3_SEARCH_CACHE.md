# Prompt 004 — M3 Search & Local Cache

Add:
- PostgreSQL full-text/trigram search;
- saved views abstraction;
- local SQLite cache in Windows client;
- explicit cache schema/version;
- offline read of recently synchronized records;
- queue for reversible safe writes (notes/tasks/interactions only);
- conflict policy document and tests.

Before coding the sync queue, write an ADR and identify invariants.
If sync semantics become non-trivial, draft a TLA+/PlusCal model before adding more behavior.
