# Repair Wave 003C — local work ledger

Every file this wave touched, and why. Recorded here rather than left to be
reconstructed from history, because no Git operation was performed during this
programme.

Baseline for the wave: executable `ada2310` (003A.2), working tree as it stood
before 003C.

---

## Production code

| File | Finding | Purpose |
| --- | --- | --- |
| `src/AgencyOS.Domain/Common/ConcurrencyConflictException.cs` | `AOS-R002-007` | The `409` sentence no longer quotes an identifier the product displays nowhere; it names the kind of record in words, keeps both versions, and says what to do next. `EntityId` is unchanged on the exception and still reaches the caller in the problem's `entityId` extension. |
| `src/AgencyOS.Api/Middleware/AgencyOsExceptionHandler.cs` | `AOS-R002-008` | A body the framework could not bind is refused by naming the field the caller sent and, where the contract allows it to be said safely, what that field should have looked like. The request class name never reaches the caller. |
| `src/AgencyOS.Windows/Pages/ProjectsPage.xaml.cs` | `AOS-R002-024` | Shows the server's reason (`Detail ?? Message`) instead of the problem's title. |
| `src/AgencyOS.Windows/Pages/PipelinePage.xaml.cs` | `AOS-R002-024` | The same. |
| `src/AgencyOS.Windows/Pages/PackagesPage.xaml.cs` | `AOS-R002-024` | The same. Found by the structural guard, not by the finding's own list. |
| `src/AgencyOS.Windows/Dialogs/LinkResearchItemDialog.xaml.cs` | `AOS-R002-024` | The same. Found by survey. |
| `src/AgencyOS.Windows/Dialogs/CreateContractDialog.xaml.cs` | `AOS-R002-024` | The same. Found by the structural guard. |

**No authorization, permission, role, membership, tenant-scoping,
existence-hiding, idempotency or concurrency behaviour was changed.** Only what
is said when the server has already decided.

## Tests

| File | Covers |
| --- | --- |
| `tests/AgencyOS.Tests.Unit/Refusals/ConcurrencyRefusalTests.cs` (new, 8) | `AOS-R002-007`: no identifier in the sentence, both versions present, a next step present, the kind of record read as words (including a Pascal-case compound), and an empty kind still producing a sentence. |
| `tests/AgencyOS.Tests.Integration/RefusalDetailTests.cs` (new, 13) | `AOS-R002-008`: the field is named, a nested path survives, the expected shape is described for the kinds we have words for, an unfamiliar kind is not guessed at, the request class never appears, a body with no readable field is still a refusal, and the real record-shaped serializer message resolves the member's type. Runs without a database. |
| `tests/AgencyOS.Tests.Windows/Presentation/RefusalPresentationTests.cs` (new, 8) | `AOS-R002-024`: no surface in the client shows a refusal by its title; the five repaired surfaces use the detail; and the rule itself is proved against four controls. |

## Reviewer

**None.** No harness file was changed by this wave.

## Documentation

| File | Purpose |
| --- | --- |
| `docs/reviews/repair-003c/REPAIR-003C-ADMISSION.md` | Admission table, and why `AOS-R002-014` is not admitted. |
| `docs/reviews/repair-003c/REPAIR-003C-LOCAL-WORK.md` | This ledger. |
| `docs/reviews/repair-003c/REPAIR-003C-REPORT.md` | The wave's report. |

## Temporary probes

| Probe | Purpose | Removed |
| --- | --- | --- |
| `tests/AgencyOS.Tests.Integration/TempJsonMessageProbe.cs` | To read the serializer's *actual* failure messages rather than assume them. It proved the assumption wrong: for a record request the serializer names the request type, not the member's type, whichever property failed. | **yes** — deleted before the wave's gates were run; the knowledge it produced is pinned by `RefusalDetailTests.ARecordRequestStillDescribesTheField`. |

That probe changed the repair. Without it the "expected" clause would have been
silently dead for every record request in the product — which is every request —
and the live check would have shown a field name with no expectation, exactly as
the first run did.

## Runtime evidence

`artifacts/reviewer/run-repair-003c-local/`

| Path | What |
| --- | --- |
| `refusal/` | The Pipeline domain refusal through the real client, with the InfoBar's text captured from the automation tree. |
| `conflict/` | A genuine stale-version `409` through the real client: the dialog was left open while the record was changed from the API. |
| `refusal-matrix.json` | Nine live cases across the status classes, including the authorized success path. |
| `logs/api.log` | Server log for the run. Contains no credential or connection string. |
