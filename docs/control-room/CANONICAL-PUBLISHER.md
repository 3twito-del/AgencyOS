# AgencyOS canonical state publisher

**Status:** IMPLEMENTED — FIXTURE-VALIDATED; REAL CANONICAL DOGFOOD VERIFIED

**Governed by:** [CANONICAL-PUBLISHER-CONTRACT.md](CANONICAL-PUBLISHER-CONTRACT.md) (normative) and
[CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md). Where this document and the contract
differ, the contract wins.

The publisher executes an **already-adjudicated** canonical transition:
1. it stages the accepted bytes;
2. it records a durable seal authorization;
3. it promotes exactly the bound bytes;
4. it proves each step by reading the remote back.

**Semantic authority stays outside it.** It never decides that a transition should be accepted,
that evidence is enough, that a correction is descriptive, that an Owner should approve, or that
scope may grow. It checks that such authority is referenced and bound, never that it was right.

It is exercised in the tests against disposable fixture repositories with disposable bare remotes.
Its first real canonical use, `DELTA-20260929-001` in Phase 3D, went through every command on
`operational-regression-gate` and reached `PUBLISHED_VERIFIED` at P7 and again, as a no-op, at R7
(section 8). That run exposed findings which this implementation now addresses. It gives the
publisher no semantic authority, and it is not Self-Update V1 terminal acceptance, which is a
separate Control Room state decision.

Code: `tools/AgencyOS.Canonical.Publisher/` (C#, `net10.0`, no product reference, no package, no
reference to the detector). Tests: `tests/AgencyOS.Tests.Canonical/Publisher/`, run with the
detector's by `scripts/Invoke-AgencyOS.ps1 test-canonical` and the CI step "Canonical detector
tests".

---

## 1. Commands

```
AgencyOS.Canonical.Publisher stage     --repo <path> --payload <file>
AgencyOS.Canonical.Publisher authorize --repo <path> --authorization <file>
AgencyOS.Canonical.Publisher resume    --repo <path> --branch <name> --delta <DELTA-ID>
AgencyOS.Canonical.Publisher correct   --repo <path> --payload <file>
```

| Command | Input | Route |
| --- | --- | --- |
| `stage` | Accepted Publication Payload | R0, R1, R2 or a replacement basis. P0 → P4, always ending at the mandatory hold with `BASIS_VERIFIED_AWAITING_SEAL`. It never enters P4A. |
| `authorize` | Explicit durable seal authorization | R2, R3 or R4. P4A (append one record) → P5 → P6 → P7 → `PUBLISHED_VERIFIED`, unless a failure stops it. There is no rest state between P4A and P5. |
| `resume` | None | Derives R1–R7 from git and continues only along the permitted route. At R2 it stops at the hold again, because it never seals without an authorization. |
| `correct` | `DESCRIPTIVE_CORRECTION` payload | P0 → P4 of contract section 7.3 → `CORRECTION_VERIFIED`. The correction commit carries the exact payload as its durable record (section 3.1). |

The receipt goes to standard output. It is the normative `AGENCYOS PUBLISHER RECEIPT`: every
section 11 field, with `None` where a field does not apply, and no confidence score. Receipts are
not stored anywhere.

| Exit code | Result |
| --- | --- |
| 0 | `PUBLISHED_VERIFIED` or `CORRECTION_VERIFIED` |
| 10 | `BASIS_VERIFIED_AWAITING_SEAL` |
| 20 | `STOPPED_PRECONDITION`, `STOPPED_AUTHORITY` or `STOPPED_SCOPE` |
| 30 | `FAILED_SEAL_VALIDATION` or `FAILED_REMOTE_VERIFICATION` |
| 64 | Usage error |

**Command boundaries.** Where a run stops is fixed by the command, as contract section 6
defines. Payload v1.1 has no `stop_after`, and a payload that still carries one is refused as an
unknown member. A seal authorization names the semantic-basis SHA, which exists only after P4. So
`stage` always ends at the hold, and the seal happens only in a later `authorize` run. P5 is never
entered before P4A has passed.

## 2. Inputs

**Payload.** Contract `agencyos-canonical-publisher/v1.1`, with no `stop_after`, as the exact
RFC 8785 bytes, parsed strictly:
- every key is present, and no other key is;
- every value has the right type;
- there are no numbers and no `null`;
- no text contains a carriage return or a byte-order mark;
- the stored bytes are already canonical.

Any other version, including `v1`, is refused. Every byte of semantic text comes from the payload
verbatim:
- `current_state.next_text`;
- `delta.entry_text`;
- `decisions.new_entries`;
- `provenance_prose`;
- `corrections`.

