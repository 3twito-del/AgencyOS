# Local release-candidate report

```
LOCAL RELEASE CANDIDATE = READY
AUTHORITATIVE VALIDATION / VERSIONING PENDING
```

**No owner decision remains open.** `AOS-R002-003` was the last blocker; the owner
decided it and it is repaired (see the addendum). Every known finding is repaired,
accepted, intentionally unsupported or deferred with evidence, and every local gate
is green.

This means local implementation and evidence are complete. It does **not** mean
released, `postgres:18.6`-validated, CI-validated, Nightly-validated, versioned or
promoted.

**Date:** 2026-09-18 · **Tree:** the cumulative local repair tree
**Git operations:** none · **CI / Nightly:** not dispatched

---

## 1. Starting local counts

| Gate | Before this pass | After |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,814 | **3,832** |
| Windows | 1,031 | **1,038** |
| Reviewer | 163 | **163** |
| LAB integration | 864 / 864 | **871 / 871** |
| OpenAPI | 263 / 187 | **263 paths / 187 schemas** |
| API contract | 14 | **14** |

## 2. `AOS-R002-020` — mailbox visibility · **REPAIRED**

`ConnectMailboxDialog` offered `Organization`, which `MailboxVisibility` does not
have and the server refused with
`400 "Visibility 'Organization' is not valid."` The option is gone; `Private` and
`Shared` remain. The enum, the permissions, the schema and the API were not
touched — organization-wide mailbox reading would be a new authorization
capability, not a missing line.

## 3. `AOS-R002-025` — `500` for a missing nested object · **REPAIRED**

**Boundary:** `APPLICATION_MAPPING`. The body bound correctly; `Party` was null,
and `EndpointParsing.ToEndpoint` guarded it with
`ArgumentNullException.ThrowIfNull` — the exception for a programmer's mistake,
which the handler rightly answers as `500`.

| | |
| --- | --- |
| Before | `500` with a trace id |
| After | `400 "Participant is required."` — measured live |

It now refuses the way the file's own `ParseEnum` already did. **The published
contract already declared `party` required**, so the server was the one
disagreeing; no contract change was needed.

**Three sibling sites shared the same root** and were repaired with it:
`M3Endpoints.ToDefinition`, and `ParseValue` in `M7Endpoints` and `M8Endpoints`.

## 4. `AOS-R002-026` — the instant an operator could not see · **REPAIRED**

`RecordPitchDialog` and `RecordSubmissionDialog` now offer **date and time**, both
editable, defaulting to local now. `ResponseExpectedBy` stays a date picker,
because it is a `DateOnly` and always was.

The offset used is the one in force **on the chosen date**, not today's — a
January meeting recorded in July is not written an hour out. Canonical UTC
normalization stays where `AOS-R002-001` put it; nothing here converts anything.

12 unit tests: both halves survive, midnight is kept, the end of the day does not
roll over, the offset belongs to the chosen date, and an instant round-trips at
`+03:00`, `-05:00` and `0`.

## 5. The four owner/lead identifier fields · **REPAIRED**

`CreateDealDialog`, `CreateContractDialog`, `CreateOpportunityDialog` and
`CreatePackageDialog` choose from the organization's people instead of taking a
typed GUID, backed by `ListOrganizationMembersAsync` — the same directory 003E
used for representation teams.

Whoever is signed in is preselected, **proved by the directory's own `IsSelf`**
rather than guessed, and every other member stays selectable.

Their exemptions were removed from the raw-identifier guard, so the rule now
enforces them like every other field.

## 6. `LinkRecordDialog` · **REPAIRED for 12 of 14 kinds**

| | Kinds |
| --- | --- |
| Chosen from a list | Person, Company, TalentProfile, Project, Package, Opportunity, Submission, Deal, Offer, Contract, Invoice, Payment |
| Still by identifier | **Material**, **ContractVersion** |

A material belongs to a person and is listed per person; a version belongs to a
contract and is read from that contract. Neither has a list of its own, so
choosing one means choosing its parent first — a shape this dialog does not have.

**The two kinds were not removed.** A link that was legal yesterday stays legal:
the dialog shows the identifier box for exactly those two and says why. This is
the design gap §15 anticipated.

## 7. `AddIntelligenceSubjectDialog.IdBox` · **ACCEPTED**

Reconfirmed on this tree: the runtime sweep reports it
`UNREACHABLE_NOTHING_CONSTRUCTS`. Nothing opens it, so its raw identifier is
unchanged. Productising an unreachable dialog for consistency is work nobody can
use.

## 8. Fire-and-forget · **DEFERRED as a pattern, one sub-class REPAIRED**

