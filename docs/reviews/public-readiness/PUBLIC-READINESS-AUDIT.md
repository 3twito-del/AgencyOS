# Public-readiness secret / history audit

**Question:** can `AgencyOS` be changed from private to public without exposing a
credential or private data?

**This pass is inspection only.** Nothing was made public, committed, pushed,
rewritten, rotated or deleted, and no workflow was dispatched.

**Date:** 2026-09-18

---

## 1. Repository state inspected

| | |
| --- | --- |
| Tracked files at `HEAD` | **1,041** |
| Commits reachable from all refs | **127** |
| Distinct blobs in history | **1,879** |
| Distinct paths ever committed | **1,245** |
| Refs covered | `master`, `repair-wave-001`, `origin/master`, `origin/repair-wave-001`, tag `nightly-ada2310` |
| Uncommitted working tree | included in the current-tree scan |

`artifacts/` is **not tracked** (`.gitignore:24`) — **0** files under it are in the
index or in history, so none of the local evidence, server logs or LAB data would
be published.

## 2. Scan methods used

1. **Current tree** — `git grep` over all tracked files: provider token prefixes
   (`ghp_`/`gho_`/`ghu_`/`ghs_`/`ghr_`/`github_pat_`, `xox[baprs]-`, `AKIA`,
   `AIza`, `sk-`), private-key and certificate blocks, credential-bearing URLs
   (`postgres://user:pass@`, `mysql`, `mongodb+srv`, `redis`, `amqp`),
   `Password` and `pwd` assignments, `Bearer` literals, named-secret assignments
   with 16+ character values, and a high-entropy sweep of every 32+ character
   token.
2. **Full history** — every one of the 1,879 blobs read through
   `git cat-file --batch` and scanned for the same shapes. Not a `HEAD`-only scan,
   and not a commit-message scan: blob contents.
3. **Filename history** — every path ever committed checked for `.dmp`, `.env`,
   `.pem`, `.pfx`, `.p12`, `.key`, `.zip`, `.7z`, `.tar`, `.gz`, `.bak`, `.sql`,
   `.dump`, `.log`, `.npmrc`, `.pgpass`, `secrets*`, `credentials*`.
4. **Large/binary** — all blobs ranked by size; the largest inspected by path.
5. **Workflows** — `.github/workflows/**` reviewed for secret references, echoed
   environments, verbose shell tracing and artifact contents.
6. **Docs/evidence** — tracked documentation and committed evidence logs scanned
   for credentials, personal data and machine paths.
7. **Repository metadata** — commit author/committer identities, licensing,
   confidentiality markings.

The repository also carries its own deterministic control,
`tests/AgencyOS.Tests.Unit/Architecture/SecretScanTests.cs` (ADR-0039), which runs
in every unit run — **3,832 passing**. It scans the working tree and **not** Git
history; closing that gap is what method 2 above is for.

That control scans this file too, and an earlier draft of the list above tripped
it: quoting a password assignment pattern verbatim *is* the pattern. The prose was
reworded rather than the pattern loosened or the file excused, which is the order
the scanner's own failure message asks for. A future edit that writes the literal
back will fail the unit gate again, by design.

## 3. Current-tree secret result

**0 findings.**

Every `Password=` occurrence in tracked files is a development default, a
documentation placeholder or a deliberate test fixture:

| Path | Class | Assessment |
| --- | --- | --- |
| `.github/workflows/ci.yml:218`, `nightly.yml:86` | CI service-container default | `localhost` / `postgres`, created and destroyed by the job |
| `src/AgencyOS.Api/Program.cs:108` | local dev fallback | `localhost` / `agencyos_dev` |
| `src/AgencyOS.Infrastructure/Persistence/DesignTimeDbContextFactory.cs:24` | EF design-time fallback | `localhost` / `agencyos_design` |
| `docs/14_OPERATIONS.md:126` | documentation placeholder | elided (`Password=...`) |
| `tests/AgencyOS.Tests.Integration/HealthTests.cs:28` | deliberately unreachable host | port 1, `Password=none` |
| `tests/AgencyOS.Tests.Windows/Diagnostics/DiagnosticSummaryTests.cs:98` | redaction test input | a fixture asserting that such a string is *redacted* |
| `scripts/AgencyOS.Backup.Common.ps1:84` | variable read | `Get-Part`, not a literal |

