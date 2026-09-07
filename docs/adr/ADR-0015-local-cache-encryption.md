# ADR-0015: Encrypt the local cache with SQLCipher, key protected by DPAPI

Status: Accepted
Date: 2026-09-07

## Context

M3 puts a copy of agency data on a laptop for the first time. The cache holds
people, their contact details, who they work for, who introduced whom, and notes
about conversations. In this industry that set is commercially sensitive on its
own, and a laptop is the most likely thing in the system to be lost or stolen.

An unencrypted SQLite file is readable by anything that can read the file. Full
disk encryption helps only while the machine is off, and says nothing about
another process on the same machine under the same user.

The instruction was explicit: evaluate encryption before implementing, and do not
silently accept a plaintext cache of sensitive data.

## Decision

The cache is a SQLCipher database. The key is 32 random bytes generated on first
run and stored beside the cache, protected with Windows DPAPI at
`CurrentUser` scope. No key is hard-coded, and no key is committed.

Packages: `Microsoft.Data.Sqlite.Core` with `SQLitePCLRaw.bundle_e_sqlcipher`
(2.1.11) and `System.Security.Cryptography.ProtectedData`. The `.Core` package is
deliberate - the full `Microsoft.Data.Sqlite` bundles a non-cipher SQLite that
would win the provider race and silently produce an unencrypted database.

### This was verified, not assumed

A spike exercised the whole path before the design was committed:

```
created+wrote OK
read back with correct key: sensitive-contact-data
file header: '?5???\t{?|?)ew?'        <- not "SQLite format 3"
plaintext leak on disk: False          <- sentinel string absent from the raw bytes
wrong key rejected: 26 'file is not a database'
```

The header check matters: SQLCipher misconfiguration typically produces a
perfectly ordinary, perfectly readable SQLite file, and the failure is invisible
unless the bytes are inspected. The same three assertions - correct key opens,
wrong key fails, no plaintext on disk - are integration tests, so a future
packaging change that quietly disables encryption fails the build rather than
shipping.

### Key protection

DPAPI `CurrentUser` binds the key to the Windows user account. Another user on
the machine cannot unprotect it, and the key never exists on disk in the clear.
It is not bound to the machine alone, which would be weaker.

This is deliberately not a passphrase the user types. A passphrase good enough to
resist offline attack is one users write down, and the threat here is a lost
device rather than a compromised Windows account - if the account is compromised
the attacker can read whatever that account can read, encrypted cache or not.

### What is cached

Even encrypted, the cache holds only what the client needs offline: records the
user opened, the Command Center, active tasks, and recently synchronized people
and companies. It is never canonical, and deleting it loses nothing.

## Why this is better than the alternatives

Relying on BitLocker alone protects a powered-off machine and nothing else.

Encrypting individual fields inside a plain SQLite file would leave names and
identifiers in the clear - which is most of the sensitivity - and make every
query worse.

Not caching sensitive fields at all was the fallback the instruction allowed if
encryption could not be integrated cleanly. It integrated cleanly, so the
fallback is not needed; a cache without names would also have been close to
useless offline.

## Raw key rather than a derived one

The key is given to SQLCipher in its raw form (`PRAGMA key = "x'<64 hex>'"`)
rather than as a connection-string password. A password is run through PBKDF2 to
derive a key; here the key is already 256 bits from a cryptographic RNG, so
derivation adds cost and no entropy. Measured effect: opening a cache went from
roughly half a second to a few milliseconds, which matters because the offline
queue's interleaving test opens one per simulated scenario.

Opening the connection is not sufficient to prove the key. SQLCipher decrypts
lazily, so a wrong key produces an error only at the first real read; the cache
therefore issues a schema query immediately after keying, and turns the resulting
"file is not a database" into a message that tells the user resetting is safe.

## Consequences

- A native bundle joins the client dependency set. It ships for `win-x64` and
  `win-arm64`, which is exactly the client's supported surface.
- Losing the DPAPI-protected key - a new Windows profile, a restored machine -
  makes the cache unreadable. That is recoverable by design: the cache is rebuilt
  from the server, so the failure path is "sync again", and the client treats an
  unreadable cache as a reset rather than an error.
- Cache reset must delete the key file as well, or the new cache is created with
  a key that no longer matches. The reset path does both.

## Migration / rollback

The cache is disposable. Changing or removing encryption means deleting the local
file; no canonical data is involved.

## Evidence / metrics that would cause reconsideration

- The SQLCipher bundle failing to load on a supported architecture, which would
  force the restricted-content fallback.
- A requirement for the cache to survive a Windows profile change, which would
  need a different key custodian such as a credential-manager entry.
- Enterprise key-management requirements, which DPAPI does not satisfy.
