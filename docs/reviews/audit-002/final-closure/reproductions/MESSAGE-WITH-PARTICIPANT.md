# Creating a message with a participant, through canonical routes only

The reproduction that changed `ResolveParticipantDialog`'s classification. Every
step is a route the product exposes and the Windows client calls; nothing was
written to PostgreSQL directly and nothing was scripted in-process.

**Tenant** `01a0a015-c44e-7f0e-9c4c-380e118b5e2b` · **subject** `w2-owner` ·
**provider** `FakeCommunicationProvider` only.

---

## What Audit 002 concluded, and what it missed

Phase D:

> There is no route that creates an inbound message. Messages arrive only by
> mailbox synchronisation, the only registered provider is the fake one, and it
> reports `isConfigured: false`.

Every word of that is true, and it answers a question `ResolveParticipantDialog`
does not ask. The dialog's opener guards on a **participant**, not on an inbound
message:

```csharp
if (_messageDetail is null
    || ParticipantList.SelectedItem is not ParticipantResponse participant)
{
    Error("Select a participant first.");
    return;
}
```

`CommunicationMessage.AddParticipant` has two callers, not one:

| Caller | Origin |
| --- | --- |
| `MailboxSynchronizer` | inbound sync — external |
| `OutboundSendProcessor.RecordSentMessageAsync` | **AgencyOS sending a message** |

The second is reachable through the API.

---

## The steps

### 1. Connect a mailbox

The same call `ConnectMailboxDialog` makes. The fake accepts any authorization
code and returns a connection.

```
POST /api/v1/organizations/{org}/communication-accounts
{"provider":"Fake","authorizationCode":"closure-…",
 "redirectUri":"http://localhost:5173/oauth/callback","visibility":"Shared"}
→ 200 {"accountId":"01a0a6a9-2a0f-7242-809e-68026c037e8e"}
```

State `Connected`. The tenant's pre-existing mailbox was in `Error` and refused
to send — `"That mailbox is error and cannot send."` — which is a correct domain
refusal and the reason a fresh one was connected.

### 2. Compose

```
POST /api/v1/organizations/{org}/outbound-messages
{"accountId":"…","subject":"Closure slice probe","bodyText":"Probe body.",
 "recipients":[{"role":"To","address":"counterparty@example.test",
                "displayName":"A Counterparty"}]}
→ 201 {"dispatchId":"01a0a6a9-4f3e-7d7f-ad36-ef16be65d843"}
```

### 3. Queue

```
POST /api/v1/organizations/{org}/outbound-messages/{dispatch}/queue
{"expectedVersion":1}
→ 204
```

### 4. The worker sends it

No test hook. `CommunicationWorker`, the background service the API host runs,
leased the dispatch and walked it to `Sent` against the fake provider.

```
state = Sent   sentMessageId = 01a0a6a9-8c75-71d3-a612-28e6181f3b2b
```

### 5. The message exists, with an unresolved participant

```
GET /api/v1/organizations/{org}/messages           → 1 message, Outbound
GET /api/v1/organizations/{org}/messages/{id}
  participants: 1
    01a0a6a9-8c77-74e1-be2f-ffe5100491e7  To  counterparty@example.test  personId=None
  attachments: 0
```

`personId = None` is exactly the state the dialog exists to settle.

---

## The same test, for attachments — which fails

Run deliberately, because `IngestAttachmentDialog` needs an attachment **row**
rather than a flag, and the outbound path looked like it might produce one.

A document was uploaded through the product's own route so the bytes really
existed (an earlier attempt used a Phase C synthetic version whose blob was gone
and failed with `BlobNotFoundException`, which is a fixture artifact and not a
product fault):

```
POST …/documents  (multipart)  → 201 versionId=01a0a6ab-fe5b-70d3-9a94-67a675a853ec
```

Composed with `attachmentVersionIds`, queued, and sent:

```
state = Sent   sentMessageId = 01a0a6ac-3216-7b31-a3ba-3ceb88541d77

GET …/messages/{id}
  hasAttachments  : true
  attachmentCount : 0
  attachments     : []
```

**Measured, not inferred.** A sent message carries the flag and no rows, because
`RecordSentMessageAsync` records participants and does not record attachments —
the provider's own copy arrives on the next synchronisation and keys to the same
external identifier. So the outbound path cannot produce what
`IngestAttachmentDialog` needs, and only `MailboxSynchronizer` can.
