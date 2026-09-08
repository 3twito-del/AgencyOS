# ADR-0027 — OAuth credentials: protection at rest, and what an operator must provision

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0007 (authorization), ADR-0011 (tenant isolation),
  ADR-0026 (a mailbox belongs to a person)

## Context

Connecting a mailbox means AgencyOS holds a credential that can read and send
somebody's mail. That is not ordinary domain data. A refresh token in a database
backup is a mailbox in a database backup; a refresh token in a log line is a
mailbox in a log aggregator.

Everything below follows from treating the credential as the most dangerous value
in the system.

## Decisions

### 1. The token never reaches a client

The authorization code is exchanged **server-side**. The tokens it produces are
encrypted, stored, and never returned to anything.

`CommunicationAccountResponse` carries the mailbox address, the owner, the state,
the granted scopes, whether a credential is stored and when it expires. It has no
field that could hold a token, and no field that could hold part of one. The shape
of the record is part of the guarantee, and a test reads the raw JSON of every
communications response looking for one.

There is no endpoint that returns a token, refreshes one on a client's behalf, or
proxies a provider call with a client-supplied credential.

### 2. Encrypted at rest with ASP.NET Core Data Protection

`ISecretProtector` wraps Data Protection with a purpose string. The stored value
is ciphertext; an integration test asserts that what the database holds is not the
plaintext the provider issued and does not contain the authorization code.

**What this protects and what it does not.** It protects a leaked database, a
stolen backup and a misconfigured read replica. It does **not** protect a
compromised application server: the key ring is file-backed beside the
application, so anything that can run as the service can decrypt. Moving the keys
to a hardware module or a managed key service is the right next step and is not
done here.

That limitation is stated rather than glossed. A deployment that treats the
current arrangement as equivalent to envelope encryption with a managed KMS is
making a mistake this ADR is meant to prevent.

### 3. Nothing is logged, traced or exported

No token, no fragment, no authorization code appears in a log message, an
exception message, an OpenTelemetry attribute, an audit `semanticDelta` or an API
response. Telemetry for the communications subsystem carries counts, provider
names and state transitions.

### 4. Revocation destroys the credential and keeps the correspondence

Disconnecting a mailbox clears the stored token, marks the account
`Disconnected`, and stops synchronization and sending.

The messages already synchronized stay. They are a record of what was said, and
losing access to a mailbox is not a reason to lose the negotiation it carried.

### 5. AgencyOS authentication does not become Entra ID

Connecting a Microsoft mailbox is a delegated grant for *that mailbox*. It is not
a change to how people sign in to AgencyOS, and the two are deliberately
unconnected: an agency that uses Microsoft mail is not thereby an agency that
wants Microsoft to own its identity system.

### 6. Least privilege, and no more

`offline_access Mail.Read Mail.Send User.Read`, and nothing else. AgencyOS asks
for nothing about calendars, contacts, files, directories or other users, because
it uses none of them. A consent screen listing permissions the application does
not exercise trains administrators to approve without reading.

### 7. No credentials are committed, and none are fabricated

`GraphOptions` is configuration with no defaults for `ClientId` or
`ClientSecret`. When they are absent the adapter is **not registered at all** —
offering a mailbox provider that cannot connect is worse than not offering it —
and `GET /communication-providers` reports the provider as not configured rather
than handing back a dead link.

No secret, no tenant identifier and no test credential is in the repository.

An operator must, themselves:

1. Register an application in Microsoft Entra ID.
2. Add a redirect URI matching the one the client sends.
3. Grant delegated `Mail.Read`, `Mail.Send`, `offline_access`, `User.Read`.
4. Create a client secret and supply it as `AgencyOS:Graph:ClientSecret`.
5. Supply `AgencyOS:Graph:ClientId` and, for a single-tenant registration,
   `AgencyOS:Graph:TenantId`.

Until that is done, Microsoft mailboxes cannot be connected. That is the honest
state and the interface reports it as such.

### 8. The consent URL comes from the server

The Windows client does not construct an authorization URL, because doing so would
mean shipping the application registration in the desktop build.
`GET /communication-providers` returns, per provider, whether it is configured,
the scopes it will ask for, and the authorization URL to open. The URL carries the
application identifier and the scopes — both public by construction in OAuth — and
no secret.

The client opens it in the **system browser**, never in an embedded WebView. A
credential prompt inside the application is a credential prompt the application
could be reading, and the user has no address bar to check.

## Consequences

- A stolen database does not yield a mailbox.
- A compromised application server does. That is a real, stated limitation of the
  ALPHA key-management arrangement.
- Nothing in AgencyOS can hand a token to a client, because nothing has a place to
  put one.
- Microsoft mailboxes require an administrator to provision an application
  registration first, and the system says so plainly instead of failing obscurely.
