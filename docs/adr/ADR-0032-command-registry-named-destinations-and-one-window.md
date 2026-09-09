# ADR-0032 — The command environment: named destinations, one registry, one window

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M13 — Advanced native Windows, workstation integration and local
  AI platform
- **Supersedes:** nothing
- **Builds on:** ADR-0031 (the AI runtime, which added the workspace that exposed
  the defect below)

## Context

M13 set out to make AgencyOS a Windows workstation rather than a Windows-shaped
UI, and the first thing it was asked to build was a keyboard-first command
environment. Before writing one, the existing keyboard surface was audited. It
was broken, and it had been broken since M12 shipped.

`Ctrl+9` had silently changed meaning. Not stopped working — changed meaning. The
window installed its accelerators against the navigation pane *by index*, and the
index table existed twice: once in the window constructor and once in the palette
dispatch switch. When M12 inserted the AI workspace into the pane, both tables
shifted, and every shortcut after the insertion point began opening the workspace
next door.

This is the failure mode that matters most in a system somebody uses daily. It
produced no exception, no log line and no visual defect. The application opened a
real workspace, drew it correctly, and was simply not the one the person asked
for. Somebody would have learned to distrust their own muscle memory long before
anybody diagnosed it.

Auditing the rest of the surface found the same class of problem twice more. The
palette advertised **twenty-six commands that nothing dispatched** — selecting one
did nothing at all — and **thirteen keyboard shortcuts the window never
installed**. The palette's own documentation stated that it lists only implemented
commands. That had stopped being true, and nothing could have noticed.

## Decisions

### 1. Destinations are named, not numbered

`SelectMenu(int)` is gone. `SelectWorkspace(string tag)` replaces it, and every
navigation command carries the tag of the workspace it opens.

An index is a fact about the current arrangement of a list. A tag is a fact about
the destination. Reordering the navigation pane, inserting a workspace, or hiding
one behind a permission cannot repoint a shortcut at something else, because
nothing about the shortcut refers to position any more.

### 2. One registry defines every command

`AgencyOsCommands.All` holds all **129** commands this build implements, across
**17** workspaces. The palette lists them, the window installs accelerators by
iterating them, and pages dispatch through the same identifiers.

The defect was not that any one of the three places was wrong. It was that there
were three places, so *no* place was authoritative and drift was invisible. A
single list is the only arrangement in which "the palette advertises a command
that does not exist" is a statement that can be false.

### 3. The registry validates at construction

`CommandRegistry` throws `CommandRegistryException` for a duplicate identifier, a
duplicate global gesture, a page gesture shadowed by a global one, a malformed
gesture, or a `Navigate` command with no workspace.

Deliberately at construction and not in a linting step. `CommandRegistry.Default`
is built by the application at startup and by nineteen tests, so a collision fails
the build rather than reaching a person as a shortcut that does the wrong thing.
The Ctrl+9 defect specifically could not survive this: two commands claiming one
gesture is exactly what it refuses.

### 4. A gesture is well-formed or it is not a gesture

`CommandGesture.IsWellFormed` refuses a bare letter and permits an unmodified key
only for the function keys and the named keys. A bare letter is what somebody
types into a text box.

### 5. One primary window

AgencyOS opens a single primary window with a navigation pane, not a
document-per-window shell.

The agency's work is cross-referential — a deal read against a contract read
against what somebody said last week — and a multi-window shell would need window
lifetime, per-window state and cross-window navigation before the first useful
screen. That is a large amount of machinery in service of a workflow nobody has
asked for. Dialogs remain modal to the primary window. This is a decision that can
be revisited when somebody has a concrete second-monitor workflow; it is not one
to build speculatively.

## What the registry found

Building the list forced a reconciliation, and the reconciliation is the
substance of this ADR rather than a footnote to it.

Of the twenty-six palette commands that dispatched to nothing:

- **seven** were real destinations that had simply never been wired — the
  `*.open` family — and became `Navigate` commands;
- **nineteen** described actions this build does not implement and were
  **removed**.

Removing them was the uncomfortable half. Each was a plausible-sounding entry that
made the product look more capable than it is. Keeping them would have meant
keeping a palette that lies, and the palette is the surface a keyboard-first user
trusts most.

Three pre-M13 tests asserted the old behaviour and were updated:
`FinanceViewModelTests`, `RepresentationViewModelTests`, `DealViewModelTests`.

## Four dialogs that no surface can reach

The audit found four complete, tested dialogs that nothing opens, because the list
surface each belongs to was never built:

- `RaiseReceivableDialog` (M9)
- `CalculateCommissionDialog` (M9)
- `RecordMonetaryObligationDialog` (M9)
- `AddIntelligenceSubjectDialog` (M11)

These are **inherited M9 and M11 gaps, not M13 gaps**, and M13 deliberately did
not repair them. Each needs a list surface with filtering, selection and empty
states — that is the missing part, not the dialog — and building four of those
inside a platform milestone would have meant doing M9 and M11 UI work under an
M13 heading, where it would get M13's review rather than a finance reviewer's.

They are recorded here so the next person finds a decision rather than a mystery.

## Why this is better than the alternatives

**A test asserting the accelerator table matches the menu.** Would have caught
Ctrl+9 and nothing else. The dead palette commands and uninstalled shortcuts were
a different symptom of the same cause, and a test per symptom leaves the cause.

**Generating accelerators from the pane at runtime.** Removes the duplication but
keeps position as the addressing scheme, so a shortcut still means "the ninth
thing" rather than "Finance".

**A linter over the source.** Runs where somebody can skip it. Construction-time
validation runs everywhere the registry is constructed, which is everywhere it is
used.

## Consequences

**What this buys.** A shortcut means one destination for the life of the build.
The palette can be trusted to list what exists. Adding a workspace cannot break an
unrelated shortcut, and adding a colliding command fails immediately with the name
of the collision.

**What it costs.** Adding a command means editing a central list rather than
adding a line near the code it belongs to, which is a real ergonomic loss and the
price of having one authority. Nineteen advertised capabilities disappeared from
the palette in a milestone that added none to replace them, so the product looks
smaller than it did — accurately.

**What is deliberately not here.** No user-remappable shortcuts, no per-user
palette history, no command aliases, no multi-window shell. Each is reasonable and
none is required to make the keyboard trustworthy, which was the problem.

**What the next milestone inherits.** A list that can be queried, so a permission
filter over the palette becomes a `Where` rather than a redesign.