**230 sites, classified statically:**

| Class | Count |
| --- | --- |
| Load / refresh, error state owned by a view model | 74 |
| Command dispatch | 150 |
| Background best-effort | 4 |
| Opener / navigation | 2 |

No reproduced defect is attributable to the pattern itself. The programme's real
failures were something else each time: 003A's opener defect was initialization,
003A.1's crash was a layout cycle.

**One concrete sub-class was reproduced**, so it was repaired. Thirteen page
commands called the server inside a dispatched, unawaited task with no handling.
As an observer, whose thesis create answers `403`: the dialog closed, nothing was
created, **no error appeared anywhere**, and focus landed on the navigation pane.
Now each handles its own refusal — *"Could not state the thesis — Forbidden"* —
pinned by a structural test with positive and negative controls. **The other 217
sites were not touched.**

## 9. `MailboxSynchronizer` · **REPAIRED**

`ProviderMessage.SentAt` and `ReceivedAt` are `DateTimeOffset?` with no documented
UTC requirement, and reached persistence untouched. The columns are `timestamptz`,
which Npgsql writes only at offset zero.

**Reproduced deterministically** through the provider interface itself — nothing
unsupported invented:

```
System.ArgumentException : Cannot write DateTimeOffset with Offset=03:00:00 to
PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.
```

Normalized at the ingestion boundary, for the same reason `AOS-R002-001`
normalizes once at the HTTP boundary. The instant is unchanged — `09:30+03:00`
reads back as `06:30Z` — and the test asserts exactly that.

## 10. Representation with no workspace visibility · **ACCEPTED**

**The earlier observation was wrong, and the correction matters.** It was made
with default filters only.

| Question | Answer |
| --- | --- |
| Is the state valid by design? | Yes — a representation belongs to a person; a talent profile is a separate thing. |
| Is a talent profile required by invariant? | No. Conversion succeeds without one. |
| Where does an operator find it? | The person stays in the **People** directory, and the **Prospects** workspace lists the converted prospect when "Open only" is cleared. |

Measured live: clearing the filter grew the list from 2 rows to 4, and the row
read `A, Review Owner, Converted`. The record is reachable through existing
surfaces, so nothing was changed.

## 11. `contract` gate idempotence · **REPAIRED**

**Classification:** `REPOSITORY_MUTATION`. The gate deleted `artifacts/openapi`
and then ran an *incremental* build; with nothing to rebuild the generation target
never ran, so a second run produced no document and the gate failed on its own
output. The generated bytes were never the problem.

Fixed with `--no-incremental`. Two consecutive runs of the canonical gate now both
produce `942d0e74…` — same source, same bytes, no difference on the second run.

## 12. Newly discovered

| # | What | Disposition |
| --- | --- | --- |
| 1 | A policy-level `403` returns no problem document, so it reads `"Forbidden"` rather than `AOS-R002-014`'s capability sentence | recorded — the authorization pipeline is out of scope here |
| 2 | A `Guarded(title, Method)` dispatch hides the command from every source scanner | **found and corrected during this pass** |

### On the second

My first shape for the fire-and-forget repair dispatched
`_ = Guarded("title", MethodAsync)`. The sweep then reported **13 dialogs
unreachable**, 3 accessibility findings and 7 refused keystrokes — because the
reviewer's reachability analysis follows `_ = Method()` and could no longer see
the command. The dialogs still opened for a person; the analysis had gone blind.

That analysis is what four waves of evidence rest on, so the repair was reshaped
rather than the measurement: dispatch names the command directly again, and the
guarding moved inside each method. The re-run sweep is identical to baseline. The
same indirection had also produced two false positives when re-measuring
`AOS-R002-003`.

## 13. Final finding ledger totals

[The ledger](FINAL-FINDING-LEDGER.md) — 35 distinct findings, every one disposed:

| Disposition | Count |
| --- | --- |
| `REPAIRED` | 23 |
| `ACCEPTED_CURRENT_BEHAVIOR` | 4 |
| `INTENTIONALLY_UNSUPPORTED` | 2 |
| `DEFERRED_NO_REPRODUCED_DEFECT` | 1 |
| `DESIGN_DECISION_REQUIRED` | **1 open** |
| Closed by the audit as harness error | 3 |

## 14–21. Gates

| | |
| --- | --- |
| **build** | 0 warnings / 0 errors |
| **unit** | 3,832 |
| **Windows** | 1,038 |
| **Reviewer** | 163 |
| **LAB integration** | **871 / 871** |
| **OpenAPI** | 263 paths / 187 schemas, `942d0e74…`, byte-identical across two forced regenerations |
| **contract** | version 14; gate idempotent across two consecutive runs |
| **dialog sweep** | **65 / 54 opened / 7 / 4 — zero changed outcomes**, client never closed, 0 refused keystrokes, 0 accessibility findings |

