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

Ctrl+9 still means Saved Views. The digit accelerators follow each item's access
key rather than its position, so inserting AI above Saved Views does not repoint a
shortcut people already use.

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
