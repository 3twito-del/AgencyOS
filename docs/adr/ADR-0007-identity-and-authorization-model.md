# ADR-0007: Replaceable identity, authorization decided from current state

Status: Accepted
Date: 2026-09-07

## Context

`docs/07_SECURITY_AND_AUDIT.md` asks for a development identity with an explicit
authorization architecture now, and OIDC or Entra ID later, plus RBAC for broad
roles and policy checks for context. `prompts/002_M1_IDENTITY_AUDIT_UPDATE.md`
requires the identity provider to stay replaceable and forbids Entra integration
in M1.

Two things had to be decided: what "replaceable" means concretely, and where the
authorization decision is actually made.

## Decision

**Identity.** `User.ExternalSubject` holds the identity-provider subject as an
external identifier, never as the primary key. Users are located by subject.
The only provider-specific code is `DevelopmentAuthenticationHandler` and its
scheme registration; adopting OIDC replaces that class and registration, and
touches no domain, application or persistence code.

**The development scheme is fenced by the ring model.** It trusts a header, so
the host refuses to start when it is configured on a ring that permits real data
(`ReleaseRingNames.AllowsRealData`, mirroring `canonical_data_allowed_from: alpha`).
An unknown subject fails authentication rather than provisioning a user.

**Authorization is decided twice, and the inner decision is authoritative.**

- The endpoint declares a permission policy. This is an early gate that rejects
  anonymous and plainly unauthorized callers, and it checks the permission
  unscoped - held through any active membership.
- The command handler calls `IPermissionEvaluator` again, scoped to the
  organization the command targets. This is the authoritative check.

**Permissions are read from current state on every request.** Nothing is cached
in a token or session.

**Roles confer permissions through an active membership only.** A revoked
membership confers nothing, and is retained rather than deleted.

## Why this is better than the alternatives

Putting the authoritative check in the endpoint would tie enforcement to the HTTP
pipeline. Anything else that ever calls a handler - a background job, a second
transport, an AI tool invocation under M12 - would be unguarded by construction.
`docs/09_AI_RUNTIME.md` states plainly that AI may not bypass permissions; the
cheapest way to keep that true is for the permission check to live where the
command lives.

Making the endpoint gate scope-aware was considered and rejected. It would mean
reading route values inside an authorization handler and duplicating the scoping
rule in two places, where the two could drift apart. An unscoped early gate plus a
scoped authoritative check has no such failure mode: the early gate can only be
more permissive, never less.

Carrying permissions as token claims would be faster and would make revocation
take effect only when the token expired. Revocation that is not immediate is not
really revocation, and the read is a single indexed query.

Keeping the development scheme without the ring guard would leave a
header-trusting authenticator one configuration mistake away from canonical data.
Refusing to start is loud, early, and cannot be missed.

## Consequences

- Every authorized request costs a membership lookup. Indexed on
  `(user_id, status)`; if it ever matters, it caches per request, not per session.
- An unscoped permission check exists in the pipeline. It is deliberately the
  weaker of the two, and is never the only one.
- Adopting OIDC means new claims mapping and a new scheme, not a data migration.
- A first-run bootstrap - the initial organization and its first owner - is not
  yet built. Tests seed directly. See the M1 limitations note.

## Migration / rollback

Adding an OIDC scheme is additive: register it alongside, move the policies to
it, remove the development scheme. `ExternalSubject` values are rewritten by a
data migration if the issuer changes, and nothing referencing a user changes,
because nothing references a user by subject.

## Evidence / metrics that would cause reconsideration

- Membership lookups appearing in latency profiles.
- A need for relationship-aware authorization beyond organization scope, which
  `docs/07_SECURITY_AND_AUDIT.md` anticipates and this model does not yet cover.
- Delegation or impersonation, which would need the audit trail to record both
  the acting and the effective identity.