**Seal authorization.** A JSON object with contract `agencyos-canonical-seal-authorization/v1`
and exactly these fields:
- `scope`, which must be `AUTHORIZE_SEAL_ONLY`;
- `authority`, which is `CONTROL_ROOM` or `OWNER`;
- `delta_id`;
- `semantic_basis_sha`;
- `current_state_next_sha256`;
- `payload_sha256`;
- `authorized_utc`;
- `reference`;
- `recorded_by`.

The publisher adds only the next `SA-n` number and writes the record in the section 5.2 syntax.
It never infers an authorization from a conversation, from a commit's existence, or from intent.

**Which authority seals.**
- The seal authorization must be `OWNER` when the transition requires Owner authority, meaning
  `owner_decision_applies` or an `OWNER` class.
- Otherwise it must be `CONTROL_ROOM`. That includes a transition accepted as
  `MACHINE_VERIFIABLE_FACT` only, which still cannot seal without one.
- The record permits execution of already-accepted bytes. It is not a new adjudication.

## 3. Phases

| Phase | What the implementation does |
| --- | --- |
| P0 | Checks the branch; fetches, and asks the remote for its head (`ls-remote`); requires a clean tree. Refuses another pending directory. Parses both ledgers, with strict lifecycle shapes. Checks the prior `CURRENT-STATE.md` digest, the static scope (mandatory forbidden paths, written paths in `paths.allowed`, no `src/`), authority presence, and lifecycle validity. |
| P1 | Writes the exact payload text: the delta entry appended or replaced, new decisions appended, `CURRENT-STATE.next.md`, and the canonical payload. The active `CURRENT-STATE.md` is not touched. Everything is staged. |
| P2 | Runs every section 7.1 check against the index: scope; the active state untouched; the staged bytes and claims; delta identity and digest; other entries byte-identical; decisions; authorization history; the correction chain; authority references; lifecycle; forbidden phrases. Also checks receipts to seal and provenance prose. |
| P3 | One commit, whose tree must equal the validated index. Fetches, then pushes as a normal fast-forward. |
| P4 | Fetches; the remote head must be the basis; checks the staged blobs and digest on the remote. Stops at the hold. |
| P4A | Appends one record. The diff from the basis must be exactly that record, and the digest unchanged. Commits; confirms the remote is still the basis; pushes; reads back; applies the binding rule; records `basis_readback_utc`. |
| P5 | Computes the section 7.2 whitelist from the final basis. Writes it, stages it, and requires every changed path and byte to be whitelisted, the working tree clean, the digest unchanged, and the promoted state equal to the bound digest. Re-checks the binding. |
| P6 | Fetches; requires the remote to still be the final basis; pushes as a normal fast-forward. |
| P7 | Fetches; requires the remote head to be the seal; compares every changed path's blob on the remote with the sealed bytes; requires the staging directory to be absent. Only then `PUBLISHED_VERIFIED`. |

**Digests.** Every digest is taken over git blob bytes: the index (`:path`) before a commit, and
`<commit>:path` or `origin/<branch>:path` after it. Working-tree files are never trusted,
because line-end conversion alters them. A fixture with `core.autocrlf=true` and CRLF working
files proves this.

**Parser.** The contract section 5.4 parser (`Ledger/Ledger.cs`) is implemented once and shared by
every phase:
- it starts after `# Entries`, and uses a closed, longest-first field set;
- field blocks run from header to header, and an entry ends at its last non-blank line;
- the whole `Status`, `Seal authorizations`, `Published` and `Publication receipt` blocks are
  excluded from the digest, and `Adjudication` is kept;
- excluded blocks must have their strict shape, so substantive text cannot hide in one.

**Entry boundaries and the heading.** An entry ends at its last non-blank line before the next
structural boundary (`#…`, `---` or EOF). The blank separator lines before that boundary belong to
no entry, internal blank lines stay in their field blocks, and nothing is whitespace-normalised.
The `## DELTA-…` heading is digested.

**Digest correction chain.** The normative digest of `DELTA-20260928-001` is
`397d9ce4220567d86b0cc9fdb0ed601a11e4d33c61f5f4ac916e37d5ac9902fa`:
- The contract's section 5.4, both as published at `4932877` and as clarified, and the production
  parser all give this value over the committed `CANONICAL-DELTAS.md` blob.
- The clarification makes no difference to this entry: it is the last one, with no trailing
  separator before EOF.

