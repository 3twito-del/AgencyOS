namespace AgencyOS.Domain.Authorization;

/// <summary>
/// The permission vocabulary. Every protected operation names one of these.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are strings rather than an enum so a permission can be added
/// without a breaking renumber, and so the same literal appears in the endpoint
/// declaration, the role map and the audit record.
/// </para>
/// <para>
/// Authorization is always evaluated server-side
/// (<c>docs/07_SECURITY_AND_AUDIT.md</c>): the Windows client is never trusted to
/// enforce permissions.
/// </para>
/// </remarks>
public static class Permission
{
    // ---- Tenant administration (M1) ----

    public const string OrganizationsRead = "organizations.read";
    public const string OrganizationsCreate = "organizations.create";
    public const string OrganizationsArchive = "organizations.archive";

    public const string MembershipsRead = "memberships.read";
    public const string MembershipsGrant = "memberships.grant";
    public const string MembershipsRevoke = "memberships.revoke";

    public const string AuditRead = "audit.read";

    public const string ReleasePolicyRead = "release.policy.read";
    public const string ReleasePolicyManage = "release.policy.manage";

    // ---- People and companies (M2) ----
    //
    // Read/write pairs per aggregate rather than one permission per command.
    // Commands stay explicit at the API; the grant model stays reviewable.

    public const string PeopleRead = "people.read";
    public const string PeopleWrite = "people.write";

    public const string CompaniesRead = "companies.read";
    public const string CompaniesWrite = "companies.write";

    public const string RelationshipsRead = "relationships.read";
    public const string RelationshipsWrite = "relationships.write";

    public const string InteractionsRead = "interactions.read";

    /// <summary>Recording an interaction. Named for the act, because it is append-like.</summary>
    public const string InteractionsRecord = "interactions.record";

    public const string TasksRead = "tasks.read";
    public const string TasksWrite = "tasks.write";

    // ---- Talent and representation (M4) ----
    //
    // Coarse read/write pairs per aggregate, matching M2's grant model, plus one
    // deliberately finer permission. Representation-team membership is NOT an
    // authorization dimension: being on a team says who works a relationship, not
    // who may read it, and letting it grant access would make assignment a way to
    // escalate (ADR-0017).

    public const string TalentRead = "talent.read";
    public const string TalentWrite = "talent.write";

    /// <summary>
    /// Reading internal judgment: talent positioning and prospect strategy.
    /// </summary>
    /// <remarks>
    /// The one fine-grained permission M4 adds, because these two fields are
    /// materially more sensitive than the records holding them. A caller without it
    /// receives the record with those fields absent rather than a refusal, so an
    /// observer can still see who a client is without reading what the agency
    /// privately thinks about them.
    /// </remarks>
    public const string TalentNotesRead = "talent.notes.read";

    public const string RepresentationRead = "representation.read";
    public const string RepresentationWrite = "representation.write";

    public const string ProspectsRead = "prospects.read";
    public const string ProspectsWrite = "prospects.write";

    // ---- Projects and packaging (M5) ----
    //
    // Coarse read/write pairs again, plus one finer permission. A project's
    // logline is a shared fact about a piece of work; a package's strategy is what
    // the agency privately thinks its play is, and routinely names who it expects
    // to pass. That is a materially different sensitivity (ADR-0019).

    public const string ProjectsRead = "projects.read";
    public const string ProjectsWrite = "projects.write";

    public const string PackagesRead = "packages.read";
    public const string PackagesWrite = "packages.write";

    /// <summary>Reading a package's internal strategy.</summary>
    /// <remarks>
    /// Absent rather than refused, exactly as <see cref="TalentNotesRead"/> works:
    /// an observer can see that a package exists and what is in it without reading
    /// the agency's private reasoning about it.
    /// </remarks>
    public const string PackageStrategyRead = "packages.strategy.read";

    // ---- Opportunities and submissions (M6) ----
    //
    // Submissions are separated from opportunities deliberately. Recording a
    // submission asserts that something left the building and became provenance
    // somebody may later rely on; who may make that assertion is worth being able
    // to control apart from who may edit a pursuit (ADR-0020).

    public const string OpportunitiesRead = "opportunities.read";
    public const string OpportunitiesWrite = "opportunities.write";

    public const string SubmissionsRead = "submissions.read";
    public const string SubmissionsWrite = "submissions.write";