No provider token, cloud access key, private key, certificate, bearer literal or
credential-bearing URL exists in any tracked file. The high-entropy sweep returned
only UUIDv7 identifiers, W3C trace parents, migration names, a `SHA256SUMS.txt`
checksum and clearly-patterned test vectors (`00112233…`, `0123456789abcdef…`).

No `.env` file, key file or credential file is tracked.

## 4. Full-history secret result

**0 findings across all 1,879 blobs.**

- Provider tokens, private keys, cloud keys: **none**, ever.
- Credential-bearing URLs (`scheme://user:password@host`): **none**, ever.
- `neon.tech` / `cloudflare` / `CF_API` / `CLOUDFLARE_API`: **no occurrence in any
  blob at any commit**.
- Password assignments, the complete list across all history:
  `PASSWORD = $Password`, `PASSWORD = $previous` (shell indirection),
  `Password = Get-Part` (a cmdlet call), and `Password=postgres` (the development
  default). **No literal credential.**

No file was ever committed and later deleted that would resurface through history:
the filename sweep over all 1,245 historical paths found no dump, archive,
environment file, key or backup at any commit.

## 5. Binary / large-file result

**No binaries are tracked at all** — zero `.dll`, `.exe`, `.pdb`, `.png`, `.jpg`,
`.ico`, `.zip`, `.snk` files in the index. The largest blobs in history are EF Core
migration files:

| Size | Path |
| --- | --- |
| 420,621 | `…/Migrations/20260909072201_AiResultClassification.Designer.cs` |
| 420,507 | `…/Migrations/AgencyOsDbContextModelSnapshot.cs` |
| 416,683 | `…/Migrations/20260908210740_AiRuntime.Designer.cs` |

All text, all generated schema description, no credentials.

Two committed evidence logs exist —
`docs/reviews/repair-003a/evidence/exception-trace-before.log` (50 lines) and
`exception-trace-connectmailbox.log` (11 lines). Both scanned: **0** matches for
password, token, secret, connection string or provider name.

## 6. Workflow / logging exposure result

**0 blockers.**

| Check | Result |
| --- | --- |
| `secrets.*` references | **none in either workflow** — no secret is injected, so none can be printed |
| `permissions:` | `contents: read` in both — least privilege |
| `set -x`, `printenv`, `Get-ChildItem Env:` | none |
| Action pinning | third-party actions pinned to a commit SHA |
| Artifacts uploaded | `release-manifest.json`, `migrate.sql`, `sbom/**`, `artifacts/openapi/*.json`, `artifacts/nightly/**` |

The uploaded artifacts are schema DDL, a dependency SBOM, the OpenAPI contract and
build output. None carries a credential.

**Publication consequence, stated plainly:** on a public repository, Actions logs
and artifacts become publicly readable. Because these workflows consume **no
secrets at all**, there is nothing for a future log to leak. That position holds
only while it stays true — a secret added later would be publicly logged if it
were ever echoed.

## 7. Docs / artifacts result

**0 blockers.**

- Every email address in tracked files uses an RFC-reserved synthetic domain
  (`.test`, `.invalid`, `.example`). **No real address appears in file content.**
- No `C:\Users\…` or other local machine path is committed.
- "Confidential" and "Proprietary" appear only as **domain sensitivity
  classifications** (`IntelligenceSensitivity`, document sensitivity levels), not
  as document markings.
- Test fixtures use obviously fictional people
  (`Bartholomew-Fitzgerald Pemberton-Featherstonehaugh III`, `Zoë Ångström`) and
  reserved-domain addresses. No data appears to be real personal data.
