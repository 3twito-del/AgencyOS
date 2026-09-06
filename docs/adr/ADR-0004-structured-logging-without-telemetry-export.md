# ADR-0004: Structured JSON logging now, OpenTelemetry export when there is something to trace

Status: Accepted
Date: 2026-09-06

## Context

`CLAUDE.md` section 2 names "OpenTelemetry + structured logging + Windows-native
diagnostics" in the observability baseline. `prompts/001_M0_REPOSITORY.md` asks
M0 for a "structured logging foundation".

`CLAUDE.md` section 5 forbids adding infrastructure before the capability is
required, and section 7 requires telemetry for consequential failure paths. M0
has no consequential failure path: there is no database, no authentication and
no business endpoint.

## Decision

1. Hosts call `AddAgencyOSStructuredLogging()` from
   `AgencyOS.Infrastructure.Logging`, which emits one JSON object per line via
   the built-in JSON console logger with scopes and UTC timestamps.
2. Activity tracking is enabled now for trace id, span id and parent id, so
   correlation identifiers are present in logs before any exporter exists.
3. No OpenTelemetry package is referenced in M0. Exporters arrive with M1, when
   authentication, audit and persistence create paths worth tracing.

## Why this is better than the alternatives

Serilog would add a dependency and a second configuration model to do what the
platform logger already does structurally. The built-in JSON console logger
produces machine-readable output with no new package.

Wiring OpenTelemetry now would mean an exporter with no spans, a collector
endpoint with no destination, and configuration nobody can validate. Enabling
activity tracking captures the part that is expensive to retrofit - correlation
identifiers threaded through the logs - without the part that is cheap to add.

## Consequences

- Logs are structured and correlated from the first commit.
- There is no distributed trace export, and no metrics pipeline, until M1.
- `AgencyOS.Infrastructure` takes exactly one package reference,
  `Microsoft.Extensions.Logging.Console`.

## Migration / rollback

Adding OpenTelemetry is additive: a hosting package, exporter configuration and
instrumentation registration alongside the existing logging call. Nothing here
needs to be undone.

## Evidence / metrics that would cause reconsideration

- The first cross-process call, which is where a trace becomes more informative
  than a log line.
- A failure that log correlation alone cannot diagnose.
- A need for metrics, which the logging pipeline does not address at all.
