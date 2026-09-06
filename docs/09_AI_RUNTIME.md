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

## Windows-local AI

Prefer a layered route:
1. Windows AI / OS capabilities where suitable.
2. Windows ML / ONNX/local model runtime.
3. Cloud ModelGateway.

Do not require local AI simply for novelty; use it where privacy, latency, offline use or cost justify it.