The Phase-3A validation receipts reported
`206d087d88890bb7f8b1dd2f4d4aed825476763abb4dc9e9bb8bc39cc16ad8cd`. That value came from an
ad-hoc script that left out the heading `## DELTA-20260928-001\n`:
- it hashed 4,991 bytes, where the normative input is 5,013; the 22-byte difference is exactly
  that heading;
- the script's phantom empty line, left by splitting an LF-terminated file, fell inside the
  excluded `Publication receipt` block, so it changed nothing.

That receipt value is corrected here as non-canonical validation evidence. Its qualitative claim,
that the digest stayed stable under record appends and sealing, still stands. The historical
delta is unchanged, and no correction delta is needed. A test pins both values and proves the
heading is digested.

### 3.1 Descriptive correction

`correct` runs contract section 7.3:
- **P0** also resolves every `authority.adjudication_references` entry, and every governing
  decision, with the same resolver semantic staging uses (`CheckAuthorityReferences`): a Decision
  ID present in `DECISIONS.md`, `<repository path>@<full SHA>` resolving in the repository, or
  `Adjudication of <DELTA-ID>` naming a delta whose `Adjudication` is populated. The references are
  resolved at the pre-correction head. Anything else is `AUTHORITY_MISSING`, before any write.
- **P3** commits with this fixed message, and nothing else in it:

  ```
  Apply a descriptive canonical correction

  AgencyOS-Correction-Contract: agencyos-canonical-publisher/v1.1
  AgencyOS-Correction-Payload-SHA256: <SHA-256 of the payload bytes>
  AgencyOS-Correction-Payload-Base64: <standard padded Base64 of the exact payload bytes, one line>
  ```

  Every line is the contract ID, a hash, or an encoding of bytes the Control Room supplied. The
  publisher generates no semantic text for it. Before pushing, it reads the local commit object
  (`cat-file commit`) and requires the record to reconstruct the exact payload, so that a hook or
  configuration that rewrote the message never reaches the remote.
- **P4** confirms by `ls-remote` that the remote head is the correction commit, compares every
  corrected blob on the remote, and then reads the commit object at the fetched
  `origin/<branch>` with `cat-file commit`. `CORRECTION_VERIFIED` requires that object to
  reconstruct, byte for byte, the payload this run applied.

Reconstruction is strict (`Publishing/CorrectionRecord.cs`):
- the message must be exactly the subject, a blank line and the three lines, in order;
- the contract must be `v1.1`;
- the Base64 value must be the one canonical encoding: no whitespace, correct padding;
- the decoded bytes must hash to the recorded SHA-256;
- they must parse under the production v1.1 parser as a `DESCRIPTIVE_CORRECTION`.

Any failure of this before or after the push is `REMOTE_BASIS_MISMATCH`, which the contract
raises in P3 and P4.

**Payload size bound** (contract section 7.3). `CorrectionRecord.MaxPayloadBytes` is 18,000. A
correction payload whose canonical bytes exceed it is refused in P0 with `PRECONDITION_DRIFT`,
before any file is written, staged, committed or pushed. A payload of exactly 18,000 bytes is
accepted. The bound exists because the publisher passes the message to `git commit -m` as one
command-line argument. It keeps the Base64 record, which is about 24,000 characters at the bound,
well inside the Windows command-line limit of 32,767 characters on every platform. It is this
implementation's safe transport bound, not a Git format limit and not a governance rule. The
payload is never truncated or split, and semantic `stage` payloads are not subject to it.

Running the same correction again is a new run. Its `expected_start_sha` is no longer the head,
so it is refused with `PRECONDITION_DRIFT` and nothing is written. The contract defines no
recovery state for a correction, and none is invented.

## 4. Git write boundary

Every git call passes `GitCommandPolicy` (`Git/PublisherGit.cs`) before a process starts. The
subcommands allowed are:
- read: `fetch`, `ls-remote`, `rev-parse`, `merge-base`, `rev-list`, `diff`, `status`,
  `symbolic-ref`, `cat-file`, `ls-tree`, `ls-files`, `for-each-ref`;
- write: `add`, `commit`, `push`.

Each has one shape:

| Command | The only shape allowed |
| --- | --- |
| push | `push --porcelain origin <full SHA>:refs/heads/<branch>`: a normal fast-forward of one commit |
| commit | `commit -q -m <message>` |
| add | `add -A -- <paths>` |
| fetch | `fetch [--no-tags] [--quiet] origin` |

