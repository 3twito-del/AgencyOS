# Audit 002 Phase D — the 235 GB capture

§11. Phase C's scratch capture of the API's stdout reached **235 GB** and filled
the volume, which stopped a solution build mid-audit.

The question §11 asks first is attribution, and the answer is measured rather
than assumed: **the product did not emit 235 GB.**

---

## 1. What was measured

The API was restarted against the review database with a fresh capture, and the
file was measured under three controlled conditions.

| Condition | Growth | Rate |
| --- | ---: | --- |
| Startup to first healthy response | 104 KB | one-off |
| 20 × `GET /organizations/{id}/people` | 1,972,846 bytes | **~96 KB per request** |
| Idle, no client, 60 s | 81,856 bytes | **1,364 bytes/sec** |
| Idle, client running and idle, 60 s | 81,856 bytes | **unchanged** |
| A real 4-dialog reviewer run across ~25 tabs | 9 MB | **~2.3 MB per dialog attempt** |

### What dominates a request

Per request, at `Default: Debug`:

| Category | Lines per 20 requests |
| --- | ---: |
| `Microsoft.EntityFrameworkCore.Database.Command` | 980 |
| `Microsoft.EntityFrameworkCore.Database.Connection` | 640 |
| `Microsoft.EntityFrameworkCore.ChangeTracking` | 60 |
| everything else | 120 |

EF Core at `Debug` logs each statement twice — once as `Executing DbCommand`
and again as `Executed DbCommand` — and each entry carries the full SQL text
twice more, in `Message` and in `State.commandText`. That is where the 96 KB
goes.

### Why that does not add up to 235 GB

235 GB ÷ 96 KB ≈ **2.4 million requests**, or 48 requests per second sustained
for fourteen hours. The dialog pass sleeps hundreds of milliseconds between
steps; the measured cost of a real pass is ~2.3 MB per dialog attempt, so every
Phase C pass over all 63 dialogs accounts for roughly **140 MB**, and every pass
run in the entire audit for a couple of gigabytes.

**Roughly 99% of that file was not product output.**

---

## 2. Source attribution

| § | Question | Answer |
| --- | --- | --- |
| A | How much does the API actually emit? | ~96 KB per request, ~1.4 KB/s idle — high, and explained entirely by `Default: Debug` |
| B | How does the reviewer capture it? | The API was started as a background process with `> scratch.log 2>&1`, unbounded |
| C | Do capture loops duplicate content? | Not established. The most likely mechanism is repeated API restarts redirecting to the same path while an earlier handle was still open, which on Windows leaves the old handle writing at a stale offset and zero-fills the gap. **The original files were truncated to recover the disk before this could be confirmed**, so it is named as probable, not proven. |
| D | Does one message dominate? | Yes, by category: EF Core `Database.Command` at `Debug` is 60% of lines and more than that by bytes |
| E | Is API logging pathological? | **No.** The shipping configuration is `Default: Information`, `Microsoft.AspNetCore: Warning`. `Debug` is `appsettings.Development.json`, a deliberate developer setting, and it behaves exactly as `Debug` is meant to |
| F | Is it only unbounded scratch capture? | That is where the two orders of magnitude are |

---

## 3. Containment implemented

In the reviewer and the audit tooling only. **No product logging was touched.**

### `BoundedCapture`

`tools/AgencyOS.Reviewer/Runtime/BoundedCapture.cs`. The policy keeps three
things and drops the fourth:

- **the beginning**, up to a documented budget — startup is where configuration
  problems appear;
- **every line that looks like a failure**, whatever the budget — error, warning,
  critical, exception, unhandled, `fail:`;
- **a rolling tail** of the most recent lines — the end is where the run stopped;
- and it drops the routine middle, writing an explicit marker where the drop
  began and a count of what was dropped.

Fourteen tests in `BoundedCaptureTests` pin it, and the one that matters is
`AFailureIsKeptNoMatterHowLateItArrives`: a bound that discarded failure context
would make every future audit cheaper and useless.

### Disk safety gate

`Native.FreeSpaceBytes` plus a check in the dialog-runtime loop: if free space on
the volume the run is writing to falls below 5 GB, the pass stops between
dialogs, says so, and keeps the evidence it already has. Phase C's run died
mid-dialog instead, which cost the run rather than a dialog.

---

## 4. Is a product finding warranted?

**No, and one is deliberately not filed.**

`Default: Debug` in a Development appsettings file is a developer choice that
behaves as documented. The shipping configuration is `Information` / `Warning`.
Nothing here shows AgencyOS logging abnormally for the level it was asked to log
at.

One thing is recorded as an **observation, not a finding**: a single
`GET /organizations/{id}/people` for a 50-row list produced 49 EF Core command
log lines, which is roughly 16 database round trips. Whether that is legitimate
eager loading or an N+1 read was **not** investigated — Phase D is a closure pass
and §0 freezes that ground. It is noted here so somebody can look deliberately
rather than discover it the way this was discovered.

If a future wave does look, it belongs in an engineering/operational group, not
in 003A–003F.

## Evidence

- `artifacts/reviewer/run-002-phase-d/logs/` — the measurement captures
- `tools/AgencyOS.Reviewer/Runtime/BoundedCapture.cs`
- `tests/AgencyOS.Tests.Reviewer/BoundedCaptureTests.cs`
