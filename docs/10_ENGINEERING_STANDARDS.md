# Engineering Standards

## C# / .NET
- nullable reference types on;
- warnings-as-errors progressively for owned projects;
- analyzers enabled;
- async cancellation propagated;
- no fire-and-forget side effects;
- explicit domain commands;
- value objects for important concepts;
- no business logic in UI code-behind.

## F#
- use where discriminated unions/state modeling reduce illegal states;
- interop boundary must be clean and documented;
- do not duplicate C# domain model without a correctness reason.

## Rust
- deny unsafe by default; isolate/document unavoidable unsafe blocks;
- fuzz parsers;
- stable toolchain for promoted components unless approved otherwise.

## C++
- modern C++ only;
- RAII;
- sanitizers/tooling in LAB where supported;
- dual MSVC/Clang builds for portable native modules;
- isolate Win32 ownership/resource lifetime.

## Python
- typed Python;
- Pydantic contracts where service boundaries exist;
- lock dependencies;
- separate experimentation notebooks from production service code.

## SQL
- explicit constraints;
- indexes justified by query patterns;
- migrations reviewed;
- no application-only enforcement for invariants that belong in DB constraints;
- measure before denormalizing.

## API
- version contracts;
- idempotency keys for external side effects;
- structured problem details/errors;
- pagination;
- optimistic concurrency where appropriate.

## Documentation
Every significant architectural decision -> ADR.
Every module -> module spec.
Every cross-boundary event -> contract documentation.
