# ADR-0035 — Model residency, the context lease, and what local AI can honestly claim

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M13 — Advanced native Windows, workstation integration and local
  AI platform
- **Supersedes:** nothing
- **Builds on:** ADR-0011 (composite tenant keys), ADR-0012 (audit), ADR-0014
  (concurrency and idempotency), ADR-0030 (intelligence provenance), ADR-0031 (the
  AI runtime, the model gateway and the approval protocol), ADR-0033 (the
  untrusted client)

## Context

M12 established that a model is untrusted input reached through one gateway, that
tools are a closed registry, and that `task.create` is the only canonical write a
model can propose. All of it assumed the model runs somewhere AgencyOS controls.

M13 asks whether a model can run on the user's workstation. The appeal is real and
is not primarily about speed: material that never leaves the building has a
different disclosure profile from material sent to a provider, and an agency has
records it will not put in anybody else's datacentre at any price.

The danger is a single confusion, and it is seductive enough to state plainly:

> **The material is already on this machine, and this person is already allowed to
> read it, so what exactly is being protected?**

The answer is that "already allowed to read it" is a statement about a moment, and
that authorization is a server decision. A workstation that assembles its own
context has made itself the authority on what it may see. Once that is true, every
guarantee from M0 to M12 rests on a process AgencyOS does not control, running on
a machine somebody else administers.

So the governing rule for M13, from which everything below follows:

> **Residency is where execution happens. It is never who decides.**

## Decisions

### 1. `ModelResidency` describes execution, not trust

Three values: `ExternalCloud = 1`, `OrganizationControlled = 2`, `DeviceLocal = 3`.

A residency answers "which machine ran the arithmetic". It confers no authority,
relaxes no classification and skips no check. A `DeviceLocal` run is authorized
exactly as a cloud run is, by the server, before anything is disclosed.

Recording it at all matters because it is a real property of a run that a reader
should be able to see — "this brief was produced on your laptop" is a meaningful
fact — and because the no-fallback rule needs something to name.

### 2. Restricted material is unreachable at every residency

`ModelDataPolicy.EvaluateAsync` refuses `Restricted` **before the provider is
looked up at all**. Not after checking whether the provider is local; before
consulting any policy.

This is where the seductive argument bites hardest, because at `DeviceLocal` it is
almost convincing. The material stays on the machine; the reader may read it; no
packet leaves. And it is still wrong, because "restricted" would come to mean
"restricted unless the model runs nearby" — a classification whose meaning depends
on an execution detail is not a classification.

`RestrictedIsRefusedBeforeAnyProviderIsConsulted` proves the ordering by
constructing the service with a repository that throws if it is consulted.
Restricted is still refused, so no configuration of the local provider and no
grant to the person asking can reach it. A policy row for `windows` is an ordinary
policy row: it cannot be created at `Restricted` and cannot be raised to it.

### 3. Context is disclosed to a device only under a lease

`AiContextLease` is a single-use, expiring, fingerprint-bound authorization to
execute one run's context on one device. It binds the organization, the user, the
run, the subject kind and id, the residency, the context fingerprint, the model
key, the policy version, an issue time and a **ten-minute** expiry.

The lease exists because the client half of the protocol is unobservable. AgencyOS
hands over context and later receives text; it cannot see what happened in
between, cannot know the process was the one it was talking to, and cannot know
the context was not altered. The lease makes the return checkable without needing
to trust the machine: the result is accepted only if presented with a lease that
is still `Issued`, matches on every binding field, and carries a fingerprint the
server recomputes for itself.

`Authorizes` never mentions the subject, which reads like an omission and is not.
The subject is part of the material the fingerprint covers, so a lease issued to
brief one person cannot be presented for another — the arithmetic disagrees before
a comparison would have run.

`AiContextLease.Issue` **throws for `ExternalCloud`**. There is no such thing as a
lease for cloud execution, so there is no object that could authorize a fallback.

### 4. The fingerprint proves identity of context, not secrecy of it

SHA-256 over the binding fields and the rendered context, joined on the ASCII
unit separator (U+001F) so that no field's content can forge a field boundary.

It is worth being exact about what this does and does not mean. It is **not** a
confidentiality mechanism — the context is disclosed to the device in the clear,
because the device has to read it. It answers one question: *is what came back a
result for the context we authorized?* If the person's name changed between
issuing and returning, the fingerprint the server recomputes differs and the
result is refused, because it is an answer to a question nobody asked.

### 5. Capability is checked before a lease is requested, not after

The client probes `LanguageModel.GetReadyState()` and the execution provider
catalog **before** asking for a lease. If the workstation cannot run the model,
**zero leases are issued** and no context is disclosed.

