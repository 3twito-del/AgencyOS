using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.SavedViews;

/// <summary>What a saved view lists.</summary>
public enum SavedViewTarget
{
    People = 1,
    Companies = 2,
    Tasks = 3,

    // Added in definition version 2. Talent and prospects are the lists an agency
    // actually works from: "my active clients in television", "prospects I owe a
    // follow-up".
    Talent = 4,
    Prospects = 5,
}

/// <summary>Sort direction for a saved view.</summary>
public enum SavedViewSortDirection
{
    Ascending = 1,
    Descending = 2,
}

/// <summary>
/// How a saved view orders its results.
/// </summary>
/// <param name="Field">Field name, drawn from the allow-list for the target.</param>
/// <param name="Direction">Ascending or descending.</param>
public sealed record SavedViewSort(string Field, SavedViewSortDirection Direction);

/// <summary>
/// The filters a saved view applies.
/// </summary>
/// <remarks>
/// A closed set of typed, optional predicates. Deliberately not an expression
/// language: a saved view is data the user composed, and data that can express
/// arbitrary queries is data that can express arbitrary damage. Every field here
/// maps to a specific, reviewed clause in the query layer.
/// </remarks>
/// <param name="Status">Person or company lifecycle status.</param>
/// <param name="CompanyId">Restricts people to one primary company.</param>
/// <param name="TitleContains">Substring match on a person's title.</param>
/// <param name="TextContains">Substring match on the primary name field.</param>
/// <param name="TaskState">Open or Completed.</param>
/// <param name="DueWithinDays">Tasks due within this many days.</param>
/// <param name="OverdueOnly">Only tasks past their due date.</param>
/// <param name="Discipline">Restricts talent to one professional discipline.</param>
/// <param name="ScopeArea">Restricts talent to one represented area.</param>
/// <param name="LeadUserId">Restricts talent to one internal owner.</param>
/// <param name="ClientsOnly">Only people the agency currently represents.</param>
/// <param name="FormerClientsOnly">Only people the agency used to represent.</param>
/// <param name="ProspectStage">Restricts prospects to one stage.</param>
/// <param name="OwnerUserId">Restricts prospects to one internal owner.</param>
/// <param name="FollowUpWithinDays">Only prospects needing attention within this many days.</param>
public sealed record SavedViewFilters(
    string? Status = null,
    Guid? CompanyId = null,
    string? TitleContains = null,
    string? TextContains = null,
    string? TaskState = null,
    int? DueWithinDays = null,
    bool OverdueOnly = false,
    string? Discipline = null,
    string? ScopeArea = null,
    Guid? LeadUserId = null,
    bool ClientsOnly = false,
    bool FormerClientsOnly = false,
    string? ProspectStage = null,
    Guid? OwnerUserId = null,
    int? FollowUpWithinDays = null);

