# ADR-0033 — Activation, deep links, and what the Windows client is still not trusted to decide

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M13 — Advanced native Windows, workstation integration and local
  AI platform
- **Supersedes:** nothing
- **Builds on:** ADR-0031 (the AI runtime and its approval protocol), ADR-0032
  (the command registry and named destinations)

## Context

A workstation application is reachable from outside itself. A link in an email, a
toast the shell raises, a file somebody double-clicks, a URI another application
launches — each is an instruction from somewhere AgencyOS does not control, asking
it to open at a particular place.

M13 introduced that surface, and it introduced it in the same milestone that
introduced device-local model execution. The two together create a specific
temptation: once the workstation is doing real work — running a model, resolving a
link, deciding what a toast should say — it starts to look like a participant in
authorization rather than a consumer of it. It is not, and every decision here
exists to keep that true while the client gets more capable.

The rule from M0 is unchanged: **the Windows client is untrusted with respect to
authorization.** M13 makes it do more without letting it decide more.

## Decisions

### 1. One canonical activation router, and it is pure

`ActivationRouter` parses the `agencyos` scheme and resolves exactly seven routes:
`person`, `company`, `deal`, `contract`, `research`, `ai/run` and `ai/approval`.
It is a pure function from a string to an `ActivationRoute`, with no I/O, no
ambient state and no dependency on what the user may see.

Identifiers must parse as `Guid.TryParseExact(candidate, "D", …)`. Trailing
segments are refused. A route that fails resolves to a typed `ActivationFailure`
— `ForeignScheme`, `UnknownRoute`, `MalformedIdentifier`, `Empty` — rather than an
exception, because a malformed link is an ordinary event and not an error
condition.

### 2. A link carries a destination, never an instruction

The vocabulary is a kind and an identifier. There is no route that approves,
executes, sends or deletes, and anything shaped like one is refused as an unknown
route.

This matters most for `ai/approval`. M12's whole approval protocol rests on a
person choosing, at a screen, having read what AgencyOS says the action would do.
A link that could carry `?approve=true` would route around all of it — and it
would be a link an attacker could put in an email. So the link opens the approval;
the person decides.

### 3. Resolving a link is not evidence that the object exists or may be read

A route for an identifier that names nothing resolves exactly as one that names
something real. The client cannot tell the two apart, and does not try.

This is deliberately the opposite of an optimization somebody will propose. The
router could be given a cache and made to answer "no such approval" without a
round trip, which would feel faster and more helpful. It would also be a
disclosure — it tells whoever sent the link whether the identifier is real — and
it would put an authorization decision on the untrusted side of the boundary.

So a route is a destination. What the person may see is decided by the server when
the client asks, and the answer is uniform.

The same reasoning governs stale notifications. A toast is written at one moment
and acted on at another, and the gap can hold the withdrawal of the permission
that justified sending it. Acting on a toast is a fresh request, answered by what
is true when it arrives.

### An M11 observation, recorded and not repaired

The uniform-answer rule above holds on the AI routes. It does not hold across the
whole product, and M13's citation test is what surfaced that.

Following a citation to an M11 signal returns **403** when the signal exists above
the reader's clearance and **404** when it does not exist. Those are
distinguishable, so a well-formed identifier plus a 403 tells the holder that a
signal by that id exists in their tenant and is above their clearance. The AI
routes deliberately answer 404 to both.

The practical exposure is small — the caller is already inside the tenant and the
identifiers are unguessable — but it is a real difference in disclosure semantics
between two surfaces of the same system.

It is **not repaired here**. It is a question about the intelligence surface as a
whole rather than about citations, and changing it under an M13 heading would
settle an M11 design decision in a place an M11 reviewer would never look. M13's
test therefore asserts the property that actually matters for a citation, and is
stronger than either code: the read does not succeed, and the claim text does not
appear in the response.

### 4. A separate platform project, in C# only

`AgencyOS.Windows.Platform` holds the workstation decisions — activation,
notification policy, diagnostics, document handoff, capability description, the
local inference protocol — and holds **no WinRT**. It targets
`net10.0-windows10.0.26100.0` with `UseWinUI=false`, and it is pure decision code.

That separation is what makes any of it testable. The WinUI application project
provides the thin adapters that touch the operating system; the decisions live
where a test can reach them without a UI thread, and 521 Windows tests exercise
them.

**No C++, no Rust, no Python.** CLAUDE.md §2 permits all three for exactly the
work M13 does — native interop, local infrastructure, ML services — so refusing
them is a decision and not an oversight. The Windows AI APIs are projected into
C#, the protocol is HTTP and JSON, and the policy code is arithmetic over enums.
Nothing in M13 needed a second toolchain, a second build, a second set of
supply-chain surface or a second language to review. Adding one would have been
adding it for its own sake.

### 5. The client cannot reach server authority, structurally

No client project references `AgencyOS.Infrastructure`, `AgencyOS.Application`,
`AgencyOS.Domain` or `AgencyOS.Api`. `AgencyOS.Client` references only
`AgencyOS.Contracts`.

A client that referenced Application could call a business rule in-process and get
an answer the server never gave. A client that referenced Infrastructure could
reach the ModelGateway and the provider credentials behind it. The reference graph
is checked by `ClientBoundaryTests` rather than left to reviewer memory, because
it is the kind of thing added casually to fix a compile error.

### 6. No provider credential exists on the Windows client

M12 put every provider credential behind the server ModelGateway. M13 keeps it
there.

The temptation M13 creates is real: once the workstation runs a model, giving it a
key so it can "just call the provider itself" looks like a simplification. It is
not one. A key on a workstation is a key on every workstation, revocable only by
rotating it everywhere, and it would let the client reach a provider with no
lease, no policy evaluation and no audit record.

Nothing is given up by refusing. The device-local path needs no credential,
because the model is already on the machine.

## Why this is better than the alternatives

**Letting each page parse its own links.** Every page would reimplement identifier
validation, and the strict-format rule would hold wherever somebody remembered it.

**Routing through the command registry.** Tempting, since ADR-0032 just built one.
Wrong: a command is something a person here chose, and a link is a request from
outside. Collapsing them would make "open this" and "do this" the same kind of
thing at precisely the boundary where they must not be.

**Resolving links against a local cache.** Faster, and it converts the router into
an oracle for which identifiers exist.

**A C++ interop layer for the Windows AI APIs.** Would be the conventional choice
for native work and buys nothing here: the APIs are already projected, and the
second toolchain would need its own build, review and supply-chain story.

## Consequences

**What this buys.** Deep links, toasts and shell activation can be added freely,
because none of them can widen what a person may see. The router is a pure
function with 44 tests over it. The client's inability to reach server authority
is a property of the build rather than a convention.

**What it costs.** Every link resolution is a round trip, including the ones that
will turn out to point at nothing. Following a stale link shows a workspace and
then an empty state rather than failing immediately, which is slightly worse
interaction design and the price of not leaking existence.

**What is deliberately not here.** No file-type registration, no jump lists, no
share-target integration, no protocol-launched actions, no background agent. Each
is a plausible workstation feature and each widens the surface that arrives from
outside; none was needed to prove the boundary holds.

**What the next milestone inherits.** A router whose vocabulary is a kind and an
identifier, so adding a route is adding a case, and a client whose reference graph
is asserted rather than assumed.
