# AgencyOS canonical state publisher

**Status:** IMPLEMENTED — FIXTURE-VALIDATED; REAL CANONICAL DOGFOOD PENDING

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

It has been exercised only against disposable fixture repositories with disposable bare remotes.
**Its first use on the real canonical branch is Phase 3D, and has not happened.**

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
| `correct` | `DESCRIPTIVE_CORRECTION` payload | P0 → P4 of contract section 7.3 → `CORRECTION_VERIFIED`. |

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

- It has been validated only against fixtures. The real canonical branch has hooks, history and a
  remote the fixtures only approximate. That is what Phase 3D must prove.
- `basis_readback_utc` and `seal_readback_utc` come from the local clock at the moment the checks
  complete.
- Remote readback trusts `ls-remote` and the fetched objects of the one configured `origin`.
- "Remote blob equality" compares content-addressed objects, so its force is in confirming the
  remote head; the blob comparison re-derives the receipt.
- It does not collect CI or Nightly results.
- It checks forbidden implications only by exact phrase, which is narrow by design.
- It cannot check that a `MACHINE_VERIFIABLE_FACT`-only payload publishes only machine-verifiable
  facts. That is semantic, and stays with the authority.
