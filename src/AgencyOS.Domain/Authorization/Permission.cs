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
    };
}
