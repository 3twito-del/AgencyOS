# ADR-0034 — What the workstation shows, keeps and hands on

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M13 — Advanced native Windows, workstation integration and local
  AI platform
- **Supersedes:** nothing
- **Builds on:** ADR-0025 (document classification and linking), ADR-0026
  (communications and mailbox boundaries), ADR-0030 (intelligence provenance),
  ADR-0033 (activation and the untrusted client)

## Context

Everything before M13 kept the agency's material inside the application. A person
opened AgencyOS, authenticated, and read what they were authorized to read on a
screen that belonged to AgencyOS.

M13 breaks that containment in four directions at once, because that is what
"native workstation" means:

- a **toast** appears over the lock screen, next to whatever else is on the
  desktop, read by whoever is standing there;
- an **update prompt** tells somebody their build can no longer do their job;
- a **document** is written to disk so another application can open it, at which
  point AgencyOS no longer controls it;
- a **diagnostic summary** is copied into a support message and pasted somewhere.

Each is a surface where material leaves the boundary that has protected it since
M0, and each fails quietly. Nobody notices that a toast said too much, because the
person who saw it was allowed to see it — the problem is the person behind them.

The governing distinction for all four: **being allowed to read something inside
the application is not the same as its being appropriate to display outside the
application.** M13 treats those as separate questions.

## Decisions

### 1. Notification detail requires three separate yeses

`NotificationPolicy.Compose` shows detail only when the organization allows it,
*and* the user has asked for it, *and* the material is at or below
`DetailCeiling`, which is `Confidential`. Any one absent yields a notice that says
something happened and where to look, and nothing about what.

Three because they answer three different questions — the firm's posture, this
person's screen, this material's sensitivity — and no one of them implies the
others. A person working alone in an office and a person on a train have the same
permissions and very different circumstances, which is why the user preference is
in there alongside the organization's.

### 2. A notification never carries a money amount

`NotificationRequest` has **no amount field at all**. Not an optional one, not a
suppressed one.

A receivable's value is the fact most likely to matter to somebody who should not
have it and least necessary for the notice to do its job. "A receivable is
overdue" gets the person to the screen; the number adds nothing they will not see
in two seconds and everything a bystander needs.

Structural rather than a suppression rule, because a suppression rule is one
refactor away from being bypassed, and there is no legitimate future caller.

### 3. Seven categories, and the categories are the vocabulary

`TaskDue`, `LegalDeadline`, `OptionDeadline`, `ReceivableOverdue`,
`AiApprovalAwaiting`, `AiRunCompleted`, `AiRunFailed`. A notification is one of
these or it is not sent.

A free-text notification surface would put arbitrary business content into a
channel with none of these protections, one caller at a time.

### 4. The release authority decides what an old build may do; the client only says so

The update UI reads the server's `BlocksProtectedMutations` and reports it. It
does not compute whether a build is too old, and it cannot decide it is fine after
all.

M2 put minimum-version enforcement on the server for a reason CLAUDE.md §6 states
outright: an old client must not be able to bypass server-enforced version policy.
An update prompt that decided its own severity would be exactly that. So
`UpdateFormatting` turns a server answer into a sentence, and the sentence says
what is refused rather than exhorting anybody to upgrade — a person who cannot
install an update is not helped by being told to.

### 5. A materialized document is a copy with a lifetime, never an identity

`DocumentHandoffPolicy.Plan` writes under
`LocalApplicationData/AgencyOS/materialized`, with a **four-hour** lifetime for
ordinary material and **thirty minutes** for sensitive material. **Restricted
material yields no plan at all** — it is never written to disk for another
application to open.

The refusal says the reader may still read it inside AgencyOS, because that is
true and because a refusal that sounds like a permission error invites somebody to
go looking for a way around it.

