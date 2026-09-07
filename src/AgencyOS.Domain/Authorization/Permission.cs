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
    };
}
