# AI Runtime

Implemented in M12. See `docs/adr/ADR-0031-ai-runtime-untrusted-models-and-the-approval-protocol.md`
for why each of these is the way it is.

## The principle everything follows from

**The model is not a trusted application component. Its output is untrusted
input.**

It may read what the current user is authorized to read, reason over what it is
given, ask for a registered tool, draft prose, and propose canonical actions.

It may not reach PostgreSQL, invent authorization, bypass a domain handler,
bypass optimistic concurrency, bypass idempotency, bypass M10 send semantics,
bypass finance posting rules, bypass M11 provenance rules, grant itself
permissions, treat retrieved content as instructions, or change business state.

## The chain

```
MODEL GATEWAY
  -> MODEL INVOCATION
    -> TOOL REGISTRY
      -> AUTHORIZED CONTEXT
        -> AGENT RUN
          -> TOOL CALL
            -> APPROVAL
              -> CANONICAL COMMAND
                -> RESULT / PROVENANCE
```

Nothing skips a link. There is no route, tool or code path that reaches a
canonical command without an approval behind it.

## Components

| Piece | Type | What it is responsible for |
| --- | --- | --- |
| Model gateway | `AgencyOS.Infrastructure.Ai.ModelGateway` | Choosing the provider, checking model capability, applying the timeout, normalizing failure. Everything provider-shaped stops here. |
| Provider | `IModelProvider` | One adapter per provider. `FakeModelProvider` is registered in every environment. |
| Context assembler | `AiContextAssembler` | The only path from AgencyOS data to a provider. Reads through authorized queries, classifies, applies policy, marks untrusted content, records provenance, bounds size. |
| Data policy | `ModelDataPolicy` + `AiProviderPolicy` | Whether this material may be transmitted to this provider for this organization. |
| Tool registry | `AiToolRegistry` | A closed, versioned list. Refuses duplicate names at construction. |
| Runtime | `AgentRuntime` | The bounded loop: call, validate, execute reads, park writes, stop. |
| Approval | `AiApproval` + `AiToolRequest` | One exact action, bound by fingerprint, decided by one person, executed once. |
| Run | `AgentRun` + `AgentRunStep` | What was asked, what happened, in order, append-only. |

## Agents

A closed set. There is no general assistant: an agent whose task is unbounded
cannot say what context it needs, what tools it should hold, or when it is
finished.

| Agent | Tools | Canonical write |
| --- | --- | --- |
| `ResearchCopilot` | `research_case.get`, `signals.search`, `agency.search`, `task.create` | yes |
| `RelationshipBrief` | `person.get`, `company.get`, `relationship.intelligence`, `signals.search`, `task.create` | yes |
| `DealBrief` | `deal.get`, `task.create` | yes |
| `ContractBrief` | `contract.get`, `task.create` | yes |
| `FinanceBrief` | `receivables.list` | no — money is strictly read-only |
| `CommunicationDraft` | `person.get`, `company.get` | no — AgencyOS never sends from a run |

## Tools

Each tool declares a name, a version, a description, a JSON schema, an effect and
a required permission. Every one derives from `AiToolBase`, whose `ExecuteAsync`
is sealed and re-checks the permission as its first act — a run may sit awaiting
approval for half an hour, and a grant revoked in between must stop it.

Effects:

- `ReadOnly` — runs immediately, needs nobody.
- `CanonicalWrite` — proposed, and does not happen until a person approves it.
- `ExternalEffect` — refused at construction and by a CHECK constraint. No tool
  in this build has one.

**`task.create` is the only canonical write.** It is a facade over
`CreateTaskHandler`: the same handler a person reaches, with the same validation,
the same audit entry and the same actor.

Never exposed as a tool: `DbContext`, arbitrary SQL, filesystem access, shell
execution, environment variables, provider secrets. An architecture test asserts
the constructor of every tool takes none of them.

## What may be transmitted

Authorization to read is not permission to transmit. `AiProviderPolicy` is a
per-organization, per-provider row, and the default is closed — the absent row and
the disabled flag both mean no.

`ModelDataSensitivity` is the single scale every classification maps onto:

| Source | Value | Maps to |
| --- | --- | --- |
| M11 `IntelligenceSensitivity.Confidential` | | `Confidential` |
| M11 `IntelligenceSensitivity.SourceSensitive` | | `Protected` |
| M10 `DocumentSensitivity.Financial` | | `Confidential` |
| M10 `DocumentSensitivity.Privileged` | | `Protected` |
| M10 `MailboxVisibility.Private` | | `Protected` |
| Any `Restricted` | | `Restricted` |

