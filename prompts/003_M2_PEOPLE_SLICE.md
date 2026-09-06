# Prompt 003 — M2 People Vertical Slice

Implement exactly this end-to-end workflow:

Create Person
-> connect Organization
-> create Relationship
-> record Interaction
-> create next Task
-> surface it on Command Center.

Requirements:
- server-side validation/authorization/audit;
- OpenAPI contracts;
- PostgreSQL persistence;
- native WinUI list/detail flow;
- keyboard-friendly navigation;
- unified timeline;
- basic search;
- tests for domain invariants and API integration.

Do not add Talent, Deals, AI, Redis, Temporal, OpenSearch, graph database or microservices.