**Hard-forbidden paths.** Separately from the git policy, Publisher V1.1 never changes `src/`,
`tests/`, `.github/`, `scripts/`, the protocol, the closure state, `docs/releases/` or
`docs/adr/`. `CONTROL_ROOM` and `OWNER` authority alike cannot override this, and there is no
exception field. Such a transition stops with `SCOPE_VIOLATION`, or `PRODUCT_BOUNDARY_VIOLATION`
for `src/`.

Refused by the git policy, before any process starts:
- `--force`, `-f`, `--force-with-lease` and `--force-if-includes`;
- a `+` refspec;
- `--mirror`, `--delete`, `--amend`, `--prune`, `--all`, `--tags`, `--no-verify` and
  `--allow-empty`;
- the subcommands `reset`, `checkout`, `switch`, `rebase`, `merge`, `tag`, `branch`, `config`,
  `update-ref` and `gc`, and every other one not listed.

Before every push, the publisher fetches and compares the remote head with the expected one.

## 5. Recovery

Recovery is derived from git alone: the heads, the ancestry, the ledger lifecycle fields and
records, the staging directory and the staged digests. Nothing else holds workflow state. Every
state below is produced in the tests by really interrupting a run.

| State | How the publisher recognises it | Continues with |
| --- | --- | --- |
| R0 | Remote = local = `expected_start_sha`; not staged | `stage`: P1 |
| R1 | Local is one commit ahead and stages this payload | `stage` or `resume`: P2 → P3 → P4 |
| R2 | Remote = local = a semantic basis; no record binds it | `resume`: P4 and the hold. `authorize`: P4A |
| R3 | Local is one authorization commit ahead of the basis | `authorize` or `resume`: push, P4A readback, seal |
| R4 | Remote = local = an authorization commit | `authorize` (same record) or `resume`: binding, P5 |
| R5 | Local is one whitelisted seal commit ahead of the final basis | `resume`: P6 → P7 |
| R6 / R7 | Remote = local = the seal commit | `resume`: re-runs P7, which only reads, and returns `PUBLISHED_VERIFIED` marked "Already published" |

Movement past a pending basis is classified, not left as drift:
- Commits on top of an authorization commit (the final basis) before the seal are
  `REMOTE_MOVED_BEFORE_SEAL`.
- Commits on top of a semantic basis not yet authorized are `REMOTE_BASIS_MISMATCH`.

Two starts sit outside the table:
- A **replacement basis** is `stage` with an `ADVANCE_EXISTING_DELTA` payload whose
  `expected_start_sha` is the current head, while `pending/<DELTA-ID>/` already exists. The
  delta's digest and new decisions must be unchanged, and its records byte-identical.
- An authorization whose basis the remote has moved past (other than by its own authorization
  commit) is `REMOTE_BASIS_MISMATCH`.

Any other state is `PRECONDITION_DRIFT`.

## 6. Failures

Every failure class the contract names is implemented, each mapped to its section 11 result
exactly. None is retried. A local-only failure leaves the working tree and the index as produced,
for inspection. The publisher never resets, discards or cleans.

## 7. Evidence limitations

- Fixture repositories remain its broad, deterministic coverage. One real canonical transition,
  `DELTA-20260929-001`, has now exercised the complete path on the canonical branch (section 8).
  One successful transition does not prove every future repository or remote failure mode.
- Semantic correctness stays with the authority. Neither the fixtures nor the dogfood make the
  publisher a judge of it.
- `basis_readback_utc` and `seal_readback_utc` come from the local clock at the moment the checks
  complete.
- Remote readback trusts `ls-remote` and the fetched objects of the one configured `origin`.
- "Remote blob equality" compares content-addressed objects, so its force is in confirming the
  remote head; the blob comparison re-derives the receipt.
- It does not collect CI or Nightly results.
- It checks forbidden implications only by exact phrase, which is narrow by design.
- It cannot check that a `MACHINE_VERIFIABLE_FACT`-only payload publishes only machine-verifiable
  facts. That is semantic, and stays with the authority.

## 8. Real canonical dogfood: `DELTA-20260929-001`

The first real use of the publisher, on `operational-regression-gate`:

