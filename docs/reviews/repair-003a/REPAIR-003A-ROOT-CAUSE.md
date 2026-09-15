# AOS-R002-019 — the root cause, traced

§2 and §3 of the repair brief. What follows was measured, not inferred. Where a
claim rests on reading rather than on running, it says so.

---

## 1. The mechanism, in one paragraph

A control declares its starting state in the markup — a slider's `Value="50"`, a
combo box item's `IsSelected="True"` — **and** wires the event that state raises.
The XAML parser therefore calls the handler *while `InitializeComponent()` is
still running*, against a dialog that is half built: named controls declared
further down the document have not been created yet, and the code-behind's own
fields have not been assigned, because the constructor assigns them after
`InitializeComponent()` returns. The handler dereferences one of those and throws
`NullReferenceException`. The parser wraps it as `XamlParseException`,
`InitializeComponent()` throws, the exception leaves the dialog constructor,
leaves the page's `…Async()` opener, and is discarded by the fire-and-forget
`_ = …Async()` dispatch that runs it.

The operator clicks a button, and nothing happens. No dialog, no refusal, no
error bar, no focus change, no log.

---

## 2. Is this one root or four?

**`ONE_SHARED_ROOT`** — one mechanism, four independent instances of it.

There is no shared helper to repair. Each dialog reaches a different piece of
not-yet-existing state, so the repair is four call-site changes rather than one,
and the shared thing is a rule rather than a function. The rule is now a test.

| Dialog | Markup that fires during load | Handler | What did not exist yet |
| --- | --- | --- | --- |
| `ConnectMailboxDialog` | `VisibilityBox`, item `IsSelected="True"` (line 39) | `OnChanged` → `Update()` | `_providers`, assigned after `InitializeComponent()` |
| `CreatePredictionDialog` | `ProbabilitySlider Value="50"` (line 37) | `OnProbabilityChanged` → `ApplyProbability()` | `ProbabilityText` (line 41) |
| `RecordSourceDialog` | `KindBox`, item `IsSelected="True"` (line 24) | `OnKindChanged` → `ApplyKind()` | `UrlBox`, `CustodyBar`, `PublishedPicker`, `TitleBox` |
| `ResolvePredictionDialog` | `OutcomeBox`, item `IsSelected="True"` (line 22) | `OnOutcomeChanged` → `ApplyScoring()` | `ScoringBar` |

---

## 3. How it was established

Three independent lines, and none of them is "it looks like this".

### 3.1 The exception, caught at run time

Temporary instrumentation — a `try`/`catch` that logged and rethrew, applied to
the four opener methods, built, run, and reverted before any repair — caught the
throw on the real path, from the real button and the real palette command. It is
not in the shipped tree; the log is
[`evidence/exception-trace-before.log`](evidence/exception-trace-before.log).

```
ConnectMailboxAsync: ENTERED
ConnectMailboxAsync: THREW Microsoft.UI.Xaml.Markup.XamlParseException: XAML parsing failed.
   at Microsoft.UI.Xaml.Application.LoadComponent(...)
   at AgencyOS.Windows.Dialogs.ConnectMailboxDialog.InitializeComponent()
   at AgencyOS.Windows.Dialogs.ConnectMailboxDialog..ctor(IReadOnlyList`1 providers, String redirectUri)
   at AgencyOS.Windows.Pages.CommunicationsPage.ConnectMailboxAsync()
```

For two of the four the parser named the exact attribute it was assigning when
the handler threw:

```
CreatePredictionAsync:  Failed to assign to property 'RangeBase.Value'. [Line: 37 Position: 97]
ResolvePredictionAsync: … [Line: 24 Position: 83]
RecordSourceAsync:      … [Line: 24 Position: 70]
```

`CreatePredictionDialog.xaml` line 37 is the slider's `Value="50"`. The other two
line 24s are the last child of the combo box whose first item declares
`IsSelected="True"`. The property *name* in the second and third is wrong — WinUI
could not find the text for its own error code, and reused the first one's — but
the position is exact and points at the markup that raises the event.

### 3.2 Which thing was null, measured

Second temporary instrumentation, also reverted: each load-time handler logged
whether each control it touches was null at the moment it ran.

```
ConnectMailboxDialog.Update:            ProviderBox null=False  ConsentButton null=False
                                        CodeBox null=False      RedirectBox null=False
                                        VisibilityBox null=False  _providers null=True