The mapping is pessimistic: where a classification could reasonably land on two
levels, it lands on the higher one.

**No ceiling reaches `Restricted`.** The aggregate refuses it, `Permits` refuses
it independently, and a CHECK constraint refuses the row. There is no
administrator, role or flag that transmits restricted material.

Provider credentials live in server configuration and never in PostgreSQL. The
policy screen does not report whether one is configured.

## Untrusted content

Everything a person or an outside system wrote is `ContextTrust.Untrusted`: email
bodies, document text, source excerpts, notes, a claim somebody typed. It is
fenced, labelled as data, and accompanied by a sentence saying it is not an
instruction. A fence sequence inside content is neutralized rather than escaped.

**None of that is the defence.** A model that ignores the framing entirely still
cannot call an unregistered tool, still cannot execute a write without a person,
and still cannot cite an object it was not given. The framing reduces confusion;
the registry and the approval make confusion survivable.

A block is transmitted whole or not at all. There is no redaction: a partially
redacted block reads exactly like a complete one. Omissions are counted and never
described — the model is told its view may be incomplete, not how much was
withheld or of what kind.

## The approval protocol

1. The model asks for a tool. The runtime validates it against the registry, the
   agent's allow-list, the caller's permission and the argument shape.
2. A `CanonicalWrite` becomes an `AiToolRequest` in `AwaitingApproval`, with its
   arguments canonicalized and fingerprinted, and an `AiApproval` addressed to the
   person who started the run. The run parks.
3. The person sees what AgencyOS says would happen, written from the validated
   arguments — never the model's account of its own request. The dialog defaults
   to **Reject**.
4. Deciding is its own act and completes on its own.
5. Executing is a second call. It recomputes the fingerprint from what is about to
   run, re-checks the permission, re-checks the tool version, and may still be
   refused by the domain.

The fingerprint is SHA-256 over organization, run, tool name, tool version and
canonical arguments, joined on a unit separator. Recomputed at execution, never
read back — that is what makes rewritten arguments fail.

Approvals last thirty minutes. `Expired` is distinct from `Rejected` because
nobody made it. There is no standing approval and no "don't ask again".

An approval is answered only by the person it was put to. Reading, deciding and
executing all narrow to `RequestedOf` and all answer as missing.

## Limits

| Bound | Value |
| --- | --- |
| Model turns | 6 |
| Tool calls | 12 |
| Output tokens | 4,000 |
| Context characters | 60,000 |
| Tool result characters | 12,000 |
| Tool result rows | 25 |
| Provider timeout | 120s |
| Wall clock | 10 minutes |
| Approval validity | 30 minutes |

The loop is not autonomous. It calls, validates, executes reads, parks writes and
stops — at an answer, at a limit, or at a refusal.

## Provenance and privacy

A run records the provider, the model key, the prompt template id and version, the
step history, the tool requests, the counts and the outcome.

It does **not** record or return the system prompt, the provider request, or the
response body. The prompt version identifies which wording ran, which is what
reproducing a run needs; the wording lives in source control.

A run is private to the person who started it. There is no permission that grants
reading another person's runs, and the read side narrows in SQL.

`AgentRunStep` is append-only, enforced by a trigger. Every kind names its actor,
so a reader can always tell what the model said from what AgencyOS did.

Citations use `[cite:Kind:id]` and are resolved against the blocks the run actually
assembled. One that does not resolve is stripped rather than reported.

## Failures

`AgentFailureKind` categorizes every failure: provider unavailable, timeout, rate
limited, invalid response, policy refused, authorization refused, context too
large, tool failed, command refused, limit reached. A provider exception's message
is logged and never returned.

## Telemetry

Duration, provider, model, outcome, tool-call count. **Never** the prompt, the
answer, a fragment of either, a document body, a communication body, or a provider
key.

## Testing

CI runs entirely against `FakeModelProvider` and needs no network and no
credential. Promotion to ALPHA does not depend on live external provider
availability.

The prompt-injection corpus asserts what AgencyOS does with hostile content — that
it is enveloped, labelled, unable to close its own fence, and never in the system
role. It does not assert that a model resists injection; AgencyOS cannot test that
and does not depend on it.

`specs/AiApproval.tla` model-checks the approval-to-execution protocol under every
interleaving of deciding, expiry, revocation, cancellation and retry.

## Nothing in M11 is AI

