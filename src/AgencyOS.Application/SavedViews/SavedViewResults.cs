using AgencyOS.Application.Directory;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Finance;
using AgencyOS.Application.Legal;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.SavedViews;

namespace AgencyOS.Application.SavedViews;

/// <summary>
/// What running a saved view produced.
/// </summary>
/// <remarks>
/// One shape per target rather than a single untyped list, so the client renders
/// people as people and tasks as tasks without inspecting a discriminator.
/// </remarks>
/// <param name="Target">Which list this is.</param>
/// <param name="People">Matching people, when the target is People.</param>
/// <param name="Companies">Matching companies, when the target is Companies.</param>
/// <param name="Tasks">Matching tasks, when the target is Tasks.</param>
/// <param name="Talent">Matching talent, when the target is Talent.</param>
/// <param name="Prospects">Matching prospects, when the target is Prospects.</param>
/// <param name="Projects">Matching projects, when the target is Projects.</param>
/// <param name="Packages">Matching packages, when the target is Packages.</param>
/// <param name="Opportunities">Matching pursuits, when the target is Opportunities.</param>
/// <param name="Deals">Matching negotiations, when the target is Deals.</param>
/// <param name="Contracts">Matching contracts, when the target is Contracts.</param>
public sealed record SavedViewResultModel(
    SavedViewTarget Target,
    IReadOnlyList<PersonSummaryModel> People,
    IReadOnlyList<CompanySummaryModel> Companies,
    IReadOnlyList<TaskModel> Tasks,
    IReadOnlyList<TalentSummaryModel> Talent,
    IReadOnlyList<ProspectModel> Prospects,
    IReadOnlyList<ProjectSummaryModel> Projects,
    IReadOnlyList<PackageSummaryModel> Packages,
    IReadOnlyList<OpportunitySummaryModel> Opportunities,
    IReadOnlyList<DealSummaryModel> Deals,
    IReadOnlyList<ContractSummaryModel> Contracts,
    IReadOnlyList<ReceivableModel> Receivables,
    IReadOnlyList<InvoiceModel> Invoices,
    IReadOnlyList<PaymentModel> Payments)
{
    public static SavedViewResultModel Empty(SavedViewTarget target) =>
        new(target, [], [], [], [], [], [], [], [], [], [], [], [], []);

    // One factory per target, so a query names only the list it filled. Building
    // these positionally meant every target's call site had to grow by an empty
    // list each time a target was added - eight edits for one addition, each of
    // them a chance to put a list in the wrong slot.

    public static SavedViewResultModel OfPeople(IReadOnlyList<PersonSummaryModel> people) =>
        Empty(SavedViewTarget.People) with { People = people };

    public static SavedViewResultModel OfCompanies(IReadOnlyList<CompanySummaryModel> companies) =>
        Empty(SavedViewTarget.Companies) with { Companies = companies };

    public static SavedViewResultModel OfTasks(IReadOnlyList<TaskModel> tasks) =>
        Empty(SavedViewTarget.Tasks) with { Tasks = tasks };

    public static SavedViewResultModel OfTalent(IReadOnlyList<TalentSummaryModel> talent) =>
        Empty(SavedViewTarget.Talent) with { Talent = talent };

    public static SavedViewResultModel OfProspects(IReadOnlyList<ProspectModel> prospects) =>
        Empty(SavedViewTarget.Prospects) with { Prospects = prospects };

    public static SavedViewResultModel OfProjects(IReadOnlyList<ProjectSummaryModel> projects) =>
        Empty(SavedViewTarget.Projects) with { Projects = projects };

    public static SavedViewResultModel OfPackages(IReadOnlyList<PackageSummaryModel> packages) =>
        Empty(SavedViewTarget.Packages) with { Packages = packages };

    public static SavedViewResultModel OfOpportunities(
        IReadOnlyList<OpportunitySummaryModel> opportunities) =>
        Empty(SavedViewTarget.Opportunities) with { Opportunities = opportunities };

    public static SavedViewResultModel OfDeals(IReadOnlyList<DealSummaryModel> deals) =>
        Empty(SavedViewTarget.Deals) with { Deals = deals };

    /// <summary>A contract result set.</summary>
    /// <remarks>
    /// The summaries carry no drafted term and no privileged prose, so a saved view
    /// cannot become a second route to content its owner may not read. What a
    /// contract says is redacted where it is projected, not here (ADR-0022).
    /// </remarks>
    public static SavedViewResultModel OfContracts(
        IReadOnlyList<ContractSummaryModel> contracts) =>
        Empty(SavedViewTarget.Contracts) with { Contracts = contracts };

    /// <summary>A receivable result set.</summary>
    /// <remarks>
    /// Finance refuses rather than redacts, so a saved view either runs whole or
    /// not at all. There is no partially-visible balance here (ADR-0023).
    /// </remarks>
    public static SavedViewResultModel OfReceivables(
        IReadOnlyList<ReceivableModel> receivables) =>
        Empty(SavedViewTarget.Receivables) with { Receivables = receivables };

    /// <summary>An invoice result set.</summary>
    public static SavedViewResultModel OfInvoices(IReadOnlyList<InvoiceModel> invoices) =>
        Empty(SavedViewTarget.Invoices) with { Invoices = invoices };

    /// <summary>A payment result set.</summary>
    public static SavedViewResultModel OfPayments(IReadOnlyList<PaymentModel> payments) =>
        Empty(SavedViewTarget.Payments) with { Payments = payments };

    /// <summary>Gets how many rows the view returned, whatever its target.</summary>
    public int Count =>
        People.Count
        + Companies.Count
        + Tasks.Count
        + Talent.Count
        + Prospects.Count
        + Projects.Count
        + Packages.Count
        + Opportunities.Count
        + Deals.Count
        + Contracts.Count
        + Receivables.Count
        + Invoices.Count
        + Payments.Count;
}

/// <summary>
/// Runs a validated saved-view definition against the tenant's records.
/// </summary>
/// <remarks>
/// Takes the definition rather than the view, so the query layer never needs to
/// know about ownership or versioning. Deliberately unauthorized: the caller's
/// right to see these records is established by
/// <see cref="SavedViewService"/> before this is reached.
/// </remarks>
public interface ISavedViewResultQueries
{
    Task<SavedViewResultModel> RunAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);
}
