# Prompt 009 — M12 AI Runtime

Implement AI only after the underlying domain tools exist.

Required architecture:
- ModelGateway abstraction;
- provider adapters;
- Tool Registry;
- capability/risk/permission metadata;
- source provenance;
- prompt/model version logging;
- evaluation harness;
- approval gates.

AI must never receive database credentials or bypass domain commands.

Start with:
- people.search
- people.read
- interaction.create (reversible)
- task.create (reversible)
- deal.read
- contract.compare

Do not enable autonomous sensitive legal/financial execution.
