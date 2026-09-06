# ADR-0005: One release handshake contract, and enforcement independent of it

Status: Accepted
Date: 2026-09-07

## Context

The repository carried two incompatible descriptions of the release authority's
answer.

`docs/06_FORCED_UPDATE_PROTOCOL.md` shows a response with `policy`,
`latestVersion`, `minimumSupportedVersion`, an `apiContract` range,
`securityEpoch`, `mandatoryAfterUtc`, `killSwitch` and `rollbackTarget`.

`config/release-policy.example.json` shows the same decision plus `platform`,
`channel` and an `artifact` block carrying `sha256` and `signature`, which the
document never mentions.

M1 implements the handshake, so the difference had to be resolved rather than
noted. Separately, the document is explicit that enforcement is a security and
data-integrity mechanism and that a REVOKED client "cannot bypass it by altering
the UI" - which is a statement about what the server does, not about what the
response says.

## Decision

1. The response is the union of the two shapes, with the config example's fields
   kept: `platform` and `channel` are echoed, and `artifact` is present and
   nullable. `docs/06_FORCED_UPDATE_PROTOCOL.md` is updated to match.
2. Two fields are added that neither source had: `reason`, a human-readable
   explanation, and `blocksProtectedMutations`, the operative outcome.
3. `blocksProtectedMutations` is **informational**. The server computes the same
   decision again, from the same code, on every protected mutation. A client that
   never performs a handshake, ignores the answer, or is modified to believe it is
   current is refused identically.
4. Evaluation lives on the `ReleasePolicy` aggregate. The handshake endpoint and
   the enforcement middleware both call it; there is no second implementation.
5. Reads are not governed. Only mutating requests under `/api` are, and the
   handshake route itself is always reachable.

## Why this is better than the alternatives

Keeping the document's narrower shape would have discarded `artifact`, which is
what lets a client verify the build it is being told to install. Update
enforcement exists to protect data integrity; directing a client to an
unverifiable artifact would undercut the point.

Returning the decision without `blocksProtectedMutations` would leave the client
guessing which policy values are fatal, and would invite each client to
reimplement the severity table - the exact drift that having one authority is
meant to prevent.

Trusting the client to honor the flag would be the real mistake. A client that
has been tampered with is precisely the client the protocol exists to stop, so
the flag is a courtesy and the middleware is the control.

Governing reads as well as writes would leave a revoked build unable to explain
itself or fetch its update. `docs/06_FORCED_UPDATE_PROTOCOL.md` already
anticipates this with update-only and read-only modes.

## Consequences

- One severity table, in `ReleasePolicy.Evaluate`, tested directly and reachable
  through both the handshake and the middleware.
- A client that presents no usable identity headers cannot be evaluated and is
  therefore refused for mutations. Failing closed is deliberate.
- An unknown platform or ring returns MANDATORY rather than a permissive default.
- `artifact` is carried but not yet populated; signing arrives with M15.

## Migration / rollback

The contract is versioned by `ApiContract.Current`, currently 1. Widening the
response is additive. Removing or renaming a field requires a contract increment
and a supported range that covers both, which the handshake already negotiates.

## Evidence / metrics that would cause reconsideration

- A client that legitimately cannot send identity headers on every request.
- A need to govern reads, for example a data-exfiltration concern that outweighs
  a revoked build's ability to update itself.
- Signing arriving early enough that `artifact` should become non-nullable.
