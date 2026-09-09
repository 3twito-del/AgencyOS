# Windows Native Client Specification

## Primary stack
- C#
- WinUI 3
- Windows App SDK
- XAML
- MVVM
- packaged MSIX application

## Power-user UX
- dense tables;
- keyboard navigation;
- universal command palette;
- multi-window;
- split views;
- back/forward navigation;
- saved views;
- inline editing where safe;
- fast quick-capture.

## Native integrations roadmap
- notifications with actions;
- protocol/deep-link activation;
- file associations;
- drag/drop;
- clipboard;
- Explorer integration;
- Jump Lists;
- taskbar;
- global hotkey/quick capture;
- Windows Search integration;
- Windows Hello/passkeys;
- background tasks where appropriate;
- local cache/offline mode;
- NPU/GPU/local AI experimentation.

## Documents and communications (M10)

Two workspaces, and one rule running through both: the interface never claims an
act the build does not perform.

- **Documents** is a list and a detail pane. Versions are newest first and every
  one of them is still readable; the only command that adds bytes is called "Add
  version", because uploading again appends and never replaces. Files are chosen
  and saved through the Windows App SDK pickers, which work unpackaged and take a
  window id rather than needing interop for a handle.
- **There is no editor and no preview of formats this build cannot read.** Opening
  a file hands it to whatever Windows uses. A half-working document surface invites
  people to edit a contract in a box that quietly loses its formatting, and the
  milestone that stores files should not also be the one that renders them.
- **Nothing says a file is safe.** The detail pane says "Not scanned" and warns
  beside it, because nothing scanned it.
- **Communications is not an inbox.** Outlook exists and is better at being one.
  This surface answers the question Outlook cannot: which correspondence bears on
  which deal. Messages render as sanitized text, with a line saying that remote
  images and scripts were removed when the message was stored.
- **Sending is two actions.** Composing writes a draft and sends nothing. Queueing
  is a separate, confirmed act, and the confirmation says that AgencyOS cannot
  recall a message once a provider has accepted it.
- **An unknown outcome is never shown as a failure.** It reads "Outcome unknown -
  check the mailbox", it is counted apart from failed sends, and the explanation
  says the message may or may not have gone and that AgencyOS will not resend it
  on a guess.
- **No embedded WebView for credentials.** The OAuth consent page opens in the
  system browser. A credential prompt inside the application is one the
  application could be reading, and the user has no address bar to check.
- **No file watching.** AgencyOS does not watch a folder and overwrite canonical
  versions from it. Uploads are deliberate, and each one creates a new version.
- **No Office add-in.** The integration boundary is documented and server-side;
  a task pane is not added to satisfy a roadmap.

## Intelligence (M11)

One workspace, tabbed in the order of the chain: the desk, signals, sources,
theses, predictions, watchlists, the radar, research and relationship. Reached
from the navigation pane or F7, with nineteen palette commands under an
Intelligence category.

**The wording is load-bearing and is tested.** No screen renders a verification
state as "Verified"; no probability appears without the forecaster and the date
beside it; no calibration figure appears without the sample count it was computed
from; and where nobody has recorded a relationship strength the screen says "Not
recorded" rather than filling the gap from an interaction count.

**Nothing on the page generates anything.** There is no summarize button, no
"extract signals from this document" and no suggested probability, because there
is no route on the server that would answer one.

Two refusals happen before a dialog opens rather than after it is filled in:
recording a signal with no source available says so and offers to record the
source first, and putting somebody on the radar requires an existing person record
rather than a typed name.

The whole surface is ONLINE_ONLY. Nothing is cached, for the reason
`docs/13_OFFLINE_CLASSIFICATION.md` gives: a source-sensitive claim on a laptop is
not something a later revocation takes back.

## UI rule
Windows UI optimizes for Windows. It must not be constrained by a hypothetical future cross-platform UI framework.

## M12 — the AI workspace

`AiPage` carries five surfaces: **Ask** (start a task, read what came back),
**Approvals** (decide one exact proposed action), **Runs** (your own history),
**Trace** (what the model asked for beside what AgencyOS did) and **Policy** (what
may be transmitted at all). Navigation item after Intelligence, F6, palette
commands `go.ai`, `ai.ask`, `ai.approvals`, `ai.runs`.

> **This paragraph was wrong, and M13 found out.** It read: "Ctrl+9 still means
> Saved Views. The digit accelerators follow each item's access key rather than
> its position, so inserting AI above Saved Views does not repoint a shortcut
> people already use."
>
> The accelerators did **not** follow the access key. They addressed the
> navigation pane by index, from an index table that existed twice — once in the
> window constructor and once in the palette dispatch — and inserting the AI
> workspace shifted both. Ctrl+9 had silently changed meaning, and documentation
> asserting otherwise is part of why nobody noticed. It is recorded here rather
> than deleted, because a milestone that quietly corrected its own record would
> teach the next reader nothing.
>
> M13 made the claim true by making it structural: destinations are named, one
> registry defines every command, and a gesture collision fails at construction
> (ADR-0032).

### The approval dialog

**The default button is Reject.** Not neutral, and not Approve: a dialog whose
default commits a canonical write turns a keypress into a business act, and the
whole point of the approval is that somebody chose.

