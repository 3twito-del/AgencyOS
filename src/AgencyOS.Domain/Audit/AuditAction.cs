namespace AgencyOS.Domain.Audit;

/// <summary>
/// Action names recorded in the audit trail.
/// </summary>
/// <remarks>
/// Strings rather than an enum, for the same reason as <c>Permission</c>: audit
/// rows outlive the build that wrote them, and a stored value must stay readable
/// after an enum is renumbered or a member is removed.
/// </remarks>
public static class AuditAction
{
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationArchived = "organization.archived";

    public const string MembershipGranted = "membership.granted";
    public const string MembershipRevoked = "membership.revoked";

    public const string UserRegistered = "user.registered";

    /// <summary>First-run initialization. Occurs at most once in a system's life.</summary>
    public const string SystemBootstrapped = "system.bootstrapped";

    /// <summary>A release policy was published for a platform and ring.</summary>
    public const string ReleasePolicyPublished = "release.policy.published";

    // ---- People vertical slice (M2) ----

    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonArchived = "person.archived";

    public const string CompanyCreated = "company.created";
    public const string CompanyUpdated = "company.updated";
    public const string CompanyArchived = "company.archived";

    public const string RelationshipCreated = "relationship.created";
    public const string RelationshipEnded = "relationship.ended";

    public const string InteractionRecorded = "interaction.recorded";

    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string TaskReopened = "task.reopened";

    // ---- Saved views (M3) ----
    //
    // A saved view holds no business fact, but it is stored user data and
    // deleting it is destructive, so the lifecycle is audited like any other
    // privileged operation.

    // ---- Talent and representation (M4) ----

    public const string TalentProfileCreated = "talentprofile.created";
    public const string TalentProfileUpdated = "talentprofile.updated";
    public const string TalentDisciplineAdded = "talentprofile.discipline.added";
    public const string TalentDisciplineRemoved = "talentprofile.discipline.removed";

    public const string ProspectCreated = "prospect.created";
    public const string ProspectUpdated = "prospect.updated";
    public const string ProspectStageChanged = "prospect.stage.changed";
    public const string ProspectConverted = "prospect.converted";

    public const string RepresentationCreated = "representation.created";
    public const string RepresentationUpdated = "representation.updated";
    public const string RepresentationStatusChanged = "representation.status.changed";
    public const string RepresentationScopeAdded = "representation.scope.added";
    public const string RepresentationScopeEnded = "representation.scope.ended";
    public const string RepresentationTeamAssigned = "representation.team.assigned";
    public const string RepresentationTeamRemoved = "representation.team.removed";

    public const string CreditAdded = "credit.added";
    public const string CreditUpdated = "credit.updated";

    public const string MaterialAdded = "material.added";
    public const string MaterialUpdated = "material.updated";

    public const string SavedViewCreated = "savedview.created";
    public const string SavedViewUpdated = "savedview.updated";
    public const string SavedViewDeleted = "savedview.deleted";
}
