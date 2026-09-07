namespace AgencyOS.Contracts.SavedViews;

/// <summary>
/// The filters a saved view applies.
/// </summary>
/// <remarks>
/// A closed set of optional predicates, not an expression language. Everything
/// here maps to a specific reviewed clause in the query layer, so a saved view can
/// never become an arbitrary query.
/// </remarks>
/// <param name="Status">Person or company lifecycle status.</param>
/// <param name="CompanyId">Restricts people to one primary company.</param>
/// <param name="TitleContains">Substring match on a person's title.</param>
/// <param name="TextContains">Substring match on the primary name field.</param>
/// <param name="TaskState">Open or Completed.</param>
/// <param name="DueWithinDays">Tasks due within this many days.</param>
/// <param name="OverdueOnly">Only tasks past their due date.</param>
public sealed record SavedViewFiltersModel(
    string? Status = null,
    Guid? CompanyId = null,
    string? TitleContains = null,
    string? TextContains = null,
    string? TaskState = null,
    int? DueWithinDays = null,
    bool OverdueOnly = false);

/// <param name="Field">Field to order by. Must be sortable for the target.</param>
/// <param name="Direction">Ascending or Descending.</param>
public sealed record SavedViewSortModel(string Field, string Direction);

/// <summary>
/// A saved view's query, as a versioned document.
/// </summary>
/// <param name="DefinitionVersion">
/// Schema version of this document. A version the server does not understand is
/// rejected rather than guessed at, so filter semantics can evolve without
/// silently reinterpreting views saved months earlier.
/// </param>
/// <param name="Target">People, Companies or Tasks.</param>
/// <param name="Filters">Predicates to apply.</param>
/// <param name="Sort">Ordering, or null for the target's default.</param>
public sealed record SavedViewDefinitionModel(
    int DefinitionVersion,
    string Target,
    SavedViewFiltersModel Filters,
    SavedViewSortModel? Sort = null);

/// <param name="Name">What the user calls this view.</param>
/// <param name="Definition">The query document.</param>
public sealed record CreateSavedViewRequest(string Name, SavedViewDefinitionModel Definition);

/// <param name="Name">Replacement name.</param>
/// <param name="Definition">Replacement query document.</param>
/// <param name="ExpectedVersion">Version the caller observed. Required.</param>
public sealed record UpdateSavedViewRequest(
    string Name,
    SavedViewDefinitionModel Definition,
    int ExpectedVersion);

/// <param name="Id">Saved view identifier.</param>
/// <param name="Name">What the user calls it.</param>
/// <param name="Target">What it lists.</param>
/// <param name="Definition">The query document.</param>
/// <param name="DefinitionVersion">Schema version of the document.</param>
/// <param name="Version">Optimistic concurrency token.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
public sealed record SavedViewResponse(
    Guid Id,
    string Name,
    string Target,
    SavedViewDefinitionModel Definition,
    int DefinitionVersion,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
