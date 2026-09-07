# ADR-0016: Adopt OpenTelemetry now that there are failure paths worth tracing

Status: Accepted
Date: 2026-09-07
Supersedes: ADR-0004 (structured logging without telemetry export)

## Context

ADR-0004 decided, in M0, to ship structured JSON logging with W3C activity
tracking and *not* to wire OpenTelemetry. The reasoning was explicit: M0 had no
database, no authentication and no business endpoint, so there was no
consequential failure path to trace, and `CLAUDE.md` section 5 forbids
infrastructure with no current workload. It also predicted the condition that
would change the answer - "the first cross-process call, which is where a trace
becomes more informative than a log line".

M3 is that milestone. A single user action can now span the Windows client, a
queued command that survives a restart, a submission days later, an idempotent
replay of a request whose first attempt is invisible in any single log, and a
conflict that has to be explained to a human. Correlating that from log lines
alone means reconstructing causality by hand from timestamps.

## Decision

Adopt OpenTelemetry tracing and metrics in the API host, and correlate client
operations with server traces.

Instrumented:

- ASP.NET Core requests via its standard instrumentation, and Npgsql commands by
  subscribing to the `Npgsql` activity source by name - which is all the Npgsql
  instrumentation helper does, and avoids a package whose `AddNpgsql` extension
  collides with EF Core's;
- an `AgencyOS` activity source covering search, sync pull, queued-command
  submission, idempotent replay, conflict detection and readiness checks;
- counters for search queries, sync pulls, idempotent replays and conflicts,
  because those are the numbers that will answer "is the sync design working"
  without reading traces one at a time.

The client sends its operation identifier as the correlation id the API already
threads through logs and audit, so a queued command submitted on Wednesday can be
traced back to the intent captured on Monday.

**No vendor and no collector are added.** Exporters are configured only when an
OTLP endpoint is present in configuration; with none set, the instrumentation
runs and exports nowhere. That keeps the operational surface at zero until
somebody actually wants to look at it, which is the same restraint ADR-0004
applied, applied to the next layer up.

The structured JSON logging from ADR-0004 stays exactly as it is. This decision
supersedes only its deferral of OpenTelemetry, not its logging choices.

## Why this is better than the alternatives

Continuing to defer would mean the first milestone with genuinely distributed
failure modes is also the milestone with the least visibility into them. The
condition ADR-0004 named for reconsideration has been met, and honouring that is
the point of writing it down.

Adding a hosted observability vendor would give dashboards for free and would
introduce an external dependency, a cost centre and a data-egress question about
contact metadata - none of which M3 needs to answer to get traces.

Rolling correlation by hand on top of the existing logs would work for a while
and would reimplement, worse, what the platform already provides.

## Consequences

- Three OpenTelemetry packages and one Npgsql integration are added.
- With no OTLP endpoint configured, the cost is instrumentation overhead only.
- ADR-0004 remains the record of why telemetry was *not* added in M0. That
  reasoning was correct then; this supersedes its conclusion, not its logic.

## Migration / rollback

Additive and configuration-gated. Removing the exporter configuration returns the
system to ADR-0004's posture without code changes.

## Evidence / metrics that would cause reconsideration

- Instrumentation overhead appearing in latency profiles.
- A need for long-term retention or alerting, which is where a collector or
  vendor starts to earn its place.
