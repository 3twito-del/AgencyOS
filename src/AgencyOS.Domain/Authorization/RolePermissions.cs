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
        //
        // No finance grant appears anywhere below. An observer sees no receivable,
        // no payment, no commission and no ledger entry - not redacted versions of
        // them, none of them. Read access to money is a decision somebody makes
        // deliberately, not a consequence of being able to see the business
        // (ADR-0023).
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
        Permission.ObligationsWrite,

        // Finance is operational work, so a member does it. Two grants are
        // deliberately absent: finance.ledger.post, which covers posting a
        // hand-written journal entry, and nothing else - the postings that follow
        // from a receivable, a payment or an allocation are consequences of acts
        // already authorized above, and are made by the system inside the same
        // transaction (ADR-0023).
        Permission.FinanceRead,
        Permission.FinanceWrite,
        Permission.FinancePaymentsRead,
        Permission.FinancePaymentsWrite,
        Permission.FinanceCommissionsRead,
        Permission.FinanceCommissionsWrite,
        Permission.FinanceLedgerRead,
        Permission.FinanceAdjustmentsWrite,

        // Documents and communications are operational work, so a member does
        // both. Three grants are deliberately absent: the two elevated document
        // classifications, which are given to the people who need that material
        // rather than to everybody who can open a file, and shared mailbox
        // reading, because a member's own mailbox is not an argument for reading
        // anybody else's (ADR-0025, ADR-0026).
        Permission.DocumentsRead,
        Permission.DocumentsWrite,
        Permission.DocumentsLink,
        Permission.CommunicationsRead,
        Permission.CommunicationsSend,
        Permission.CommunicationsAccountManage,

        // Intelligence is the day-to-day work of an agent: noticing things,
        // writing down what they mean, and watching people. Two grants are
        // deliberately absent. The elevated classifications go to the people who
        // need that material rather than to everybody who can record a signal, and
        // forecasting is its own grant because a probability carries the
        // forecaster's name into the agency's calibration record (ADR-0030).
        Permission.IntelligenceRead,
        Permission.IntelligenceWrite,
        Permission.IntelligenceRadarWrite,

        // AI is ordinary working equipment for an agent: ask for a brief, let it
        // draft, let it propose. Three grants are deliberately absent. Reasoning
        // over elevated classifications is its own decision because reading a
        // confidence and exporting it are different acts; approving is held apart
        // so a proposal and its acceptance can be two people; and configuring what
        // leaves the building is not a day-to-day grant (ADR-0031).
        Permission.AiUse,
        Permission.AiPropose,
        Permission.AiApprove);

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
        Permission.ProspectsWrite,

        // Oversight rather than operation: an administrator reads the books and
        // holds the one grant that lets a person post to them by hand. Posting a
        // journal entry nothing else produced is the narrowest and most dangerous
        // act in the system, so it lives here rather than with the day-to-day
        // finance work (ADR-0023).
        Permission.FinanceRead,
        Permission.FinancePaymentsRead,
        Permission.FinanceCommissionsRead,
        Permission.FinanceLedgerRead,
        Permission.FinanceLedgerPost,

        // An administrator reads documents and communications and writes neither.
        // The pattern the role has followed since M1: oversight without the
        // ability to author the records being overseen.
        Permission.DocumentsRead,
        Permission.DocumentsPrivilegedRead,
        Permission.DocumentsRestrictedRead,
        Permission.CommunicationsRead,
        Permission.CommunicationsSharedRead,

        // Oversight again: an administrator reads the agency's intelligence,
        // including the sensitive classifications, and writes none of it.
        Permission.IntelligenceRead,
        Permission.IntelligenceSensitiveRead,

        // Oversight again, and the AI configuration grant lives here rather than
        // with the people who use AI: deciding what an organization transmits to
        // an outside provider is an administrative act, not an agent's.
        Permission.AiUse,
        Permission.AiAdminister);

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
        Permission.ProspectsWrite,
        Permission.FinanceRead,
        Permission.FinancePaymentsRead,
        Permission.FinanceCommissionsRead,
        Permission.FinanceLedgerRead,
        Permission.FinanceLedgerPost,
        Permission.FinanceAdjustmentsWrite,
        Permission.DocumentsRead,
        Permission.DocumentsPrivilegedRead,
        Permission.DocumentsRestrictedRead,

        // The owner writes documents where the administrator does not, and the
        // reason is a rule the milestone enforces elsewhere: a writer may not file
        // a document into a classification they could not then read. Without these
        // two grants no role in the system could record privileged material at all
        // - members write but cannot read privileged, administrators read but
        // cannot write - and counsel's advice would have nowhere to go
        // (ADR-0025).
        Permission.DocumentsWrite,
        Permission.DocumentsLink,

        Permission.CommunicationsRead,
        Permission.CommunicationsSharedRead,

        // The owner both reads and writes intelligence, for the same reason they
        // gained the document write grants in M10: somebody has to be able to
        // record a thesis at a classification only they can read, and a role that
        // could read it but not write it would make the classification
        // unreachable (ADR-0025, ADR-0030).
        Permission.IntelligenceRead,
        Permission.IntelligenceWrite,
        Permission.IntelligenceSensitiveRead,
        Permission.IntelligencePredictionsWrite,
        Permission.IntelligenceRadarWrite,

        // All five, for the reason the intelligence block gives: a role that could
        // not reach an elevated classification through AI while being able to read
        // it directly would make the sensitive path unreachable in a fresh tenant,
        // and somebody has to be able to configure the provider policy at all.
        Permission.AiUse,
        Permission.AiSensitiveUse,
        Permission.AiPropose,
        Permission.AiApprove,
        Permission.AiAdminister);

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