CreatePredictionDialog.ApplyProbability: ProbabilitySlider null=False  ProbabilityText null=True
RecordSourceDialog.ApplyKind:            KindBox null=False  UrlBox null=True
                                         CustodyBar null=True  PublishedPicker null=True
                                         TitleBox null=True
ResolvePredictionDialog.ApplyScoring:    OutcomeBox null=False  ScoringBar null=True
                                         NoteBox null=True
```

Every null is a control declared *after* the one that fired, or a field the
constructor assigns *after* `InitializeComponent()`. Nothing declared before it
was ever null. That is the mechanism stated exactly, and it is why the repair is
an ordering rule rather than a null sweep.

[`evidence/exception-trace-connectmailbox.log`](evidence/exception-trace-connectmailbox.log)
holds the `_providers` measurement, which was taken separately because the first
pass had not thought to ask about a field.

### 3.3 The repository already knew

Fourteen controls across the client declare load-time state and wire the event it
raises. Ten of them are in dialogs that open, and **every one of those ten already
carries the guard**:

```csharp
if (ReceivedPicker is null || SentPicker is null)
{
    return;
}
```

`RecordContractVersionDialog`, `RecordNoticeDialog`, `AddMemberDialog`,
`AnswerOfferDialog`, `CreateCommissionRuleDialog`, `ResolveObligationDialog`,
`ResolveOptionDialog`, `RaiseReceivableDialog`,
`RecordMonetaryObligationDialog` — all guarded. The four that do not open are
exactly the four that do not guard. The convention existed and these four were
outside it, which is why the repair adopts the convention rather than inventing
one.

---

## 4. §2's questions, answered

Per dialog the answers are the same, so they are given once.

| # | Question | Answer |
| :-: | --- | --- |
| 1 | Is the handler invoked? | **Yes** — `…Async: ENTERED` for all four. |
| 2 | Does it return early? | No. It throws before any guard is reached. |
| 3 | Does it throw? | **Yes** — `XamlParseException` wrapping a `NullReferenceException`. |
| 4 | Is an exception swallowed? | **Yes** — by `_ = …Async()`, the fire-and-forget dispatch. |
| 5 | Is a Task fire-and-forget? | **Yes**, at both entry points: the `Click` handler and `Execute(commandId)`. |
| 6 | Is `ShowAsync` awaited? | Yes — but it is never reached. |
| 7 | Is `XamlRoot` valid? | Yes. Set from the page in every case, and never reached either. |
| 8 | Is the owner/root correct? | Yes. |
| 9 | Is the dialog constructed? | **No.** The constructor throws inside `InitializeComponent()`. |
| 10 | Is an instance reused illegally? | No. A new instance per invocation. |
| 11 | Is another `ContentDialog` active? | No. The tree before invocation holds no popup. |
| 12 | Is UI-thread affinity violated? | No. The handler runs on the dispatcher. |
| 13 | Does navigation reset context first? | No. `ConnectMailboxDialog` had its row selected and `ResolvePredictionDialog` its prediction; the tree confirms both. |
| 14 | Does a converter/template issue prevent invocation? | No. The opener is reached; the failure is after it. |
| 15 | Is the command pointing at a different handler? | No. `Execute` dispatches to the method the name says, confirmed by the entry log. |

The `Guarded(…)` wrapper on `CommunicationsPage` catches `AgencyOsApiException`
only, so it would not have caught this even if the construction had been inside
it. It is not.

---

## 5. What this does *not* claim

- **The four openers are not the only fire-and-forget dispatch sites.** There are
  218 in the client. That an unexpected exception from any of them is discarded
  without a word is a real property of the code and a defect class of its own. It
  is **not repaired here** and is recorded in the report as an observation for a
  later wave: repairing it means touching every page, which §4 and §13 forbid.
- **No human operator has clicked these buttons with a mouse.** Every invocation
  was UI Automation `Invoke` on the real control, or the real command palette
  driven by real keystrokes. That raises the same events the product handles, and
  it is not the same as a person.
- **The `ConnectMailboxDialog` parse error carried no line number**, so its
  failing attribute is identified from the null measurement and the generated
  connection order rather than from the parser's own message.
