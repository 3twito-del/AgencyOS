# AgencyOS ALPHA 0.1.0, build 91

Reality Closure wave 8: a name says what it is, and one query stops guessing.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **91** |
| Executable commit | **`623d04e464595cda81248c68de3ba27c1662183d`** |
| Tag | **`alpha-623d04e`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 90, `alpha-6d46dfa` |

**No contract change, no migration, no new endpoint and no domain change.** One
new presentation type, one converter, one formatter change, one query expression,
eleven captions.

## 2. The executable commit is not the branch tip

```
git diff --stat 623d04e464595cda81248c68de3ba27c1662183d..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

**F-03 — a word that was never read from anything.** The opportunity subject
projection labelled every talent subject `Client`. Not sometimes wrongly: the
word was a literal, selected on the subject's *kind*. The query joins
`TalentProfiles` for the name and never touches `Representations`, so a person
the agency had never represented, or had stopped representing, read as a client
on Pipeline while Talent read `Not represented`.

`TalentProfile` states the rule this broke, in a comment written long before the
defect was found: *"There is deliberately no `IsClient` flag here. Being a client
is a consequence of holding an active representation, so it is derived rather
than stored — a stored flag is a second source of truth that drifts the first
time somebody terminates a representation without remembering to clear it."* A
hardcoded literal is that second source at its worst. It cannot even drift,
because it was never right except by coincidence.

The slot is documented as *"a project's stage or a role's type"*, and every
sibling branch emits the kind of record it points at — `Project`, `Package`,
`Role`. This one now does too.

**F-04, F-09, F-10 — a name with nothing saying what it is.** Eleven captions,
on prospects, receivables, invoices, payments, obligations, mailboxes, thread
participants, deals, contracts and two history lists. Position is not a
statement: a bare name where a byline goes is read as accountability wherever it
appears, and on half of these that reading was wrong.

**Five of the seven fields were absent from `RowLabel` entirely.** A
screen-reader operator scanning receivables heard the contract title, four
figures and a status, and never who owed the money — while the sighted row had
the payer in the caption. The two channels disagreed about what the row
contained.

## 4. Why not just say the real representation state

It was considered and rejected on the product's own grounds.
`IntelligenceSubjectLabels` already states the rule for a slot of this shape:
*"Labels are names only. Never a status, a figure, a stage or a classification,
because a subject label is rendered beside intelligence whose permissions differ
from the subject's own."* Putting representation state on a pipeline row would
publish a talent fact through an opportunity read.

The contradiction is gone because the false proposition is gone, not because a
true statement was deleted. Pipeline says what kind of record the subject is;
Talent says where the relationship stands.

## 5. One vocabulary, two channels

`PartyLine` holds the role words; the converter renders the caption and
`RowLabel` reads the same words, so the seen row and the spoken row cannot name
different roles. The markup names only the field it binds. `TargetLine` kept a
private copy of `"Contact: "` and `"Owner: "` and now delegates, because a second
copy is exactly the drift it was written to prevent.

**One word could not be used.** `ActorDisplayName` is labelled **By**, not
"Actor". In an agency for performers that word is a discipline — the project-role
dialog and the talent filters both offer "Actor" meaning somebody who acts — and
a history row reading `Actor: Ravensworth` would assert a profession the record
never claimed. A test pins it.

**Narrow on purpose.** Eleven captions changed and roughly thirty other
identities did not. A prospect, a radar subject and a commission client each
carry the only role their row could hold, and labelling those is noise rather
than meaning.

## 6. Normalizing the list moved the count again

The register recorded F-09 as seven bindings. Seven is right; its enumeration
listed six and missed the thread participants row, whose type carries both the
person an address resolved to and the internal user who resolved it.

The two radar rows were recorded as a missing label. Measured against the shipped
formatter, the visible channel was fine and the **announcement was naming a
different entity**: the row carried no property the headline vocabulary knew, so
it led with its own type name and filled the context slot from `CompanyName` —
the seen row headed by a person and the spoken row by a company, with the
person's name absent from the announcement entirely. Reclassified from
`ROLE_MISSING` to `CHANNEL_DISAGREEMENT` and repaired.

## 7. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35796448560` and canonical Nightly run `35796451803`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,021 |
| Windows | 1,349 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 927 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification put all three representation states on one pursuit, read from
the running client:

| Person | Pipeline subject | Talent surface |
| --- | --- | --- |
| A | `Talent` | `Client - Acting - led by Review Owner.` |
| Rivka … | `Talent` | `Terminated - no recorded scope - no lead assigned.` |
| third | `Talent` | `Not represented - no recorded scope - no lead assigned.` |

Before this build all three subject rows read `Client`, two of them falsely and
one of them contradicting the Talent surface outright.

The payment row is the clearest of the caption repairs, because its record
carries two parties and shows one:

| Channel | Payment row |
| --- | --- |
| Visible | `Payer: Corvid Ash Pictures \| WIRE-88213 \| 240,000.00 USD \| 0.00 USD \| Recorded` |
| Announced | `Payment, Payer: Corvid Ash Pictures, 240,000.00 USD, 0.00 USD allocated, 240,000.00 USD unapplied, Recorded` |

Before, the caption was a bare name and the announcement named no party at all.
The payee, `Thessaly Vane`, is named in neither channel, so the one name shown
cannot be mistaken for the other party. Wave 7's money repair is intact on the
same row.

Prospects and deals were confirmed the same way: `Owner: Review Owner` and
`Counterparty: A24`, in both channels.

Full detail is in
`artifacts/operational-alpha/reality-closure/WAVE-08-IDENTITY-SEMANTICS-CROSS-SURFACE-TRUTH.md`.

## 8. Verdict

**F-03, F-04, F-09 and F-10 are closed.** No in-scope instance remains open, and
no pair in scope remains a contradiction. The return-path and discoverability
findings recorded alongside them are untouched, because this wave was identity
semantics only.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build
91.
