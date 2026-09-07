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
    };
}