## 22. Schema impact

**None.** No migration, table, column, type or index changed.

## 23. Contract / API impact

**None.** No endpoint, contract type, status code, permission, role, grant, tenant
rule, concurrency or idempotency behaviour changed. `AOS-R002-025` changed which
exception a mapping helper throws, which changes a status from `500` to `400` —
the published contract already required the field.

## 24. Remaining design decisions

**One, and it blocks the release-candidate criteria:**

### `AOS-R002-003` — seventeen dialogs with no opening control

- **Re-measured on this tree: 15**, of which **13 are the Intelligence
  workspace's entire authoring surface** — record a source, record a signal,
  state a thesis, revise it, retire it, create a prediction, forecast it, resolve
  it, create a watchlist, add a radar entry, open a research case, attach to it,
  and the reason dialog.
- `ownerDecisionRequired: true`. Never admitted to any wave.
- **It was not among the items this pass was asked to reconcile.** It surfaced
  because §14 required every finding to carry a disposition.
- The decision: is palette-only an acceptable shape for an authoring surface, or
  does every authoring dialog need a control? ADR-0032 supports the palette as the
  keyboard-first way to do anything, which is an argument that this is deliberate;
  thirteen dialogs with no button is an argument that it is not.

**Also open by design, and permitted by §15:** `LinkRecordDialog`'s two
parent-scoped kinds (§6).

## 25–28. Authoritative state

| | |
| --- | --- |
| `postgres:18.6` validation | **PENDING** |
| Canonical CI | **PENDING** |
| Nightly | **PENDING** |
| Git operations | **NONE** |

PostgreSQL 19 is LAB corroboration only. A behaviour differing between 19 and
18.6 would not have been caught by anything done here.

---

## Status

```
LOCAL RELEASE CANDIDATE = NOT READY
```

**Blocker: `AOS-R002-003`** — an unresolved owner decision, not a defect and not a
failing gate. Every other criterion in §15 is met: all clear correctness findings
are repaired, every remaining item has an explicit disposition, no unresolved
S1/S2 correctness defect remains, all local suites are green, LAB integration is
green, OpenAPI is deterministic and byte-identical, the contract gate is
idempotent, the dialog regression is identical to baseline, no product process
crashed, and no new raw-identifier regressions exist for a normal user.


---

# Addendum — `AOS-R002-003` resolved

**Date:** 2026-09-18, after the report above. Additive; §24's blocker is closed and
the status at the top is updated to match.

## 1. The exact fifteen

Re-measured after the `Guarded(...)` indirection was removed — which is what had
produced false positives the first time.

| Dialog | Surface | Ordinary authoring | Palette command | Opener before | Disposition |
| --- | --- | --- | --- | --- | --- |
| `RecordSourceDialog` | Intelligence | yes | `intelligence.source.record` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `RecordSignalDialog` | Intelligence | yes | `intelligence.signal.record` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `ChangeVerificationDialog` | Intelligence | yes | `intelligence.signal.verification` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `CreateThesisDialog` | Intelligence | yes | `intelligence.thesis.create` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `ReviseThesisDialog` | Intelligence | yes | `intelligence.thesis.revise` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `IntelligenceReasonDialog` | Intelligence | yes | `intelligence.thesis.retire`, `intelligence.radar.dismiss` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `CreatePredictionDialog` | Intelligence | yes | `intelligence.prediction.create` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `RecordForecastDialog` | Intelligence | yes | `intelligence.prediction.forecast` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `ResolvePredictionDialog` | Intelligence | yes | `intelligence.prediction.resolve` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `CreateWatchlistDialog` | Intelligence | yes | `intelligence.watchlist.create` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `AddRadarEntryDialog` | Intelligence | yes | `intelligence.radar.add` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `OpenResearchCaseDialog` | Intelligence | yes | `intelligence.research.open` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `LinkResearchItemDialog` | Intelligence | yes | `intelligence.research.link` | none | `DISCOVERABLE_VIA_INTELLIGENCE_LAUNCHER` |
| `RecordInvoiceDialog` | Finance | yes | `invoice.record` | none — every sibling had one | `DISCOVERABLE_VIA_EXISTING_CONTEXTUAL_OPENER` |
| `ApproveAiActionDialog` | AI | approval | `ai.approve` | **`Click="OnApprove"`, AiPage.xaml:186** | `DISCOVERABLE_VIA_EXISTING_CONTEXTUAL_OPENER` |

