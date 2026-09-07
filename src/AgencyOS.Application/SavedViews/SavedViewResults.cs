using AgencyOS.Application.Directory;
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
public sealed record SavedViewResultModel(
    SavedViewTarget Target,
    IReadOnlyList<PersonSummaryModel> People,
    IReadOnlyList<CompanySummaryModel> Companies,
    IReadOnlyList<TaskModel> Tasks,
    IReadOnlyList<TalentSummaryModel> Talent,
    IReadOnlyList<ProspectModel> Prospects)
{
    public static SavedViewResultModel Empty(SavedViewTarget target) => new(target, [], [], [], [], []);

    /// <summary>Gets how many rows the view returned, whatever its target.</summary>
    public int Count => People.Count + Companies.Count + Tasks.Count + Talent.Count + Prospects.Count;
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