The ordering is the whole point. Issuing a lease *is* the disclosure; discovering
afterwards that the machine cannot run anything would mean the agency's material
had been handed to a process that had no use for it.
`NothingIsLeasedOnAMachineThatCannotRunIt` asserts it.

### 6. No silent fallback from device-local to cloud

A device-local run that cannot execute **fails**, with a category —
`LocalProviderUnavailable`, `LocalModelNotReady` or `LocalExecutionAbandoned` —
and nothing is sent anywhere else.

This is the decision most likely to be softened later, so the reasoning is
recorded. A fallback would be a good product decision and a catastrophic security
decision: somebody choosing device-local residency is usually choosing it *for the
material*, and quietly sending that material to a provider because the local model
was busy inverts the one choice they made. The server refuses a `DeviceLocal`
descriptor with `LocalProviderUnavailable` rather than serving it; the client has
no second provider to fall back to; the lease cannot cross residency; and the
formal model checks `LocalOnlyNeverFallsBackToCloud`.

Failing visibly is the feature.

### 7. A local result is untrusted exactly as a cloud result is

Text returned from the workstation is fenced as untrusted data, has its citations
validated against what the run was actually given, and can propose only what any
run can propose. `task.create` remains the sole canonical AI write.

The client adds nothing of its own. It does not summarize, re-prompt, retry with a
different model, or interpret. It renders the leased context, calls the model,
returns what came back and reports the device.

### 8. `SupportsTools = false` for the Windows provider, and no emulation

`WindowsLocalModel` declares `Key = "windows-local"`, `ProviderKey = "windows"`,
`SupportsStructuredOutput = true`, `SupportsTools = **false**`,
`Residency = DeviceLocal`.

The Windows AI `LanguageModel` API has `GenerateResponseAsync` and
`GenerateStructuredJsonResponseAsync` and **no function-calling contract**. It
would be possible to fake one — ask for JSON, parse it, treat a matching shape as
a tool call — and it is refused. Parsing arbitrary model JSON into executable tool
calls means a text generator's output is being routed into the tool runtime
through a path the provider never guaranteed, on the least controlled machine in
the system. The Relationship Brief needs prose, not tools, so nothing is lost by
being honest that this provider cannot call tools.

### 9. The client gets the narrowest capability that does the job

`ILocalInferenceApi` — three methods — is segregated from the broad
`IAgencyOsApi`, and `LocalInferenceRunner` depends only on it.

The runner is the component executing untrusted model output on an untrusted
machine. Handing it the full client surface would give the local inference path
the ability to call anything AgencyOS exposes, for the convenience of one
constructor parameter.

### 10. A stored result re-authorizes on every read, against the classification it
was generated under

`ResultSensitivity` is recorded when the result is produced. Reads above
`Internal` re-check `AiSensitiveUse`, and a reader who no longer holds it gets the
run with `ResultWithheld = true` and no result.

Recorded rather than recomputed, deliberately. Recomputing would ask "what would
this material be classified as *now*", which is a different question and
answerable only by reassembling context the reader may no longer be allowed to
see. The recorded value is what the result was actually drawn from.

The run itself stays visible. Hiding it would tell the reader less than the truth
— they asked the question — and a run that vanished would look like data loss.

The alternate paths are closed too: the run list carries no result to withhold,
and the trace does not quote the answer. The second holds only because steps
record what AgencyOS did rather than what the model said, which is asserted rather
than assumed.

### 11. The residual risk this does not solve, stated plainly

**Revocation governs what AgencyOS will show from now on. It does not reach a copy
somebody already read, pasted, printed or remembered.**

A brief is a derived work of everything the run was given; a summary drawn from a
source-sensitive signal carries that signal's confidence even though nothing in it
looks like one. Re-authorization stops AgencyOS from serving it again. It cannot
un-disclose a disclosure.

This is recorded because the mechanism looks stronger than it is, and somebody
reasoning about it later should not have to rediscover the limit. The mitigation
is upstream — the classification that decided what could be sent in the first
place — not downstream.

## The formal model

`specs/LocalInferenceLease.tla` is the fourth AgencyOS specification, checked with
`MaxReturns = 2`: one return reaches the effect, the second is the replay that
must not produce a second one.

**Result: 796 states generated, 160 distinct, depth 9, no error.**

Thirteen named properties. Six checked as invariants — `TypeOK`,
`AtMostOneEffect`, `LocalOnlyNeverFallsBackToCloud`,
`ClientResultIsNeverAuthorityAlone`, `ChangedContextCannotReuseLease`,
`NoDisclosureWithoutLease` — and seven as temporal formulas:
`NoEffectWithoutCurrentPermission`, `NoEffectWithoutValidLease`,
`MismatchedPresentationNeverTakesEffect`, `ExpiredLeaseCannotAuthorize`,
`CancelledRunCreatesNothing`, `CancellationNeverErasesEffects`,
`LeaseAlwaysStopsAuthorizing`.