    /// <summary>Reading an opportunity's internal strategy.</summary>
    /// <remarks>
    /// The most sensitive text the system holds: it names who the agency expects to
    /// pass, what it will settle for, and which buyer is being kept until last.
    /// Absent rather than refused, as <see cref="TalentNotesRead"/> works, and kept
    /// out of the search vector so its terms cannot be confirmed by searching.
    /// </remarks>
    public const string OpportunityStrategyRead = "opportunities.strategy.read";

    // ---- Deals and offers (M7) ----
    //
    // Four grants rather than two, because the populations genuinely differ.
    // Knowing a negotiation exists is what a coordinator needs to schedule around
    // it; knowing what it pays is not, and an agency that had to choose between
    // showing an assistant everything and showing them nothing would end up
    // showing them everything (ADR-0021).

    public const string DealsRead = "deals.read";
    public const string DealsWrite = "deals.write";

    /// <summary>Recording offers and answering them.</summary>
    /// <remarks>
    /// Separated from <see cref="DealsWrite"/> on the M6 precedent for
    /// submissions: recording an offer asserts that a commercial proposal passed
    /// between the parties, and accepting one asserts the terms are agreed. Who
    /// may make those assertions is worth controlling apart from who may rename a
    /// deal.
    /// </remarks>
    public const string OffersRead = "offers.read";
    public const string OffersWrite = "offers.write";

    /// <summary>Reading what a deal actually pays.</summary>
    /// <remarks>
    /// Gates every money and percentage term, everywhere: detail, offers,
    /// comparison, search, saved views, overview, command centre and counts.
    /// Structural terms - dates, episode counts, billing - stay visible with
    /// <see cref="DealsRead"/>, and the catalog guarantees no structural term
    /// carries a figure.
    /// </remarks>
    public const string DealEconomicsRead = "deals.economics.read";

    /// <summary>Reading the agency's own negotiating strategy.</summary>
    /// <remarks>
    /// Judgment rather than fact. "They offered 500,000" is a term; "we think they
    /// can reach 750,000 and should trade backend for guarantee" is this, and the
    /// two must not share a field. Absent rather than refused, as
    /// <see cref="TalentNotesRead"/> works, and kept out of the search vector.
    /// </remarks>
    public const string DealStrategyRead = "deals.strategy.read";

    // ---- Contracts, rights and obligations (M8) ----
    //
    // More grants than any previous milestone, because M8 holds the most varied
    // sensitivities the system has seen: whether a contract exists, what it says,
    // what it pays, and what counsel thinks of it are four different questions with
    // four different readerships (ADR-0022).

    public const string ContractsRead = "contracts.read";
    public const string ContractsWrite = "contracts.write";

    /// <summary>Reading the structured terms drafted into a contract.</summary>
    /// <remarks>
    /// Separate from <see cref="ContractsRead"/> because knowing an agreement
    /// exists and being able to read its clauses are different things. The money
    /// inside those terms is gated again by <see cref="DealEconomicsRead"/>, which
    /// is the same grant M7 uses: it is the same economics, and a parallel grant
    /// would drift from it.
    /// </remarks>
    public const string ContractTermsRead = "contracts.terms.read";

    /// <summary>Reading legal analysis and anything classified privileged.</summary>
    /// <remarks>
    /// Gates content a person has explicitly marked as legal strategy or
    /// attorney-client privileged. AgencyOS never infers that classification:
    /// privilege is a legal status with legal consequences, and guessing would be
    /// wrong in both directions.
    /// </remarks>
    public const string ContractPrivilegedRead = "contracts.privileged.read";

    /// <summary>Reading rights grants and the options over them.</summary>
    public const string RightsRead = "rights.read";
    public const string RightsWrite = "rights.write";

    /// <summary>Reading obligations, notice requirements and recorded notices.</summary>
    public const string ObligationsRead = "obligations.read";
    public const string ObligationsWrite = "obligations.write";

    // ---- Finance (M9) ----
    //
    // The most tightly held grants in the system, and deliberately disjoint from
    // everything above. Reading what a deal pays is a commercial question;
    // reading what the agency has collected, what it is owed, and what its books
    // say are three financial ones. deals.economics.read confers none of them
    // (ADR-0023).

    /// <summary>Reading receivables, invoices and the finance work queues.</summary>
    public const string FinanceRead = "finance.read";

    /// <summary>Creating receivables and recording invoices.</summary>
    public const string FinanceWrite = "finance.write";

    /// <summary>Reading payments and how they were applied.</summary>
    /// <remarks>
    /// Separate from <see cref="FinanceRead"/> because knowing that a hundred
    /// thousand is owed and knowing that eighty of it arrived last Tuesday are
    /// different disclosures. The second names bank movements.
    /// </remarks>
    public const string FinancePaymentsRead = "finance.payments.read";

