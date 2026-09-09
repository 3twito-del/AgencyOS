# ADR-0036 — Pinning a model checker to a release that cannot move

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M14 — Scale, architectural fitness and specialized services
- **Supersedes:** nothing
- **Amends:** the tla2tools pinning policy recorded in
  `docs/11_TESTING_AND_FORMAL_METHODS.md` since M3

## Context

AgencyOS checks four TLA+ specifications on every CI run, and a formal check that
quietly skips itself is worse than none. So `tla2tools.jar` has been fetched once
and pinned by SHA-256 since M3, with a hard failure on any unrecognized checksum.

The mechanism worked. What it revealed is that the thing being pinned was never
pinnable.

`v1.8.0` is not a fixed release. Upstream re-publishes that tag from current
master, and during M13 alone it moved **three times in five days** — 2026-09-04,
2026-09-08 and 2026-09-09. The pin caught every move. Each catch broke CI and cost
a full archive comparison before the build could go green again.

The 09-08 move was a manifest restamp of the same revision. The 09-09 move was
not: revision `65fbace6` at 8,925 commits against `b123b22` at 8,905, with 53 of
2,223 entries differing — the manifest and 52 `tla2sany` parser and
semantic-analyser classes. A genuinely different model checker, published under a
version number that had not changed.

## What the accepted evidence missed

M13 accepted the 09-09 build after re-checking all four specifications and finding
**identical state counts**. That was recorded as evidence that "the semantics these
specs rely on did not move".

It was not enough, and M14 found the gap:

> The 09-04 build reported an `OutboundSend` search depth of **17**. The 09-09
> build and `v1.7.4` both report **14**, over the same 83/48 state space.

No property went unchecked. No run reported an error. The state graph is the same
size in every build. But the checker's search behaviour did move between two
artifacts wearing one version number, and comparing state counts alone did not
catch it.

The 09-04 artifact is no longer obtainable, because upstream overwrote it. So the
discrepancy can no longer be examined — and that is the concrete, permanent cost
of pinning a tag that moves: not that the build broke, but that the evidence
needed to explain a difference was destroyed while the difference was still open.

## Decision

### 1. Pin `v1.7.4`, because it is built from a tag

`v1.7.4` was published 2024-08-05 and has not been touched since. Its manifest
carries `X-Git-Tag: v1.7.4` at revision `5a47802b`, 7,425 commits — built from the
tag rather than from whatever master happened to be on the build date. That is the
property the pin needed from the start, and the property `v1.8.0` never had.

SHA-256 `936a262061c914694dfd669a543be24573c45d5aa0ff20a8b96b23d01e050e88`,
2,274,532 bytes, 1,066 entries.

### 2. One accepted hash, and it is expected to stay one

The multi-hash list existed to survive a tag that moved. With an immutable tag
there is nothing to survive, so the list returns to a single entry. A second entry
would now mean either that upstream rewrote a two-year-old release — which is
worth stopping the build over — or that somebody is skipping the comparison.

### 3. Search depth is recorded evidence

State counts alone were insufficient, demonstrably. The pinned entry now records
depth alongside counts, and the specification list in
`docs/11_TESTING_AND_FORMAL_METHODS.md` does too: OfflineWriteQueue 2853/1024
**d15**, OutboundSend 83/48 **d14**, AiApproval 755/236 **d9**,
LocalInferenceLease 796/160 **d9**.

All four were verified green against `v1.7.4` before the switch, from a clean
fetch, and match those figures.

## Why this is better than the alternatives

**Keeping the multi-hash list.** Formalizes a workaround for a problem that has a
fix. CI breaks every few days, and each recovery depends on somebody performing a
2,223-entry comparison carefully rather than pasting the new hash — which is
exactly the failure the pin exists to prevent, relocated into human diligence.

**Vendoring the jar.** Genuinely deterministic and independent of upstream, and it
contradicts a principle this repository already stated: an 8 MB binary in a source
repository is its own problem. It also puts a binary into every clone and every
review. Worth revisiting only if `v1.7.4` ever becomes unavailable.

**Building TLC from source.** Reproducible in principle. It adds a Java build, its
own dependency set and its own supply chain to a repository that uses the tool as
a checker, not as a component.

**Tracking the newest TLC.** The premise is backwards. A model checker that
changes under a specification makes a green run mean something different from one
week to the next, which is the one thing a formal gate must not do.

## Consequences

**What this buys.** A formal gate that is deterministic across time. One hash, an
artifact that cannot be overwritten, and evidence — counts *and* depth — recorded
against it. CI stops breaking for reasons unrelated to AgencyOS.

**What it costs.** An older TLC than upstream master. All four specifications
check green on it today, and if a future specification needs a newer feature, that
is a deliberate upgrade with its own verification rather than an artifact that
changed underneath one.

**What is deliberately not here.** No vendored binary, no source build, no
tracking of upstream releases, no automatic hash acceptance.

**What the next milestone inherits.** A pin that should not need attention. If it
ever fails again, the failure means something real happened, which is what a gate
is for.