/// <summary>
/// A saved view's query, as a versioned, validated document.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DefinitionVersion"/> exists so filter semantics can evolve without
/// silently reinterpreting views a user saved months earlier. A version this
/// build does not understand is rejected rather than guessed at.
/// </para>
/// <para>
/// The definition is stored as JSON for readability and evolution, but it is
/// parsed into this type before it reaches the query layer. Nothing derived from
/// user input ever reaches SQL as text.
/// </para>
/// </remarks>
/// <param name="DefinitionVersion">Schema version of this document.</param>
/// <param name="Target">What the view lists.</param>
/// <param name="Filters">Predicates to apply.</param>
/// <param name="Sort">Ordering, or null for the target's default.</param>
public sealed record SavedViewDefinition(
    int DefinitionVersion,
    SavedViewTarget Target,
    SavedViewFilters Filters,
    SavedViewSort? Sort = null)
{
    /// <summary>The definition schema version this build writes and understands.</summary>
    /// <summary>Definition schema this build writes.</summary>
    public const int CurrentDefinitionVersion = 2;

    /// <summary>
    /// The oldest definition schema this build still understands.
    /// </summary>
    /// <remarks>
    /// Version 1 is read and upgraded rather than refused. Version 2 only adds
    /// targets and filters, so a version 1 document means exactly what it always
    /// meant; refusing it would have broken every view saved before M4 for no
    /// reason. A version this build genuinely does not understand is still
    /// rejected, which is the point of versioning the document at all.
    /// </remarks>
    public const int MinimumUnderstoodVersion = 1;

    /// <summary>Fields a view may sort by, per target.</summary>
    /// <remarks>
    /// An allow-list rather than a validation rule, because the value reaches an
    /// ordering clause. Anything not named here is refused.
    /// </remarks>
    public static IReadOnlyDictionary<SavedViewTarget, IReadOnlySet<string>> SortableFields { get; } =
        new Dictionary<SavedViewTarget, IReadOnlySet<string>>
        {
            [SavedViewTarget.People] = Freeze("DisplayName", "UpdatedAt", "Title"),
            [SavedViewTarget.Companies] = Freeze("Name", "UpdatedAt", "Type"),
            [SavedViewTarget.Tasks] = Freeze("DueAt", "Priority", "CreatedAt", "Title"),
            [SavedViewTarget.Talent] = Freeze("DisplayName", "UpdatedAt", "CareerStage"),
            [SavedViewTarget.Prospects] = Freeze("NextFollowUpOn", "IdentifiedOn", "Stage", "DisplayName"),
        };

    /// <summary>Validates the document, failing with a message that says what is wrong.</summary>
    public void Validate()
    {
        if (DefinitionVersion is < MinimumUnderstoodVersion or > CurrentDefinitionVersion)
        {
            throw new DomainException(
                $"Saved view definition version {DefinitionVersion} is not supported by this build "
                    + $"(it understands versions {MinimumUnderstoodVersion} to {CurrentDefinitionVersion}).");
        }

        if (!Enum.IsDefined(Target))
        {
            throw new DomainException($"Unknown saved view target '{Target}'.");
        }

        ArgumentNullException.ThrowIfNull(Filters);

        ValidateFiltersForTarget();

        if (Sort is { } sort)
        {
            if (!Enum.IsDefined(sort.Direction))
            {
                throw new DomainException($"Unknown sort direction '{sort.Direction}'.");
            }

            if (!SortableFields[Target].Contains(sort.Field))
            {
                throw new DomainException(
                    $"'{sort.Field}' is not a sortable field for {Target}. "
                        + $"Expected one of: {string.Join(", ", SortableFields[Target])}.");
            }
        }

        if (Filters.DueWithinDays is { } days and (< 0 or > 3650))
        {
            throw new DomainException("DueWithinDays must be between 0 and 3650.");
        }

        if (Filters.FollowUpWithinDays is { } followUp and (< 0 or > 3650))
        {
            throw new DomainException("FollowUpWithinDays must be between 0 and 3650.");
        }
    }

    /// <summary>
    /// Refuses filters that mean nothing for the chosen target.
    /// </summary>
    /// <remarks>
    /// A task filter on a people view is almost certainly a mistake, and silently
    /// ignoring it would give the user a view that does not do what they asked.
    /// </remarks>
    private void ValidateFiltersForTarget()
    {
        switch (Target)
        {
            case SavedViewTarget.People:
                Reject(Filters.TaskState is not null, "TaskState", Target);
                Reject(Filters.OverdueOnly, "OverdueOnly", Target);
                Reject(Filters.DueWithinDays is not null, "DueWithinDays", Target);
                RejectRepresentationFilters();
                RejectProspectFilters();
                break;

            case SavedViewTarget.Companies:
                Reject(Filters.TaskState is not null, "TaskState", Target);
                Reject(Filters.OverdueOnly, "OverdueOnly", Target);
                Reject(Filters.DueWithinDays is not null, "DueWithinDays", Target);
                Reject(Filters.TitleContains is not null, "TitleContains", Target);
                Reject(Filters.CompanyId is not null, "CompanyId", Target);
                RejectRepresentationFilters();
                RejectProspectFilters();
                break;

            case SavedViewTarget.Tasks:
                Reject(Filters.TitleContains is not null, "TitleContains", Target);
                RejectRepresentationFilters();
                RejectProspectFilters();
                break;

            case SavedViewTarget.Talent:
                Reject(Filters.TaskState is not null, "TaskState", Target);
                Reject(Filters.OverdueOnly, "OverdueOnly", Target);
                Reject(Filters.DueWithinDays is not null, "DueWithinDays", Target);
                RejectProspectFilters();

                // Current and former are contradictory: a view asking for both
                // returns nothing, which reads as a broken view rather than an
                // impossible question.
                Reject(
                    Filters.ClientsOnly && Filters.FormerClientsOnly,
                    "ClientsOnly with FormerClientsOnly",
                    Target);
                break;

            case SavedViewTarget.Prospects:
                Reject(Filters.TaskState is not null, "TaskState", Target);
                Reject(Filters.OverdueOnly, "OverdueOnly", Target);
                Reject(Filters.DueWithinDays is not null, "DueWithinDays", Target);
                Reject(Filters.TitleContains is not null, "TitleContains", Target);
                RejectRepresentationFilters();
                break;

            default:
                break;
        }
    }

    /// <summary>Refuses representation filters on a target that has no representation.</summary>
    private void RejectRepresentationFilters()
    {
        Reject(Filters.Discipline is not null, "Discipline", Target);
        Reject(Filters.ScopeArea is not null, "ScopeArea", Target);
        Reject(Filters.LeadUserId is not null, "LeadUserId", Target);
        Reject(Filters.ClientsOnly, "ClientsOnly", Target);
        Reject(Filters.FormerClientsOnly, "FormerClientsOnly", Target);
    }

    /// <summary>Refuses prospect filters on a target that is not a pursuit.</summary>
    private void RejectProspectFilters()
    {
        Reject(Filters.ProspectStage is not null, "ProspectStage", Target);
        Reject(Filters.OwnerUserId is not null, "OwnerUserId", Target);
        Reject(Filters.FollowUpWithinDays is not null, "FollowUpWithinDays", Target);
    }

    private static void Reject(bool present, string filter, SavedViewTarget target)
    {
        if (present)
        {
            throw new DomainException($"Filter '{filter}' does not apply to a {target} view.");
        }
    }

    private static IReadOnlySet<string> Freeze(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