No entry is `UNKNOWN`, `PALETTE_ONLY_BY_ACCIDENT` or unclassified, and none needed
`INTENTIONALLY_NO_VISIBLE_OPENER`.

**`ApproveAiActionDialog` already had a button** — a false positive from a scanner
that follows dispatch rather than direct construction. Nothing was manufactured.

## 2-3. The thirteen, and the other two

Thirteen reached by one launcher. `RecordInvoiceDialog` gained the button its
fourteen Finance siblings already had, beside the receivable list it bills from.
`ApproveAiActionDialog` was left alone.

## 4. The launcher

A single `DropDownButton` in the workspace header — content **"New"**, accessible
name **"New intelligence record"** — whose `MenuFlyout` is built from
`CommandRegistry` rather than written out: `Invoke` commands whose workspace is
`intelligence`, excluding `go.` navigation. **16 authoring commands**, each item
calling `Execute(id)` — the same identifier through the same dispatch the palette
uses. No second implementation, no thirteen buttons, no new navigation.

It first offered **19** entries, because three `go.*` commands share the workspace
tag. A menu called "New" should not offer to open a tab, so navigation is filtered.

## 5. Command completeness

`AuthoringDiscoverabilityTests` asserts the launcher reads the registry, filters to
authoring, adds an item per command, announces `command.Label`, dispatches
`Execute(id)`, and that **every command it offers is answered by the page**. A
fourteenth is surfaced by construction.

**A product-wide version of this rule was attempted and withdrawn.** Following a
click handler to its command needs real call-graph analysis, and the approximation
flagged pages that plainly have buttons. A guard that cries wolf is worse than
none; the withdrawal and its reason are recorded in the test file.

## 6. ADR-0032

Not contradicted, not rewritten. It requires the palette to list only what it can
dispatch; both routes dispatch the same command.

| Surface | Role |
| --- | --- |
| Command palette | accelerator / alternate dispatch |
| Workspace launcher | primary discoverability for ordinary authoring |

## 7. Keyboard

`Tab` reaches the launcher; `Enter` opens the menu and focus lands on the first
item; `Down` traverses; `Enter` invokes. Measured: focus on MenuItem *"State a
prediction"* → Down → *"State a new probability"* → Down → *"Resolve a
prediction"*.

## 8. Accessibility

| | |
| --- | --- |
| Launcher accessible name | `New intelligence record` |
| Menu entries | 16 human titles, no command records |
| Expanded state | `state=Collapsed` exposed |
| `Escape` | closes the menu and **returns focus to the launcher** |
| New findings | **0** across all 54 opened dialogs |

## 9. Layout

| Size | Result |
| --- | --- |
| 1920x1080 | on screen at `1817,113,108,48` |
| 1600x1000 | on screen at `1497,113,108,48` |
| 900x700 | renders compactly (`48` wide), menu opens with all 16 |

At 900x700 the navigation pane does not expose destinations by name, so the
workspace was reached by palette to finish the measurement. Pre-existing
(`AOS-R001-013`), not introduced here. No nested `ScrollViewer`/`ListView` added.

## 10. Palette regression

None. `State a prediction` from the palette still opens `CreatePredictionDialog`.

## 11. Representative opens

| From | Result |
| --- | --- |
| Launcher, "State a prediction" | `CreatePredictionDialog`; Escape closed it; focus returned to the launcher |
| Launcher, "Record a source" | `RecordSourceDialog` |
| Palette, "State a prediction" | `CreatePredictionDialog` |

## 12. Final disposition

```
AOS-R002-003 = OWNER DECISION RESOLVED / REPAIRED
```

## 13-22. Gates after this work

| | |
| --- | --- |
| build | **0 warnings / 0 errors** |
| unit | **3,832** |
| Windows | **1,043** |
| Reviewer | **163** |
| LAB integration | **871 / 871** |
| OpenAPI | **263 / 187**, `942d0e74...`, byte-identical x2 |
| contract | **14**, gate idempotent x2 |
| dialog sweep | **65 / 54 / 7 / 4 - zero changed outcomes**, client never closed, 0 refused keystrokes, 0 accessibility findings |

## 23. Schema / API / contract impact

**None.** Client-side only: one launcher, one button, one handler.

## 24. Remaining design decisions

**None open.** `LinkRecordDialog`'s two parent-scoped kinds remain
`INTENTIONALLY_UNSUPPORTED` with a stated reason, unchanged by this work.

## 25-27. Authoritative state

`postgres:18.6` **PENDING** - CI **PENDING** - Nightly **PENDING** - Git
operations **NONE**.
