using System.Collections.Generic;

namespace AgencyOS.Domain.Authorization;

/// <summary>
/// What each permission lets somebody do, in words an operator uses.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-014</c>. A refusal used to read <c>"Permission
/// 'finance.payments.read' is required."</c> — precise, and written for whoever
/// reads the log. It named an internal identifier, did not say what had been
/// attempted, and left the operator knowing only that they had been refused.
/// </para>
/// <para>
/// The identifier has not gone anywhere: it stays on the exception and reaches
/// the caller as the problem's <c>requiredPermission</c> extension, so anything
/// reading this by machine is unaffected. This is the same split
/// <c>AOS-R002-007</c> made for the conflict sentence — human prose in
/// <c>detail</c>, exact data in an extension.
/// </para>
/// <para>
/// A plain table rather than anything derived from the identifier. Deriving would
/// put the identifier's own words into the sentence — "Posting finance ledger" —
/// which is neither what an operator calls it nor what this finding asked for.
/// <c>PermissionCapabilityTests</c> requires an entry for every member of
/// <see cref="Permission.All"/>, so a new permission cannot quietly fall back.
/// </para>
/// </remarks>
public static class PermissionCapability
{
    /// <summary>
    /// Said when the permission is not one this build knows.
    /// </summary>
    /// <remarks>
    /// Deliberately says nothing about which permission it was. A build talking to
    /// a newer server can be refused a permission it has never heard of, and
    /// falling back to the identifier is the exact behaviour being repaired.
    /// </remarks>
    private const string Unknown = "That";

    /// <summary>The capability each permission carries, as a sentence opener.</summary>
    private static readonly IReadOnlyDictionary<string, string> Capabilities =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            // Tenant administration.
            ["organizations.read"] = "Reading this organization",
            ["organizations.create"] = "Creating an organization",
            ["organizations.archive"] = "Archiving an organization",
            ["memberships.read"] = "Seeing who is in this organization",
            ["memberships.grant"] = "Adding somebody to this organization",
            ["memberships.revoke"] = "Removing somebody from this organization",
            ["audit.read"] = "Reading the audit trail",
            ["release.policy.read"] = "Reading the release policy",
            ["release.policy.manage"] = "Changing the release policy",

            // People, companies and what happens between them.
            ["people.read"] = "Reading people",
            ["people.write"] = "Changing people",
            ["companies.read"] = "Reading companies",
            ["companies.write"] = "Changing companies",
            ["relationships.read"] = "Reading relationships",
            ["relationships.write"] = "Changing relationships",
            ["interactions.read"] = "Reading interactions",
            ["interactions.record"] = "Recording an interaction",
            ["tasks.read"] = "Reading tasks",
            ["tasks.write"] = "Changing tasks",

            // Talent, prospects and representation.
            ["talent.read"] = "Reading talent",
            ["talent.write"] = "Changing talent",
            ["talent.notes.read"] = "Reading internal notes about talent",
            ["prospects.read"] = "Reading prospects",
            ["prospects.write"] = "Changing prospects",
            ["representation.read"] = "Reading representation",
            ["representation.write"] = "Changing representation",

            // Projects and packaging.
            ["projects.read"] = "Reading projects",
            ["projects.write"] = "Changing projects",
            ["packages.read"] = "Reading packages",
            ["packages.write"] = "Changing packages",
            ["packages.strategy.read"] = "Reading package strategy",

            // Opportunities and submissions.
            ["opportunities.read"] = "Reading opportunities",
            ["opportunities.write"] = "Changing opportunities",
            ["opportunities.strategy.read"] = "Reading opportunity strategy",
            ["submissions.read"] = "Reading submissions",
            ["submissions.write"] = "Recording submissions",

            // Deals and offers.
            ["deals.read"] = "Reading deals",
            ["deals.write"] = "Changing deals",
            ["deals.economics.read"] = "Reading deal economics",
            ["deals.strategy.read"] = "Reading deal strategy",
            ["offers.read"] = "Reading offers",
            ["offers.write"] = "Recording offers",

            // Contracts, rights and obligations.
            ["contracts.read"] = "Reading contracts",
            ["contracts.write"] = "Changing contracts",
            ["contracts.terms.read"] = "Reading contract terms",
            ["contracts.privileged.read"] = "Reading privileged contract material",
            ["rights.read"] = "Reading rights",
            ["rights.write"] = "Changing rights",
            ["obligations.read"] = "Reading obligations",
            ["obligations.write"] = "Changing obligations",

            // Finance.
            ["finance.read"] = "Reading finance",
            ["finance.write"] = "Changing finance",
            ["finance.payments.read"] = "Reading payments",
            ["finance.payments.write"] = "Recording payments",
            ["finance.commissions.read"] = "Reading commissions",
            ["finance.commissions.write"] = "Changing commissions",
            ["finance.ledger.read"] = "Reading the ledger",
            ["finance.ledger.post"] = "Posting to the ledger",
            ["finance.adjustments.write"] = "Adjusting a financial record",

            // Documents.
            ["documents.read"] = "Reading documents",
            ["documents.write"] = "Changing documents",
            ["documents.link"] = "Linking documents to records",
            ["documents.restricted.read"] = "Reading restricted documents",
            ["documents.privileged.read"] = "Reading privileged documents",

            // Communications.
            ["communications.read"] = "Reading communications",
            ["communications.send"] = "Sending communications",
            ["communications.shared.read"] = "Reading shared communications",
            ["communications.account.manage"] = "Managing communication accounts",

            // Intelligence.
            ["intelligence.read"] = "Reading intelligence",
            ["intelligence.write"] = "Changing intelligence",
            ["intelligence.sensitive.read"] = "Reading sensitive intelligence",
            ["intelligence.predictions.write"] = "Changing predictions",
            ["intelligence.radar.write"] = "Changing the radar",

            // AI.
            ["ai.use"] = "Using AI",
            ["ai.propose"] = "Asking AI to propose an action",
            ["ai.approve"] = "Approving an AI action",
            ["ai.administer"] = "Administering AI",
            ["ai.sensitive.use"] = "Using AI on sensitive material",
        };

    /// <summary>
    /// The sentence an operator reads when this permission refused them.
    /// </summary>
    /// <remarks>
    /// It says what was refused and stops there. It does not say the capability
    /// does not exist, and it does not say who could grant it: the product has
    /// never told a refused caller how this organization is administered, and
    /// saying so here would be a new disclosure rather than a kinder sentence.
    /// </remarks>
    public static string Describe(string? permission) =>
        (permission is not null && Capabilities.TryGetValue(permission, out string? capability)
            ? capability
            : Unknown)
        + " is not part of your role.";

    /// <summary>Whether this build has words for a permission.</summary>
    public static bool Knows(string permission) => Capabilities.ContainsKey(permission);
}