The critical part is what the path is *not*. M10 made the document the canonical
artifact, content-addressed, with its own identity and audit trail. A temp file is
a derived convenience copy. It never becomes a document identity, never gets
linked, and nothing reads it back as though it were canonical —
`ThePlanIsAPathAndNotAnIdentity` is the assertion. Otherwise the agency would
acquire a second, unversioned, unaudited document store made of everything anybody
ever opened in Word.

Filename construction defuses reserved device names, strips control characters,
bounds length and sanitizes the extension, because the title is business data and
the path is a filesystem instruction.

### 6. Diagnostics are an allow-list of twelve fields

`DiagnosticSummary` permits exactly twelve field names — AgencyOS version, release
channel, API contract, server compatibility, Windows version, Windows App SDK,
architecture, local AI readiness, execution providers, cache schema, connectivity,
last sync — and throws `ArgumentOutOfRangeException` for anything else. Values are
additionally screened against a forbidden word list, flattened, and bounded.

An allow-list because a diagnostic summary exists to be pasted into a message to
somebody outside the agency. A deny-list would be a list of the leaks somebody
already thought of; this is a list of what has been reviewed as safe to send. The
cost is that adding a field is a deliberate act with a reviewer, which is the
intent.

None of the twelve names business content. "Local AI readiness" is a state, not a
subject.

### 7. Timing is measured in four buckets, with an injectable clock

`OperationTimer` attributes elapsed time to `Client`, `Network`, `Server` or
`Model`. "The application is slow" is not actionable; "the model took eleven
seconds" is, and points somewhere different from "the network took eleven
seconds".

### 8. Accessibility is asserted over the markup, not reviewed periodically

`XamlAccessibilityTests` parses the shipped XAML and fails on an interactive
control with no accessible name, a notice with no title anywhere, a text control
with a fixed height, or a hard-coded colour.

The pass added `AutomationProperties.Name` to **176 controls across 27 files** —
overwhelmingly `ListView`s announced as "list" on pages carrying six of them, and
filter combos in dense toolbars that have no visible label either, where a
`Header` would break the horizontal layout they sit in.

Two rules are worth explaining:

- **A title set from code-behind counts.** Several notices legitimately title
  themselves with what happened, and demanding a literal in the markup would buy a
  placeholder that is overwritten before anybody sees it. The rule is that a title
  exists by the time a reader meets it.
- **The colour rule passed on its first run.** No page hard-codes a colour today.
  It is asserted so that a contrast theme keeps working, not because anything
  needed repair.

Accessibility regressions are silent — the page still looks right — so a periodic
review is the wrong instrument.

## Why this is better than the alternatives

**Notification detail as one organization setting.** Cheaper, and it makes the
firm choose between a useful notification surface for everybody and a safe one for
everybody.

**Suppressing amounts in the composer.** Same effect today, one refactor from
being bypassed, and it leaves an amount in a request object that a future caller
will read.

**Opening documents from a service-backed virtual path.** Genuinely better —
nothing durable on disk — and it needs a shell extension, an installed service and
a failure story for when it is not running.

**A deny-list for diagnostics.** Enumerates what somebody thought of.

**An accessibility checklist in the review guide.** Runs when somebody remembers.

## Consequences

**What this buys.** Material that leaves the application does so through a decided
channel. A toast on a shared screen says a receivable is overdue and not for how
much. A support summary can be pasted without reading it first. A copy on disk has
a lifetime. An unnamed control fails the build.

**What it costs.** Notifications are less immediately useful than they could be,
and some people will find the default terse. Handing a document to another
application is slower than opening a file in place. Twelve diagnostic fields will
not answer every support question. The accessibility rules will occasionally
demand a name for a control whose purpose is obvious to a sighted reader.

**What is deliberately not here.** No notification actions, no inline reply, no
badge counts, no live tiles, no telemetry upload, no crash-dump collection, no
in-place editing of a materialized document, no shell extension. Crash handling
records that a crash happened and its category; it does not collect a dump,
because a dump of this process is a dump of the agency's material.

**What the next milestone inherits.** Four boundaries with a policy object each,
all pure and all tested, so widening one is a decision recorded against it.