- `.claude/settings.local.json` is **not** tracked; only the `.example` is.
- `artifacts/` is untracked, so no LAB database content, server log or runtime
  evidence would be published.

## 8. Non-secret publication concerns

These are **not** security blockers. They are decisions the owner should take
knowingly before flipping the switch.

| # | Concern | Detail |
| --- | --- | --- |
| 1 | **Personal email in commit metadata** | Every one of the 127 commits is authored and committed by `Twito <3twito@gmail.com>`. On a public repository this address is visible on every commit. It is not a credential. Changing it retroactively requires rewriting history; GitHub's `@users.noreply.github.com` address is the usual forward-looking fix. |
| 2 | **No LICENSE file** | The repository has none. Published without one, the default is "all rights reserved": readers may view it but have no granted rights. If public means "open source", a licence must be chosen deliberately. |
| 3 | **Documentation is internal in tone** | `CLAUDE.md` (an engineering constitution), the ADRs, milestone records and the whole `docs/reviews/` repair history would become public. Nothing in them is secret, but they describe internal process, judgement and unfinished work in candid terms. |
| 4 | **READMEs are Hebrew-only** | `README_HE.md`, `README_VSCODE_HE.md`. Not a risk; a public-audience consideration. |
| 5 | **Account identifier** | `3twito-del` and `github.com/3twito-del/AgencyOS` appear in docs and workflows. Unavoidable and already implied by publication. |

## 9. Known crash-dump incident disposition

**The concern does not reach the repository.**

The 003A.1 incident was a Windows Error Reporting minidump, written outside the
repository, which contained the launching shell's environment — including a
Cloudflare API token and a Neon database URL with its password. It was recorded as
a path only and deliberately never copied in.

Verified here, three independent ways:

1. **No `.dmp` or dump-like file was ever committed** — filename sweep over all
   1,245 historical paths.
2. **No blob in history contains the `MDMP` minidump signature** — 0 matches
   across all 1,879 blobs.
3. **No blob in history contains `neon.tech`, `cloudflare`, or any
   credential-bearing URL** — 0 matches.

The dump's contents never entered the repository, at any commit.

**This audit makes no statement about whether those credentials were rotated.**
That is outside a repository scan: a token exposed in a local dump may still be
live regardless of the repository being clean, and rotation was explicitly out of
scope for this pass.

## 10. Blockers

**None.**

No current secret, no historical secret, no credential-bearing artifact, no
workflow exposure, no private personal data.

## 11. Follow-up remediation

Nothing is **required** for secret safety. The owner may wish to decide, before
publishing:

1. Whether the author email on 127 commits should remain public (§8.1).
2. Which licence, if any, applies (§8.2).
3. Whether the internal engineering and audit documentation should be public
   (§8.3).

None of these requires credential rotation or history rewriting for security.

## 12. Limitations of this audit

Stated so the verdict is not read as broader than it is.

1. **Pattern-based, not semantic.** A credential with no recognisable shape — a
   bare high-entropy string with no key name — could evade every pattern used. The
   high-entropy sweep mitigates this but does not eliminate it.
2. **No hosted scanner was run.** GitHub secret scanning, `gitleaks` and
   `trufflehog` were not available in this environment; this is a
   `git`-and-`grep`-based audit plus the repository's own `SecretScanTests`. A
   second, independent scanner is the obvious way to raise confidence.
3. **Reachable history only.** Objects reachable from `master`,
   `repair-wave-001`, their remotes and `nightly-ada2310` were scanned. Unreachable
   or dangling objects were not; GitHub does not publish them, but they are out of
   this audit's scope.
4. **Credential validity not tested.** Nothing was probed against Cloudflare, Neon
   or any provider. "No credential in the repository" is not "no credential is
   live elsewhere".
5. **Rotation status unknown.** Out of scope by instruction.
6. **Point in time.** This describes the repository as of 2026-09-18 at the
   inspected refs. Any later commit needs its own check; `SecretScanTests` covers
   the working tree continuously, but nothing in the repository scans history
   automatically.