### What the model refused to prove

**A run does not always terminate.** A workstation that takes the context and
never comes back leaves the run in `AwaitingLocalExecution` for ever, because
AgencyOS cannot make somebody else's process answer. Writing a liveness property
claiming termination would have been false.

What is proved instead is that the *lease* always stops authorizing — consumed,
invalidated with the run, or simply expired, and the last of those needs nobody to
do anything. That is what makes an abandoned run harmless rather than dangerous:
it is untidy, not exploitable. `LocalExecutionAbandoned` exists in the domain for
a sweeper that a later milestone may add.

This is recorded as a finding rather than smoothed away, because a reader who
notices the missing termination property should find that it was considered.

## Honest hardware evidence

**No local model generation has ever executed in AgencyOS.** What exists is
integration and capability detection, and the difference matters.

What is actually evidenced:

- the code **compiles against the real Windows AI APIs** at the pinned Windows App
  SDK 2.4.0 — `Microsoft.Windows.AI.Text.LanguageModel` with `GetReadyState`,
  `CreateAsync`, `GenerateResponseAsync` and
  `GenerateStructuredJsonResponseAsync`; `Microsoft.Windows.AI.AICapabilities`;
  `AIFeatureReadyState`; and `ExecutionProviderCatalog.GetDefault()
  .FindAllProviders()`;
- the capability probe and the runner are real adapters over those APIs, not
  stubs;
- the whole protocol is exercised end to end against a deterministic fake.

What this workstation is, measured rather than assumed:

- **HP Victus, 13th Gen Intel Core i7-13700H, NVIDIA GeForce RTX 4070 Laptop GPU,
  Windows 10.0.26200**;
- **no device of class `ComputeAccelerator` is present — this machine has no
  NPU**, and a Raptor Lake-H part does not have one;
- it is therefore **not a Copilot+ PC**, and the Windows `LanguageModel` that
  AgencyOS integrates against is not available to run on it.

So: **no NPU claim, no local-generation claim, no performance claim.** The
`SupportsTools = false` decision is a reading of the API contract, not of a
generation that happened. When a machine with an NPU is available, the probe will
answer for itself — which is the reason the probe reports readiness rather than
assuming it.

## Why this is better than the alternatives

**Letting the client assemble its own context.** The obvious design, and it makes
the workstation the authority on what it may read. Everything from M0 forward
would then rest on an unobservable process.

**A bearer token instead of a fingerprinted lease.** Authorizes whoever holds it
to execute *something*, with no way to check that what came back answers the
question that was asked.

**A long-lived lease per session.** Fewer round trips, and it converts a
single-use authorization into a standing one — precisely the property that makes
replay and context substitution checkable.

**Falling back to cloud when local fails.** Better availability, and it inverts
the one choice the person made.

**Emulating tool calls by parsing model JSON.** Would make the local provider look
equal to the cloud one, by routing a text generator's output into the tool runtime
through a path nobody guaranteed.

## Consequences

**What this buys.** Device-local inference exists behind the M12 architecture
rather than beside it. A lease is checkable, single-use and self-expiring; a
result is verifiable against the context it claims to answer; a workstation that
cannot run a model discloses nothing; and no path moves material to a provider the
person did not choose.

**What it costs.** Two extra round trips per local run. A local run fails where a
cloud run would have succeeded, visibly, by design. The classification recorded at
generation time can be more conservative than a later reclassification would
justify, and the result stays withheld — the safer error. And an abandoned run
stays non-terminal.

**What is deliberately not here.** No local model distribution, no model artifact
management, no fine-tuning, no embeddings, no vector store, no RAG index, no NPU
scheduling, no GPU inference path, no local database mutation of any kind. No C++,
Rust or Python. `task.create` is still the only canonical AI write.

**A verification limit, recorded rather than glossed.** The full local integration
and canonical verify **could not be run** for this milestone. The former
PostgreSQL endpoint on port 55432 depended on Docker Desktop; Docker Desktop's
Linux engine is currently unhealthy and returns HTTP 500; the separately installed
PostgreSQL 19 service requires elevation to start normally; a manual probe found
it listening on 5432 with unknown credentials, and it was stopped again to leave
the machine unchanged. This is **not** reported as green. Authoritative CI is
unaffected, because it provisions exactly `postgres:18.6` independently, and
promotion rests on that gate rather than on any local database evidence.

**What the next milestone inherits, and where the boundary is.** A residency
enum, a lease protocol with a formal model, a capability probe that reports rather
than assumes, and a provider that is honest about not calling tools. M13 ends
here: it does not widen the AI write surface, does not add model distribution, and
does not begin M14.
