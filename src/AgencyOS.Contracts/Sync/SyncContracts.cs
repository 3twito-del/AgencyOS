using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Contracts.Sync;

/// <summary>
/// One entry in a tenant's change feed.
/// </summary>
/// <remarks>
/// The feed carries identity: it says what moved. The current state of everything
/// a page names travels in the same response, read in the same request, so the
/// feed and the records it describes cannot disagree.
/// </remarks>
/// <param name="Sequence">Position in the tenant's feed. Strictly increasing in commit order.</param>
/// <param name="EntityType">Person, Company, ProfessionalRelationship, Interaction or TaskItem.</param>
/// <param name="EntityId">Identifier of the changed record.</param>
/// <param name="Kind"><c>Upsert</c> or <c>Removed</c>.</param>
/// <param name="OccurredAt">When the change was committed, UTC.</param>
public sealed record ChangeEntryResponse(
    long Sequence,
    string EntityType,
    Guid EntityId,
    string Kind,
    DateTimeOffset OccurredAt);

/// <summary>
/// A page of changes plus the current state of everything in it.
/// </summary>
/// <remarks>
/// <para>
/// The client advances its cursor only after durably applying a page, so an
/// interrupted sync resumes rather than skips.
/// </para>
/// <para>
/// <see cref="Cursor"/> is the position to ask from next. It is per tenant: a
/// client working in two tenants keeps two cursors.
/// </para>
/// </remarks>
/// <param name="Cursor">Position to resume from.</param>
/// <param name="HasMore">Whether more changes are waiting beyond this page.</param>
/// <param name="Changes">The change entries in this page, in sequence order.</param>
/// <param name="People">Current state of people named in this page.</param>
/// <param name="Companies">Current state of companies named in this page.</param>
/// <param name="Tasks">Current state of tasks named in this page.</param>
/// <param name="Talent">
/// Current state of talent named in this page. A talent entry is keyed by person,
/// because the cached summary denormalizes representation status and a
/// representation change has to refresh it too.
/// </param>
public sealed record SyncChangesResponse(
    long Cursor,
    bool HasMore,
    IReadOnlyList<ChangeEntryResponse> Changes,
    IReadOnlyList<PersonSummaryResponse> People,
    IReadOnlyList<CompanySummaryResponse> Companies,
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<TalentSummaryResponse> Talent);
