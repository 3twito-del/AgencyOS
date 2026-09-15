# Audit 002 — the three precondition dialogs, decided

§1 and §2 of the closure brief. Each was traced through domain, application, API,
contracts, client, command registry, opener and roadmap **before** any code was
written, and each gets exactly one classification.

No route, capability or fixture shortcut was added to make any of them reachable.

---

## 1. `ApproveAiActionDialog`

### Final classification: `INTENTIONALLY_EXTERNAL_PRECONDITION`

**What state it requires.** One `AiApproval` in `Pending`, unexpired, for the
calling organization.

**Where that state comes from.** One place, `AgentRuntime`:

```csharp
if (tool.Effect != ToolEffect.ReadOnly)
{
    AiApproval approval = AiApproval.Request(…);
    _approvals.Add(approval);
```

An approval exists because a **model's response asked to run a write tool**. It
is not something a person creates, and not something a client can ask for.

**Does the API expose a creation path?** No, and deliberately not.
`M12Endpoints` says so in its own words:

> There is deliberately no route that hands the model an action to perform,
> because the model is untrusted input rather than a caller. **Nothing here
> accepts a tool name or arguments from a client.**

The surface is `GET /ai/approvals`, `GET /ai/approvals/{id}`,
`POST /ai/approvals/{id}/decision`, `POST /ai/tool-requests/{id}/execute`. Read,
decide, execute. Nothing creates.

**Is there a canonical synthetic path already present?** Partly, and not one an
operator or an out-of-process harness can use. `FakeModelProvider` is the **only**
`IModelProvider` registered in any environment:

```csharp
services.AddSingleton<FakeModelProvider>();
services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<FakeModelProvider>());
```

It answers only what a test queued through `Script…`, which is an in-process API.
`src/AgencyOS.Api` does not reference it, and no route reaches it. Unscripted, it
returns `ProviderUnavailable`, so a run started through `POST /ai/runs` fails and
proposes nothing. The integration suite creates approvals exactly this way —
`EnableProviderAsync`, `ScriptToolCall`, `POST /ai/runs` — from inside the host
process, which a reviewer driving the real client is not.

**Is the missing precondition intentional?** Yes, twice over. The absence of a
creation route is a security property. The absence of a real provider is a
roadmap position:

> **No real provider has been called.** The gateway has one adapter, and it is the
> deterministic fake. … the first one connected should be connected in FORGE
> against synthetic data.
>
> **The Windows surface has been compiled, not operated.** … Nobody has sat in
> front of the approval dialog and decided a real proposal.

**Is the dialog intended for current product use?** Yes. M12 shipped and was
promoted to ALPHA. The workflow is present-tense; its trigger is an external
actor that has not been connected.

**Would adding a creation path be a real capability?** No — it would be the
precise hole M12 was designed to exclude: a route that lets a caller manufacture
an approval for a tool and arguments of its choosing. It was not added.

**Verdict.** The state originates outside AgencyOS, by design, and AgencyOS is
not responsible for creating it. Its canonical runtime source exists as a seam
with one deterministic adapter and cannot be exercised by an operator today, so
under §14 it does not count as currently reachable.

---

## 2. `IngestAttachmentDialog`

### Final classification: `INTENTIONALLY_EXTERNAL_PRECONDITION`

**What state it requires.** A `CommunicationAttachment` row on a message —
`AttachmentList.SelectedItem is MessageAttachmentResponse`.

**Where that state comes from.** `CommunicationMessage.AddAttachment` has exactly
one caller in the whole repository: `MailboxSynchronizer`. Attachment rows exist
because a mailbox synchronisation brought them in.

**Measured, not inferred.** The outbound path was tested to destruction because
`OutboundDispatch` also has attachments and might have produced rows. A document
was uploaded through the product's own route, attached to a composed message,
queued, and sent by the background worker:

```
state = Sent
hasAttachments  : true
attachmentCount : 0
attachments     : []
```

A sent message carries the flag and no rows. The provider's own copy arrives on
the next synchronisation and keys to the same external identifier, which is the
design. So the outbound path cannot produce what this dialog needs.

**Is there a canonical real source?** Yes, and it is complete:
`GraphCommunicationProvider` is a full Microsoft Graph adapter, registered when
`GraphOptions.IsConfigured`. This environment has no Microsoft tenant, so only
`FakeCommunicationProvider` is registered — and its `FakeMailbox.Inbox` is
populated in-process by tests, with no route.

**Is the state intentionally external?** Yes, and the page says why:

> Messages and mailboxes are evidence: mail that passed between real people,
> synchronized so it can be filed against the business.

A product button that fabricated an inbound message would be a button that
fabricates evidence. It was not added.

**Is the opener healthy?** Yes — verified, because an unreachable precondition and
a broken opener look identical from outside. Running `attachment.ingest` with no
attachment selected produces a visible refusal:

> **That did not happen** — Select an attachment first.

Not the `AOS-R002-019` class.

**Verdict.** The state originates outside AgencyOS. Its canonical runtime source
exists and is production-complete, and cannot be exercised here without a real
Microsoft tenant and a real mailbox — which §12 and this audit's safety rules
forbid. Not currently reachable.

---

## 3. `ResolveParticipantDialog`

### Final classification: `AUDIT_FIXTURE_GAP`

**Classified independently of `IngestAttachmentDialog`, and it diverges.**

**What state it requires.** A selected message and a selected participant. The
guard reads `ParticipantList.SelectedItem`. **It does not require an inbound
message** — it requires a message with a participant.

**Where that state comes from.** `CommunicationMessage.AddParticipant` has two
callers, and Phase D's analysis considered only the first:

| Caller | Origin |
| --- | --- |
| `MailboxSynchronizer` | inbound sync — external |
| `OutboundSendProcessor.RecordSentMessageAsync` | **AgencyOS sending a message** |

**Exercised, through canonical routes only.** Connect a mailbox → compose → queue
→ the background `CommunicationWorker` sends it via the fake provider → a sent
message is recorded with its recipients as participants:

```
GET …/messages/{id}
  participants: 1
    To  counterparty@example.test  personId=None
```

Full record: [`reproductions/MESSAGE-WITH-PARTICIPANT.md`](reproductions/MESSAGE-WITH-PARTICIPANT.md).

**Then it opened.** In the audit's own `dialog-runtime` pass, unmodified:

```
OPENED  ResolveParticipantDialog  PALETTE  focus-in=yes escape-closed=yes a11y=0
```

**Why Audit 002 missed it.** Phase D asked "can an inbound message be created"
and correctly answered no. The dialog never needed one. The fixture had no
messages of any direction, and the outbound route that would have produced one
was never run.

**Verdict.** A canonical creation path already existed and the audit did not use
it. Currently reachable, and now opened.