Everything it shows comes from the server's account of the tool request — the
summary AgencyOS wrote from the validated arguments, and the arguments themselves.
The model's own words about what it is asking for appear nowhere, because a model
that could word its own approval prompt could describe one action and request
another.

There is deliberately no "approve everything from this run" and no "don't ask
again". A standing approval is a permission grant wearing a button.

### Wording

Model output is labelled as model output wherever it appears: "This is what the
model said — a draft, not a record. Nothing here has been filed against any
person, deal or contract."

Every trace line says whether the model asked or AgencyOS acted. Failures are
rendered from the category, in terms of what a person can do about it, and never
as a provider message.

The cancel button says what it means before the fact: stopping a run cancels what
has not happened yet, and anything already approved stays.

### Offline

The whole surface is ONLINE_ONLY. Nothing is cached and nothing is queued.

### Not in the Command Center, and not a saved view

Both were considered and both were declined.

**The Command Center** computes nothing locally — every bucket, count and ordering
comes from the server's own query, so what a user sees is what the system believes.
Adding pending approvals would mean either breaking that rule by composing two
queries on the client, or widening a cross-cutting server query for one milestone.
Neither is worth it while an approval lapses in thirty minutes: a count on a page
people leave open would tell somebody something is waiting after it stopped being
decidable, which is worse than not showing it. The AI page loads its own and is one
keystroke away.

**Saved views** filter lists of business records that colleagues share. A run is
private to the person who started it and there is no filter over them worth saving,
because there is no audience to save it for.

Both are revisitable if approvals ever become long-lived. Neither is a deferral of
something the milestone needed.

## M13 — the workstation

### One window, named destinations

AgencyOS opens a single primary window with a navigation pane. Multi-window is on
the power-user list above and was **deliberately not built**: the agency's work is
cross-referential — a deal read against a contract read against what somebody said
last week — and a document-per-window shell needs window lifetime, per-window
state and cross-window navigation before it shows a useful screen. It is
revisitable when somebody has a concrete second-monitor workflow.

Every navigation command carries a destination **tag**, never an index.
`SelectMenu(int)` is gone. Reordering the pane, inserting a workspace or hiding one
cannot repoint a shortcut, because nothing about a shortcut refers to position.

### One command registry

`AgencyOsCommands.All` holds all **129** commands across **17** workspaces. The
palette lists them, the window installs accelerators by iterating them, and pages
dispatch the same identifiers. `CommandRegistry` refuses a duplicate identifier, a
duplicate global gesture, a page gesture shadowed by a global one, a malformed
gesture, or a `Navigate` command with no workspace — at construction, so a
collision fails the build rather than a person.

Building the list found **twenty-six palette commands that dispatched to nothing**
and **thirteen shortcuts the window never installed**. Seven were real destinations
never wired and became `Navigate` commands; **nineteen were removed**, because they
described actions this build does not perform and the palette documents itself as
listing only what exists.

### Activation

The `agencyos` scheme resolves seven routes — `person`, `company`, `deal`,
`contract`, `research`, `ai/run`, `ai/approval` — through one pure
`ActivationRouter`. Identifiers must parse exactly; trailing segments are refused;
failures are typed rather than thrown.

A link carries a destination and never an instruction. There is no route that
approves, executes, sends or deletes, and `?approve=true` is refused as an unknown
route — a link that could approve would route around the whole M12 approval
protocol, and it is a link an attacker can put in an email.

Resolving a link proves nothing about the object. A route for an identifier that
names nothing resolves exactly like one that names something real, because
teaching the client to tell them apart would be a disclosure and would put an
authorization decision on the untrusted side (ADR-0033).

### Notifications

Seven categories, and the categories are the vocabulary. Detail requires three
separate yeses — the organization allows it, the user asked for it, the material
is at or below `Confidential` — because those answer three different questions and
none implies the others.

**A notification carries no money amount.** There is no amount field on the
request at all. A receivable's value is the fact most likely to matter to a
bystander and least necessary for the notice to do its job.

### Documents, diagnostics, updates

A materialized document is a copy with a lifetime — four hours ordinary, thirty
minutes sensitive — under `LocalApplicationData/AgencyOS/materialized`.
**Restricted material is never written to disk.** The copy never becomes an M10
document identity, is never linked, and is never read back as canonical.

Diagnostics are an allow-list of **twelve** reviewed field names, screened,
flattened and bounded, because the summary exists to be pasted to somebody outside
the agency.

The update UI reports the server's `BlocksProtectedMutations` and does not compute
its own severity. An old client deciding it is fine after all is exactly what M2's
server-side enforcement exists to prevent.

### The AI workspace at device-local residency

`AiPage` gains an execution-target surface: what this organization permits and what
this workstation can actually run. Capability is probed **before** a lease is
requested, so a machine that cannot run the model discloses nothing.

The local-provider-unavailable state says what is refused and offers no install.
AgencyOS will not download a model, and saying so is more useful than a button
that cannot work.

### Accessibility

`XamlAccessibilityTests` parses the shipped XAML and fails on an interactive
control with no accessible name, a notice with no title anywhere, a text control
with a fixed height, or a hard-coded colour. The M13 pass added
`AutomationProperties.Name` to **176 controls across 27 files**.

Automated evidence about markup is not evidence that anybody has operated the
application with a screen reader or at 200% scaling. Manual LAB checks at 100%,
125%, 150% and 200% and across monitors have **not** been performed.

