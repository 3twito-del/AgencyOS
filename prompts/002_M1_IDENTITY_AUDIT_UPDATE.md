# Prompt 002 — M1 Identity, Audit & Release Policy

Implement M1 as a vertical infrastructure slice.

Required:
- Organization
- User
- Membership
- role/policy authorization foundation
- append-only AuditEvent
- client version handshake endpoint
- release-policy model with NONE/AVAILABLE/RECOMMENDED/MANDATORY/REVOKED
- server-side rejection of revoked/incompatible clients
- database migrations
- integration tests using real PostgreSQL test environment

Do not build Entra integration yet. Keep identity provider replaceable.
Do not put release enforcement only in the Windows UI.

Acceptance:
- unauthorized mutation fails;
- authorized mutation succeeds;
- audit event exists;
- revoked version is rejected by API;
- migration test passes.