    /// <summary>Recording payments, allocating them and reversing either.</summary>
    public const string FinancePaymentsWrite = "finance.payments.write";

    /// <summary>Reading commission rules, entitlements and what has been collected.</summary>
    /// <remarks>
    /// What the agency earns from a client is the most sensitive number in the
    /// relationship, and it is gated on its own rather than folded into
    /// <see cref="FinanceRead"/>.
    /// </remarks>
    public const string FinanceCommissionsRead = "finance.commissions.read";

    /// <summary>Setting commission rules and calculating entitlements.</summary>
    public const string FinanceCommissionsWrite = "finance.commissions.write";

    /// <summary>Reading the ledger: accounts, journal entries and balances.</summary>
    public const string FinanceLedgerRead = "finance.ledger.read";

    /// <summary>
    /// Posting to the ledger.
    /// </summary>
    /// <remarks>
    /// The narrowest grant in AgencyOS. Posting is the one irreversible act in the
    /// milestone - a posted entry can be reversed but never unsaid - so it is held
    /// separately from every other finance permission and given to nobody by
    /// default below administrator.
    /// </remarks>
    public const string FinanceLedgerPost = "finance.ledger.post";

    /// <summary>
    /// Recording deductions, write-offs and commission adjustments.
    /// </summary>
    /// <remarks>
    /// Each of these reduces what somebody is owed by a decision rather than by a
    /// payment, so it is held apart from ordinary finance writing.
    /// </remarks>
    public const string FinanceAdjustmentsWrite = "finance.adjustments.write";

    // ---- Documents and communications (M10) ----
    //
    // Two families, deliberately separate from each other and from everything
    // above. Reading a deal grants nothing about the contract PDF attached to it,
    // and reading a document grants nothing about the email it arrived on
    // (ADR-0025, ADR-0026).

    /// <summary>Listing and reading documents and their metadata.</summary>
    /// <remarks>
    /// The floor for every document surface. It does not, on its own, open a
    /// privileged, restricted or financial document: those carry their own grants
    /// below, because sensitivity is a property of the document and not of what it
    /// happens to be linked to (ADR-0025).
    /// </remarks>
    public const string DocumentsRead = "documents.read";

    /// <summary>Adding documents, uploading versions and archiving.</summary>
    public const string DocumentsWrite = "documents.write";

    /// <summary>
    /// Reading a document classified as legally privileged.
    /// </summary>
    /// <remarks>
    /// The M8 contract-privilege grant guards a lawyer's analysis written into
    /// AgencyOS; this guards the file itself. Somebody who may read the contract
    /// record does not thereby get the privileged memo attached to it.
    /// </remarks>
    public const string DocumentsPrivilegedRead = "documents.privileged.read";

    /// <summary>Reading a document classified as restricted.</summary>
    /// <remarks>
    /// The narrowest document classification, for material with a deliberately
    /// small readership. Held separately so it can be granted to a handful of
    /// people without also opening every privileged legal document.
    /// </remarks>
    public const string DocumentsRestrictedRead = "documents.restricted.read";

    /// <summary>Connecting a document to a business record, or disconnecting one.</summary>
    /// <remarks>
    /// A link is an assertion about what a document is evidence of, so it is a
    /// write even though it changes no bytes.
    /// </remarks>
    public const string DocumentsLink = "documents.link";

    /// <summary>
    /// Reading synchronized messages from a mailbox the caller owns.
    /// </summary>
    /// <remarks>
    /// Ownership still decides which mailbox. This grant says a person may use the
    /// communications surface at all; it does not open anybody else's mail
    /// (ADR-0026).
    /// </remarks>
    public const string CommunicationsRead = "communications.read";

    /// <summary>
    /// Reading a mailbox somebody else owns, or one shared with the agency.
    /// </summary>
    /// <remarks>
    /// Held apart from <see cref="CommunicationsRead"/> because sharing a tenant
    /// with somebody is not a reason to read their correspondence. A message being
    /// linked to a deal does not change that either: the link is context, and the
    /// content is still the mailbox owner's (ADR-0026).
    /// </remarks>
    public const string CommunicationsSharedRead = "communications.shared.read";

    /// <summary>
    /// Sending an external message through a connected provider.
    /// </summary>
    /// <remarks>
    /// The most consequential grant M10 adds. Every other action in the milestone
    /// records something that already happened; this one causes something to happen
    /// outside AgencyOS, to a real person, irreversibly (ADR-0028).
    /// </remarks>
    public const string CommunicationsSend = "communications.send";