| Step | Commit | Result |
| --- | --- | --- |
| First semantic basis (`stage`) | `ac87ea079659e99e23c90f7ae2619a49bbe50e97` | `BASIS_VERIFIED_AWAITING_SEAL`; R2 `resume` held. The Control Room withheld the seal because the Release identity text in `CURRENT-STATE.md` was stale. |
| Descriptive repair (`correct`) | `dfbd7ba0ef2fb7e02f6dfd94e4e4c0c56b98ee2e` | `CORRECTION_VERIFIED`. It invalidated the first basis. |
| Replacement semantic basis (`stage`, `ADVANCE_EXISTING_DELTA`) | `ee745bd1983a03d74d2c70394ed2d2ffe02f3a49` | `BASIS_VERIFIED_AWAITING_SEAL`, with the same substantive digest `4a96859219f5962f5ace5823168c5218351e9217fa6e1ddced90fcb0d64663c4`; R2 `resume` held. |
| Authorization commit and final publication basis (`authorize`, P4A) | `5f88fbe1a046b8c5ee303dafb2a83e48a0965529` | `SA-1`, `CONTROL_ROOM`, binding `ee745bd`; basis readback `2026-09-29T12:30:01Z`. |
| Seal (P5–P7) | `11935ff83f15d715ff9cee48c1514f43456fe0dd` | **P7 PASS**: `PUBLISHED_VERIFIED`, seal readback `2026-09-29T12:30:11Z`. |
| Idempotency (`resume`) | `11935ff83f15d715ff9cee48c1514f43456fe0dd` | **R7 PASS**: `PUBLISHED_VERIFIED`, "Already published", with no commit, no changed blob and no remote movement. |

This proves the real publisher path once. It does not decide Self-Update V1 terminal acceptance,
which stays with `CURRENT-STATE.md` and subsequent Control Room adjudication.

### 8.1 Historical correction chain for `dfbd7ba`

The descriptive repair is part of the dogfood evidence, and is recorded here because its commit
carries only the generic subject `Apply a descriptive correction`:

| Field | Value |
| --- | --- |
| Correction commit | `dfbd7ba0ef2fb7e02f6dfd94e4e4c0c56b98ee2e` |
| Parent | `ac87ea079659e99e23c90f7ae2619a49bbe50e97` |
| Classification | `DESCRIPTIVE_CORRECTION` |
| Authority | `CONTROL_ROOM` |
| Payload SHA-256 | `ba0f3d8aa398e979ef9d86a4cee46282584c11ded53e3dd39c71eef68c6c12cd` |
| Adjudication reference used | `docs/control-room/CANONICAL-DELTAS.md@ac87ea079659e99e23c90f7ae2619a49bbe50e97` |
| Purpose | Repair the stale Build 97 / repository-path description in `CURRENT-STATE.md`'s Release identity section. The old text said every later commit was under `docs/`, which Self-Update V1 tooling, tests and CI wiring had made untrue. |

- The reference is in the contract-valid `<repository path>@<full SHA>` form, and it resolves.
- The implementation of that time did not validate it mechanically (H3D-001 below).
- The correction is accepted as historical evidence because independent Control Room readback
  verified the exact one-path diff and the reference form.
- The payload bytes themselves are not in the repository, because that commit predates H3D-002.
- History is not rewritten. This is a correction chain for the dogfood evidence, not a new state
  transition, and no delta is created for it.

### 8.2 Findings and how this implementation addresses them

| Finding | What the dogfood showed | Addressed by |
| --- | --- | --- |
| **H3D-001** Correction authority references | `stage` resolved references with `CheckAuthorityReferences`, and `authorize` with `ReferenceResolves`, but `correct` only checked that one was present. So any non-empty string passed. | `correct` now calls `CheckAuthorityReferences` at the pre-correction head, after `CheckAuthorityPresence`, reusing the one resolver. Malformed or unresolved references are `AUTHORITY_MISSING` (section 3.1). |
| **H3D-002** Correction provenance | Commit `dfbd7ba` did not preserve enough to reconstruct the Control Room's classification and authority without conversation history. | Prospectively, the correction commit's message carries the exact canonical payload in Base64 with its SHA-256. It is verified locally before the push and in the remote commit object at P4 (section 3.1; contract section 7.3). |
| **H3D-003** Alert "current" state | After the seal, the Canonical Alert's `Current canonical state:` quoted the baseline's `CURRENT-STATE.md`, which the range itself had superseded. | The alert now quotes the state at the end of the range, labelled as such. The JSON packet keeps both snapshots. The staging question is now direction-neutral: "Do the staging changes at … match the expected lifecycle of an accepted transition?" It holds whether staging was added, modified or deleted. |
| Real-ledger parser test | `LedgerParserTests` pinned the live ledger to exactly `DELTA-20260928-001`. So the canonical gate failed as soon as the dogfood published a second delta, although CI (dispatch-only) was never run on those commits. | The test now requires every entry heading after `# Entries`, in order, and never the template, without a fixed list. A new test pins the published dogfood entry's digest and its `SA-1` binding. |
