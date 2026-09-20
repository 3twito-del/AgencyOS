namespace AgencyOS.Contracts;

/// <summary>
/// The versioned API contract identity shared by the AgencyOS server and its clients.
/// </summary>
/// <remarks>
/// Governing document: <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. The release
/// authority answers a client handshake with a supported contract range; a client
/// outside that range is refused before it loads business data. That handshake is
/// implemented in M1 — M0 only establishes the identity it negotiates over.
/// </remarks>
public static class ApiContract
{
    /// <summary>
    /// The API contract version this build speaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>build/Version.props</c> holds the authoritative value and stamps it into
    /// assembly metadata. This constant must agree with it; the agreement is
    /// asserted by <c>AgencyOS.Tests.Unit</c> so the two cannot silently diverge.
    /// </para>
    /// <para>
    /// Version 2 added the M2 people slice. Version 3 adds M3 search, saved views,
    /// synchronization, and the optimistic concurrency token on existing records.
    /// </para>
    /// <para>
    /// Version 4 adds the M4 representation model: talent profiles, prospects,
    /// representations with effective-dated scope and team, credits and materials.
    /// </para>
    /// <para>
    /// Version 5 adds the M5 slate: projects with separate operational status and
    /// development stage, source properties, roles, attachments, company
    /// participation and packages.
    /// </para>
    /// <para>
    /// Version 6 adds the M6 pursuit layer: opportunities, their typed subjects,
    /// per-target market progression, recorded submissions with material snapshots,
    /// and pitches carried by one M2 interaction each.
    /// </para>
    /// <para>
    /// Version 7 adds the M7 deal engine: negotiations, the offer thread with its
    /// typed commercial terms, and agreed terms reached only by accepting an offer.
    /// </para>
    /// <para>
    /// Version 8 adds the M8 legal layer: contracts anchored to an accepted offer,
    /// drafting versions with transcribed terms, negotiated-against-drafted
    /// reconciliation, rights grants, options, obligations, notice requirements and
    /// recorded notices. It records document references rather than documents, and
    /// implements no electronic signature.
    /// </para>
    /// <para>
    /// Version 9 adds the M9 finance layer: monetary obligations derived from
    /// operative contracts, receivables, optional invoices, payments with explicit
    /// allocation, deductions and reconciliation, commission rules and entitlements,
    /// and a double-entry ledger with immutable posted entries. It records invoices
    /// rather than sending them, and holds no exchange rates.
    /// </para>
    /// <para>
    /// Version 10 adds documents and communications: a canonical content store
    /// addressed by SHA-256, immutable document versions, links to business records
    /// through typed foreign keys, connected mailboxes with delta synchronization,
    /// and outbound sending with provider evidence. It is the first contract whose
    /// operations cause an irreversible act outside AgencyOS, which is why an
    /// outbound send reports an unknown outcome as itself rather than as a failure.
    /// </para>
    /// <para>
    /// Version 11 adds intelligence: sources with provenance, signals that keep at
    /// least one of them, theses whose status has no "true" in it, predictions with
    /// a probability somebody stated and a Brier score afterwards, watchlists, a
    /// talent radar that hands over to M4 rather than duplicating it, and research
    /// cases. It adds no model, no summarizer and no score: every judgment in it
    /// belongs to a named person on a stated date.
    /// </para>
    /// <para>
    /// Version 12 adds the AI runtime: a provider-neutral model gateway, a registry
    /// of tools a model may ask for, an approval that binds to one exact action by
    /// fingerprint, and agent runs with the step history that lets a reader tell
    /// what the model said from what AgencyOS did. The model is untrusted input
    /// rather than a trusted component: it reads only what the caller may read,
    /// asks only for registered tools, and changes nothing without a person.
    /// </para>
    /// <para>
    /// Version 13 adds device-local inference: a residency for every execution
    /// target, and a single-use expiring context lease that lets the user's own
    /// workstation run a model over server-assembled, server-authorized context.
    /// The client becomes a model execution host and gains no authority — the
    /// server assembles the context, binds it by fingerprint, validates whatever
    /// comes back, and consumes the lease exactly once. Residency answers only
    /// where inference happens, so Restricted material stays unreachable at all
    /// three of them (ADR-0035).
    /// </para>
    /// <para>
    /// <para>
    /// Version 14 adds membership administration: reading who is in an
    /// organization, bringing somebody in, moving them between roles and ending
    /// their access. Until it, an organization's population was fixed at one
    /// person at first run — registering a user was reachable only from bootstrap,
    /// and membership was write-once with no read, no change and no revoke
    /// (<c>AOS-R002-002</c>, <c>AOS-R002-006</c>). Nothing about the authorization
    /// model changed: the same four roles confer the same permission sets, and the
    /// server decides as it always did.
    /// </para>
    /// <para>
    /// Version 15 adds <c>openOnly</c> to the negotiation list: one optional query
    /// parameter meaning the statuses that are still work, from
    /// <c>Deal.LiveStatuses</c> rather than a second list beside it. The Deals
    /// workspace asked for <c>Negotiating</c> alone, so a negotiation that reached
    /// <c>TermsAgreed</c> — agreed, waiting to be papered — disappeared from the
    /// workspace named after it. A caller that omits the parameter gets exactly
    /// what it got from contract 14.
    /// </para>
    /// <para>
    /// Version 16 widens what the negotiation, contract and project lists search.
    /// Each read only its own text, so a human name found nothing and a case was
    /// discoverable only by somebody who already knew what it was called - the one
    /// question a blind operator handoff could not get past. Search now also reaches
    /// the names of the people and companies each record genuinely relates to: a
    /// negotiation's counterparty and the client its pursuit is about, a contract's
    /// parties, and the people currently attached to a project. No relationship is
    /// invented to make this work, and a caller that searched a title still gets
    /// what it got from contract 15.
    /// </para>
    /// <para>
    /// Version 17 lets a task say who is accountable for it. The field has been on
    /// the entity and in the database since M2, and deal and contract follow-ups
    /// already set it — it simply never reached the contract, so a blind operator
    /// asking who owned an overdue action found no such field and concluded the
    /// model had no such idea. Tasks now carry an assignee and their name, a task
    /// may be created assigned, and an assignment can be changed or cleared, which
    /// is a real state: work is sometimes genuinely unowned and saying so is more
    /// honest than leaving the last person's name on it. An assignee must be an
    /// active member of the same organization. Nothing about existing tasks changes
    /// and no migration is required.
    /// </para>
    /// <para>
    /// Every step so far is additive, so the supported range stays open at 1. The
    /// concurrency guarantee does not depend on the contract version: the version
    /// token is a required field on guarded mutations, so a client that omits it
    /// gets a 400 rather than a silent overwrite, whatever contract it claims.
    /// </para>
    /// </remarks>
    public const int Current = 17;

    /// <summary>
    /// The lowest contract version this build still serves.
    /// </summary>
    /// <remarks>
    /// Version 1 remains supported because every version-2 change is additive: a
    /// contract-1 client simply does not call the new endpoints. It drops only when
    /// a change makes an older client genuinely unsafe.
    /// </remarks>
    public const int MinimumSupported = 1;
}
