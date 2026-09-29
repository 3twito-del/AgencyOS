# AgencyOS canonical state detector

**Status:** IMPLEMENTED READ-ONLY DETECTOR (Self-Update V1)

**Governed by:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md) and the detector
boundary in [CANONICAL-PUBLISHER-CONTRACT.md](CANONICAL-PUBLISHER-CONTRACT.md) section 1.

The detector compares a baseline commit with a later commit. It reports what changed, where, and
which authority must look at it. When the Control Room should look, it renders a Canonical Alert
locally. **It never says what a change means.**

**Its output is `OBSERVED` evidence, not a candidate delta** (protocol sections 2 and 6). The
lifecycle beyond the detector is:

```
detector evidence → OBSERVED
  → the Control Room inspects the evidence and decides whether a candidate delta is warranted
  → the Control Room allocates the permanent DELTA-YYYYMMDD-NNN and completes the section 6 fields
  → PENDING_ADJUDICATION
  → adjudication
```

The detector stops at `OBSERVED`. It does not:
- normalise an observation, allocate a Delta ID, or create a candidate delta;
- adjudicate, or claim any `PENDING_ADJUDICATION`, `ACCEPTED` or `PUBLISHED` state;
- write to the repository, including `CANONICAL-DELTAS.md`;
- call the publisher;
- deliver an alert. Rendering is local, and no alert transport exists.

Code: `tools/AgencyOS.Canonical.Detector/`. Tests: `tests/AgencyOS.Tests.Canonical/`, run by
`scripts/Invoke-AgencyOS.ps1 test-canonical` and by the CI step "Canonical detector tests".

---

## 1. Invocation

```
dotnet run --project tools/AgencyOS.Canonical.Detector -- \
  --repo <path> --branch <name> \
  --baseline <full SHA> \
  --observed <full SHA | local-head | remote-tracking> \
  [--detected-utc <yyyy-MM-ddTHH:mm:ssZ>] [--format json | alert]
```

- `--baseline` must be a full 40-character SHA.
- `--observed`:
  - an explicit full SHA is the deterministic mode;
  - `local-head` resolves `refs/heads/<branch>`, and requires a clean tree with that branch
    checked out;
  - `remote-tracking` resolves `refs/remotes/origin/<branch>` as of the last fetch. The detector
    never fetches.
- `--detected-utc` fixes the timestamp. With explicit SHAs and a fixed timestamp, the output is
  byte-identical on every run.
- `--format json` (the default) prints the evidence packet. `--format alert` prints the
  human-readable alert.

| Exit code | Meaning |
| --- | --- |
| 0 | `NO_RELEVANT_CHANGE` |
| 10 | `REVIEW_REQUIRED`: a Canonical Alert was produced |
| 20 | `DETECTOR_FAILURE`: nothing was observed and nothing was changed |
| 64 | Usage error |

## 2. Evidence collected

For `baseline..observed`, all from the local repository:
- ancestry, the commit list, and each changed path with its change type (added, modified,
  deleted, type-changed; renames are shown as a deletion and an addition);
- path categories and flags (section 3);
- the current branch, the local branch head, and the remote-tracking head as of the last fetch;
- local tags pointing at commits in the range;
- at both commits, read from git objects:
  - the `CURRENT-STATE.md` status line, its latest-delta line and its blob SHA-256;
  - each ledger entry's `Status`;
  - the entries under `pending/`.

**Not collected, and reported as `UNKNOWN`:**
- CI and Nightly results;
- tag movement (there is no earlier snapshot of tags);
- anything git cannot read at the time.

An unknown fact is never written as `NO` or as an empty list.

## 3. Evidence packet

The packet uses contract `agencyos-canonical-detector/v1`, serialised as RFC 8785 canonical JSON.
Its main fields:

| Field | Content |
| --- | --- |
| `result` | `NO_RELEVANT_CHANGE`, `REVIEW_REQUIRED` or `DETECTOR_FAILURE` |
| `change_kind` | `NO_GIT_CHANGE`, `GIT_CHANGE_NOT_CANONICAL_RELEVANT` or `CANONICAL_RELEVANT_CHANGE` |
| `product_code_changed` | `YES` or `NO`, established mechanically from the full diff (any path under `src/`) |
| `canonical_artifact_changed` | Whether any Control Room artifact outside `pending/` changed |
| `flags` | One `YES`/`NO` per category: release, review, closure and ADR records, tests, CI, scripts, tooling, build configuration, schema migrations, API contract source, domain source, governance files, staging, unclassified paths |
| `repository_evidence` | What the artifacts say at each commit, quoted rather than restated |
| `machine_verifiable_observations` | Facts a program established |
| `semantic_questions` | Questions only an authority can answer |
| `authority` | Classification, `owner_authority_may_apply`, each Owner trigger with its paths, and the recommended next authority |
| `conflicts` | Mechanical observations: `CURRENT-STATE.md` changed without a ledger change; a ledger entry removed; a ledger status moved backwards |
| `observation` | For `REVIEW_REQUIRED` only (see below) |
| `evidence_limitations` | What the detector could not see |

