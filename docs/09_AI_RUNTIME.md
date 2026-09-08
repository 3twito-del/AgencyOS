# AI Runtime

## Architecture

AgencyOS does not embed provider calls into domain modules.

Use:

AgencyOS.AI.ModelGateway
    -> Provider adapters
    -> Tool registry
    -> Retrieval layer
    -> Evaluation/provenance layer

## Capability levels

1. READ
2. ANALYZE
3. PROPOSE
4. EXECUTE_REVERSIBLE
5. EXECUTE_WITH_APPROVAL
6. PROHIBITED_AUTONOMOUSLY

## Tool metadata

Each tool defines:
- capability name;
- required permission;
- risk class;
- input schema;
- output schema;
- idempotency behavior;
- audit behavior;
- approval policy.

Examples:
- people.search
- people.read
- interaction.create
- task.create
- deal.read
- deal.propose_counter
- contract.compare
- finance.read

## Provenance

Persist, when material:
- provider;
- model;
- model version;
- prompt/template version;
- tool calls;
- source record IDs;
- timestamp;
- latency;
- token/cost metadata when available;
- evaluation outcome.

## Nothing in M11 is AI

M11 records what the agency knows and thinks, and adds no model of any kind: no
provider client, no local model, no embeddings, no vector column, no semantic
search, no RAG, no summarization, no signal extraction, no thesis generation, no
sentiment analysis and no talent ranking. There is no route on the server that
would answer such a request.

This is deliberate and is the milestone's central design decision rather than a
deferral. The value of an intelligence record is that every judgment in it belongs
to a named person on a stated date; a summary with no author, or a claim extracted
from a document by a program, is a judgment nobody is accountable for, and the
moment one sits in the same list as a claim somebody staked their name on, the
list stops meaning anything.

When AI arrives in M12 it operates through the capabilities described above,
against the M11 record rather than instead of it — reading sources and signals,
proposing nothing that is not attributed, and mutating nothing directly. The
provenance section is what makes that possible, and M11 is what gives it something
provenanced to work with.

## Windows-local AI

Prefer a layered route:
1. Windows AI / OS capabilities where suitable.
2. Windows ML / ONNX/local model runtime.
3. Cloud ModelGateway.

Do not require local AI simply for novelty; use it where privacy, latency, offline use or cost justify it.