M11 records what the agency knows and thinks and adds no model of any kind. That
remains true: M12 operates *against* the M11 record rather than instead of it. It
reads sources and signals, proposes nothing that is not attributed, and mutates
nothing directly. A proposed signal is accepted by a person, and the acceptance
runs the existing M11 command as that authenticated person.

## Windows-local AI

Deferred to M13 and not present in this build. When it arrives, prefer a layered
route:

1. Windows AI / OS capabilities where suitable.
2. Windows ML / ONNX / local model runtime.
3. Cloud model gateway.

Do not require local AI for novelty; use it where privacy, latency, offline use or
cost justify it.

## M13 — running the model somewhere else

### Residency is not authority

`ModelResidency` has three values: `ExternalCloud`, `OrganizationControlled`,
`DeviceLocal`. It records which machine ran the arithmetic. It confers nothing.

A `DeviceLocal` run is authorized by the server, before disclosure, exactly as a
cloud run is. The argument this refuses is worth stating because it is genuinely
persuasive: the material is already on the machine, the person may already read
it, so what is being protected? The answer is that "already allowed to read it" is
a statement about a moment, and authorization is a server decision. A workstation
that assembled its own context would have made itself the authority on what it may
see, and every guarantee from M0 forward would then rest on a process AgencyOS
does not control.

### Restricted is refused before the provider is looked up

Not after checking whether the provider is local — before consulting any policy at
all. At `DeviceLocal` the exception argument is at its most convincing and still
wrong: "restricted" would come to mean "restricted unless the model runs nearby",
and a classification whose meaning depends on an execution detail is not a
classification.

### The context lease

Context reaches a device only under an `AiContextLease`: single-use, ten minutes,
bound to the organization, user, run, subject kind and id, residency, model key,
policy version, and a SHA-256 fingerprint over the binding fields and the rendered
context.

The client half of this protocol is unobservable. AgencyOS hands over context and
later receives text; it cannot watch what happened in between. The lease makes the
return checkable without trusting the machine: a result is accepted only against a
lease still `Issued`, matching every binding field, carrying a fingerprint the
server recomputes for itself. If the context changed underneath, the recomputed
fingerprint differs and the result is refused — it is an answer to a question
nobody asked.

The fingerprint is **not** a confidentiality mechanism. The context is disclosed to
the device in the clear, because the device has to read it.

`AiContextLease.Issue` throws for `ExternalCloud`. There is no lease that could
authorize a fallback.

### Capability first, then the lease

The client probes readiness **before** requesting a lease, because issuing the
lease *is* the disclosure. A workstation that cannot run the model gets zero
leases and sees no context.

### No silent fallback

A device-local run that cannot execute fails with a category —
`LocalProviderUnavailable`, `LocalModelNotReady`, `LocalExecutionAbandoned` — and
sends nothing anywhere else. Somebody choosing device-local residency is usually
choosing it for the material; re-routing that material because the local model was
busy would invert the one choice they made. Failing visibly is the feature.

### The local result is untrusted, and the client adds nothing

Returned text is fenced as untrusted data and has its citations validated against
what the run was given, exactly as a cloud answer does. `task.create` remains the
only canonical AI write. The client renders the leased context, calls the model,
returns what came back and reports the device — it does not summarize, re-prompt,
retry with a different model, or interpret.

### `SupportsTools = false`, and no emulation

The Windows `LanguageModel` API has `GenerateResponseAsync` and
`GenerateStructuredJsonResponseAsync` and **no function-calling contract**. Faking
one by parsing model JSON into tool calls would route a text generator's output
into the tool runtime through a path the provider never guaranteed, on the least
controlled machine in the system. The Relationship Brief needs prose, not tools.

### A stored result re-authorizes on every read

`ResultSensitivity` is recorded at generation time. Reads above `Internal` re-check
`AiSensitiveUse`; a reader who no longer holds it gets the run with
`ResultWithheld = true` and no result. Recorded rather than recomputed, because
recomputing asks a different question and needs context the reader may no longer
be allowed to see.

**The limit, plainly:** this governs what AgencyOS will show from now on. It does
not reach a copy somebody already read, pasted or remembered. The mitigation is
upstream, in the classification that decided what could be sent at all.

### What has not happened

**No local model generation has ever executed.** The code compiles against the real
Windows AI APIs, the probe and runner are real adapters, and the protocol is
exercised end to end against a deterministic fake. The development workstation has
no NPU and is not a Copilot+ PC, so the Windows `LanguageModel` cannot run on it.
No NPU claim, no local-generation claim, no performance claim (ADR-0035).