    /// <summary>Connecting, reconfiguring and disconnecting a mailbox.</summary>
    /// <remarks>
    /// Account management is where credentials are exchanged, so it is separate
    /// from using the mailbox once it is connected.
    /// </remarks>
    public const string CommunicationsAccountManage = "communications.account.manage";

    // ---- Intelligence (M11) ----

    /// <summary>
    /// Reading ordinary intelligence: signals, theses, predictions, watchlists,
    /// radar entries and research cases.
    /// </summary>
    /// <remarks>
    /// The floor, and it is not automatic. Intelligence is what the agency thinks
    /// rather than what it has recorded happening, and the two deserve different
    /// doors (ADR-0030).
    /// </remarks>
    public const string IntelligenceRead = "intelligence.read";

    /// <summary>Recording and revising intelligence.</summary>
    public const string IntelligenceWrite = "intelligence.write";

    /// <summary>
    /// Reading intelligence classified confidential, source-sensitive or
    /// restricted.
    /// </summary>
    /// <remarks>
    /// One grant covering the three elevated classifications, on the M10 precedent:
    /// splitting them would be three doors that in practice open for the same
    /// people, and three chances to grant the wrong one. What it protects is
    /// unusually sharp — a recruiting thesis about a client's dissatisfaction, or a
    /// signal whose value is that its source will not be named (ADR-0030).
    /// </remarks>
    public const string IntelligenceSensitiveRead = "intelligence.sensitive.read";

    /// <summary>
    /// Stating and revising a forecast, and resolving one.
    /// </summary>
    /// <remarks>
    /// Separate from ordinary intelligence writing because a forecast carries the
    /// forecaster's name and feeds calibration. Somebody who records signals is not
    /// thereby somebody whose probability estimates belong in the agency's track
    /// record.
    /// </remarks>
    public const string IntelligencePredictionsWrite = "intelligence.predictions.write";

    /// <summary>
    /// Creating and working talent radar entries.
    /// </summary>
    /// <remarks>
    /// Held apart because the radar concerns people who do not know they are being
    /// discussed, and because it is the doorway into M4: converting an entry
    /// creates a real prospect.
    /// </remarks>
    public const string IntelligenceRadarWrite = "intelligence.radar.write";

    /// <summary>All permissions known to this build.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        OrganizationsRead,
        OrganizationsCreate,
        OrganizationsArchive,
        MembershipsRead,
        MembershipsGrant,
        MembershipsRevoke,
        AuditRead,
        ReleasePolicyRead,
        ReleasePolicyManage,
        PeopleRead,
        PeopleWrite,
        CompaniesRead,
        CompaniesWrite,
        RelationshipsRead,
        RelationshipsWrite,
        InteractionsRead,
        InteractionsRecord,
        TasksRead,
        TasksWrite,
        TalentRead,
        TalentWrite,
        TalentNotesRead,
        RepresentationRead,
        RepresentationWrite,
        ProspectsRead,
        ProspectsWrite,
        ProjectsRead,
        ProjectsWrite,
        PackagesRead,
        PackagesWrite,
        PackageStrategyRead,
        OpportunitiesRead,
        OpportunitiesWrite,
        SubmissionsRead,
        SubmissionsWrite,
        OpportunityStrategyRead,
        DealsRead,
        DealsWrite,
        OffersRead,
        OffersWrite,
        DealEconomicsRead,
        DealStrategyRead,
        ContractsRead,
        ContractsWrite,
        ContractTermsRead,
        ContractPrivilegedRead,
        RightsRead,
        RightsWrite,
        ObligationsRead,
        ObligationsWrite,
        FinanceRead,
        FinanceWrite,
        FinancePaymentsRead,
        FinancePaymentsWrite,
        FinanceCommissionsRead,
        FinanceCommissionsWrite,
        FinanceLedgerRead,
        FinanceLedgerPost,
        FinanceAdjustmentsWrite,
        DocumentsRead,
        DocumentsWrite,
        DocumentsPrivilegedRead,
        DocumentsRestrictedRead,
        DocumentsLink,
        CommunicationsRead,
        CommunicationsSharedRead,
        CommunicationsSend,
        CommunicationsAccountManage,
        IntelligenceRead,
        IntelligenceWrite,
        IntelligenceSensitiveRead,
        IntelligencePredictionsWrite,
        IntelligenceRadarWrite,
    };
}
