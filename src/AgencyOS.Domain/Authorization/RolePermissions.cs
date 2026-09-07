namespace AgencyOS.Domain.Authorization;

/// <summary>
/// Maps a role to the permissions it confers.
/// </summary>
/// <remarks>
/// A single in-domain table, so the grant model is reviewable in one place rather
/// than scattered across endpoint declarations. Each role is written out in full
/// rather than inheriting from the role below it: a widening should be visible in
/// the diff, not implied by a chain.
/// </remarks>
public static class RolePermissions
{
    /// <summary>Read-only access to the working record set.</summary>
    private static readonly IReadOnlySet<string> ObserverPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.MembershipsRead,
        Permission.ReleasePolicyRead,
        Permission.PeopleRead,
        Permission.CompaniesRead,
        Permission.RelationshipsRead,
        Permission.InteractionsRead,
        Permission.TasksRead,

        // An observer sees who the agency represents, but not what it privately
        // thinks about them: talent.notes.read is deliberately absent.
        Permission.TalentRead,
        Permission.RepresentationRead,
        Permission.ProspectsRead,

        // An observer sees the slate and what is being packaged, but not the
        // agency's private strategy: packages.strategy.read is deliberately absent.
        Permission.ProjectsRead,
        Permission.PackagesRead,

        // An observer sees the pipeline and what has gone out, but not the agency's
        // private strategy: opportunities.strategy.read is deliberately absent.
        Permission.OpportunitiesRead,
        Permission.SubmissionsRead,

        // An observer sees that a negotiation exists and how it stands, and its
        // structural terms - dates, episode counts, billing. Neither
        // deals.economics.read nor deals.strategy.read is granted, so what a deal
        // pays and what the agency privately intends stay out of reach.
        Permission.DealsRead,
        Permission.OffersRead,

        // An observer sees that a contract exists and where it stands, its rights
        // and its obligations. Neither contracts.terms.read nor
        // contracts.privileged.read is granted, so the clauses themselves and
        // anything counsel wrote stay out of reach.
        Permission.ContractsRead,
        Permission.RightsRead,
        Permission.ObligationsRead);

    /// <summary>
    /// The day-to-day operational role: everything needed to run the M2 workflow,
    /// and nothing that changes who can do what.
    /// </summary>
    private static readonly IReadOnlySet<string> MemberPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.MembershipsRead,
        Permission.ReleasePolicyRead,
        Permission.PeopleRead,
        Permission.PeopleWrite,
        Permission.CompaniesRead,
        Permission.CompaniesWrite,
        Permission.RelationshipsRead,
        Permission.RelationshipsWrite,
        Permission.InteractionsRead,
        Permission.InteractionsRecord,
        Permission.TasksRead,
        Permission.TasksWrite,
        Permission.TalentRead,
        Permission.TalentWrite,
        Permission.TalentNotesRead,
        Permission.RepresentationRead,
        Permission.RepresentationWrite,
        Permission.ProspectsRead,
        Permission.ProspectsWrite,
        Permission.ProjectsRead,
        Permission.ProjectsWrite,
        Permission.PackagesRead,
        Permission.PackagesWrite,
        Permission.PackageStrategyRead,
        Permission.OpportunitiesRead,
        Permission.OpportunitiesWrite,
        Permission.SubmissionsRead,
        Permission.SubmissionsWrite,
        Permission.OpportunityStrategyRead,
        Permission.DealsRead,
        Permission.DealsWrite,
        Permission.OffersRead,
        Permission.OffersWrite,
        Permission.DealEconomicsRead,
        Permission.DealStrategyRead,
        Permission.ContractsRead,
        Permission.ContractsWrite,
        Permission.ContractTermsRead,
        Permission.ContractPrivilegedRead,
        Permission.RightsRead,
        Permission.RightsWrite,
        Permission.ObligationsRead,
        Permission.ObligationsWrite);

    private static readonly IReadOnlySet<string> AdministratorPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.OrganizationsCreate,
        Permission.MembershipsRead,
        Permission.MembershipsGrant,
        Permission.MembershipsRevoke,
        Permission.AuditRead,
        Permission.ReleasePolicyRead,
        Permission.PeopleRead,
        Permission.PeopleWrite,
        Permission.CompaniesRead,
        Permission.CompaniesWrite,
        Permission.RelationshipsRead,
        Permission.RelationshipsWrite,
        Permission.InteractionsRead,
        Permission.InteractionsRecord,
        Permission.TasksRead,
        Permission.TasksWrite,
        Permission.TalentRead,
        Permission.TalentWrite,
        Permission.TalentNotesRead,
        Permission.RepresentationRead,
        Permission.RepresentationWrite,
        Permission.ProspectsRead,
        Permission.ProspectsWrite);

    private static readonly IReadOnlySet<string> OwnerPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.OrganizationsCreate,
        Permission.OrganizationsArchive,
        Permission.MembershipsRead,
        Permission.MembershipsGrant,
        Permission.MembershipsRevoke,
        Permission.AuditRead,
        Permission.ReleasePolicyRead,
        Permission.ReleasePolicyManage,
        Permission.PeopleRead,
        Permission.PeopleWrite,
        Permission.CompaniesRead,
        Permission.CompaniesWrite,
        Permission.RelationshipsRead,
        Permission.RelationshipsWrite,
        Permission.InteractionsRead,
        Permission.InteractionsRecord,
        Permission.TasksRead,
        Permission.TasksWrite,
        Permission.TalentRead,
        Permission.TalentWrite,
        Permission.TalentNotesRead,
        Permission.RepresentationRead,
        Permission.RepresentationWrite,
        Permission.ProspectsRead,
        Permission.ProspectsWrite);

    private static readonly IReadOnlySet<string> NoPermissions = Freeze();

    /// <summary>Gets the permissions conferred by <paramref name="role"/>.</summary>
    /// <remarks>
    /// An unrecognized role confers nothing. Failing closed matters here: a role
    /// value arriving from a newer schema must not be read as unrestricted.
    /// </remarks>
    public static IReadOnlySet<string> For(AgencyRole role) => role switch
    {
        AgencyRole.Observer => ObserverPermissions,
        AgencyRole.Member => MemberPermissions,
        AgencyRole.Administrator => AdministratorPermissions,
        AgencyRole.Owner => OwnerPermissions,
        _ => NoPermissions,
    };

    private static IReadOnlySet<string> Freeze(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