`observation` is the observed item:
- `lifecycle_state` is always `OBSERVED`;
- `delta_id` is `NOT_ALLOCATED`;
- `observation_id` is temporary: `OBS-<baseline12>-<observed12>`;
- `handoff` states the Control Room's normalization step;
- the proposed claim is `UNDETERMINED`.

It is not a candidate delta, and it has no status field.

**The detector's own words never contain a later lifecycle state.** Repository text may still
contain one: ledger statuses and the current-state lines are quoted under `repository_evidence`,
and the alert labels its current-state line as quoted. Quoted text is never the detector's own
claim.

**Canonical relevance is a path rule.** A path counts as relevant unless its only category is
ordinary `docs/`, meaning a document the protocol's precedence list (section 4) does not name as a
canonical source. An unrecognised path counts as relevant.

## 4. Authority boundary

| Classification | When |
| --- | --- |
| `MACHINE_VERIFIABLE_FACT_ONLY` | Nothing canonical-relevant changed. The packet states only mechanical facts. |
| `CONTROL_ROOM_ADJUDICATION_REQUIRED` | Any canonical-relevant change |

`owner_authority_may_apply: true` is set, with the exact trigger and paths, when any of these
changes:
- product source, a schema migration, API contract source or domain source;
- build configuration or pinned configuration;
- the decision ledger;
- the closure record or a release record;
- a governance file.

It is a caution, not a finding. **The Control Room decides whether Owner authority is required.**
The detector never infers Owner approval.

## 5. Canonical Alert

It is rendered locally, and only for `REVIEW_REQUIRED`, in the protocol section 7 shape. Its first
field names the observation, never a candidate:

```
Delta / observation: OBS-<baseline12>-<observed12> | Delta ID: NOT_ALLOCATED | lifecycle state: OBSERVED
```

The full shape:

```
AGENCYOS CANONICAL ALERT

Delta / observation:
Trigger:
Current canonical state:
Candidate change:
Evidence anchors:
Conflicts:
Authority required:
Product code changed:
Recommended Control Room action:
```

It is followed by the questions for the Control Room and the evidence limitations. The recommended
action is always to inspect the changes and decide whether a candidate delta is warranted, or to
dismiss the observation. It never states a verdict. A no-change result and a failure are reported
as plain result lines, never as alerts.

## 6. Failures

Every failure is explicit and non-mutating. None is repaired automatically.

| Failure class | Meaning |
| --- | --- |
| `INVALID_INPUT` | A SHA is not a full 40-character SHA |
| `UNKNOWN_BASELINE_COMMIT` | The baseline commit is not in the repository |
| `UNKNOWN_OBSERVED_COMMIT` | The observed commit or ref is not in the repository |
| `OBSERVED_NOT_DESCENDANT` | The observed commit does not descend from the baseline; the ancestry facts are reported |
| `DIRTY_TREE_FOR_LOCAL_HEAD` | `local-head` was requested with uncommitted changes |
| `READ_FAILURE` | git could not produce evidence the observation depends on |
| `UNSUPPORTED_REPOSITORY_STATE` | Not a git work tree, or the wrong branch for `local-head` |

## 7. Read-only guarantee

- git is run with `--no-optional-locks`, and only with an allowlist of read-only subcommands:
  `rev-parse`, `merge-base`, `rev-list`, `diff`, `for-each-ref`, `status`, `symbolic-ref`,
  `cat-file` and `ls-tree`. Any other subcommand is refused before a process starts.
- The program writes only its own standard output and standard error.
- Tests confirm that a run leaves every byte of a repository, including `.git`, unchanged.
- Tests confirm that detector output cannot be read as an Accepted Publication Payload: it names a
  different contract, lacks every payload field even with that name forged, and the detector
  assembly has no type or member that makes, takes or names a payload or a publisher.

## 8. Non-goals

This detector does not provide:
- a scheduler, watcher, webhook or daemon;
- fetching, or any GitHub call, read or write;
- CI or artifact evidence;
- a durable Delta ID, or any write to a ledger;
- adjudication, or any bridge from an adjudication to a publication payload;
- a publisher;
- product changes;
- NG-4.
