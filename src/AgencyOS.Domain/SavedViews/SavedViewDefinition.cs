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

    // Added in definition version 3. The slate is the other list an agency works
    // from daily: what am I developing, and what am I assembling.
    Projects = 6,
    Packages = 7,

    // Added in definition version 4. The pipeline is the list an agency lives in
    // once it has a slate: what is out, with whom, and what is overdue.
    Opportunities = 8,
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
/// <param name="ProjectType">Restricts projects to one kind of work.</param>
/// <param name="DevelopmentStage">Restricts projects to one development stage.</param>
/// <param name="ProjectStatus">Restricts projects to one operational status.</param>
/// <param name="AttachedPersonId">Only projects this person currently holds a role on.</param>
/// <param name="MissingRoleType">
/// Only projects with nobody currently holding a role of this type. The rule is
/// stated rather than implied: a project counts as missing a director when no
/// attachment to a directing role currently holds it, whether or not such a role
/// row exists at all.
/// </param>
/// <param name="PackageStatus">Restricts packages to one status.</param>
/// <param name="ProjectId">Restricts packages to one project.</param>
/// <param name="OpportunityKind">Restricts pursuits to one kind.</param>
/// <param name="OpportunityStatus">Restricts pursuits to one status.</param>
/// <param name="TalentProfileId">Only pursuits about this client.</param>
/// <param name="PackageId">Only pursuits about this package.</param>
/// <param name="TargetCompanyId">Only pursuits aimed at this company.</param>
/// <param name="TargetPersonId">Only pursuits aimed at this person.</param>
/// <param name="TargetStage">Only pursuits with a target at this stage.</param>
/// <param name="HasSubmission">Only pursuits something has gone out on.</param>
/// <param name="AwaitingResponse">
/// Only pursuits with a reply overdue. Derived from the submission's expected date
/// and the absence of anything recorded since; silence is never stored.
/// </param>
/// <param name="FollowUpDueWithinDays">Only pursuits with a target action due within this many days.</param>
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
    int? FollowUpWithinDays = null,
    string? ProjectType = null,
    string? DevelopmentStage = null,
    string? ProjectStatus = null,
    Guid? AttachedPersonId = null,
    string? MissingRoleType = null,
    string? PackageStatus = null,
    Guid? ProjectId = null,
    string? OpportunityKind = null,
    string? OpportunityStatus = null,
    Guid? TalentProfileId = null,
    Guid? PackageId = null,
    Guid? TargetCompanyId = null,
    Guid? TargetPersonId = null,
    string? TargetStage = null,
    bool HasSubmission = false,
    bool AwaitingResponse = false,
    int? FollowUpDueWithinDays = null);

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
    public const int CurrentDefinitionVersion = 4;

    /// <summary>
    /// The oldest definition schema this build still understands.
    /// </summary>
    /// <remarks>
    /// Versions 1 and 2 are read rather than refused. Each step so far has only
    /// added targets and filters, so an older document means exactly what it always
    /// meant; refusing one would have broken every view saved before the milestone
    /// that widened the schema, for no reason at all. A version this build genuinely
    /// does not understand is still rejected, which is the point of versioning the
    /// document.
    /// </remarks>
    public const int MinimumUnderstoodVersion = 1;

    /// <summary>
    /// The definition version each target first existed in.
    /// </summary>
    /// <remarks>
    /// A document claiming version 2 while naming a target that arrived in version
    /// 3 is internally inconsistent, and accepting it would make the version number
    /// describe nothing. Stated as data so adding a target forces the author to say
    /// when it appeared.
    /// </remarks>
    public static IReadOnlyDictionary<SavedViewTarget, int> TargetIntroducedIn { get; } =
        new Dictionary<SavedViewTarget, int>
        {
            [SavedViewTarget.People] = 1,
            [SavedViewTarget.Companies] = 1,
            [SavedViewTarget.Tasks] = 1,
            [SavedViewTarget.Talent] = 2,
            [SavedViewTarget.Prospects] = 2,
            [SavedViewTarget.Projects] = 3,
            [SavedViewTarget.Packages] = 3,
            [SavedViewTarget.Opportunities] = 4,
        };

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
            [SavedViewTarget.Projects] = Freeze("Title", "UpdatedAt", "Stage", "Status", "Year"),
            [SavedViewTarget.Packages] = Freeze("Name", "UpdatedAt", "Status"),
            [SavedViewTarget.Opportunities] =
                Freeze("Name", "UpdatedAt", "OpenedOn", "Status", "Priority"),
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

        if (TargetIntroducedIn[Target] > DefinitionVersion)
        {
            throw new DomainException(
                $"A {Target} view did not exist at definition version {DefinitionVersion}; "
                    + $"it arrived in version {TargetIntroducedIn[Target]}.");
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
    /// The filters each target understands. Anything else is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An accept-list rather than a set of reject-lists, and the inversion is
    /// deliberate. Rejecting per target meant every new filter group had to be
    /// refused explicitly by every existing target: eight cases to edit for one
    /// addition, growing with the product of targets and filters, and silently
    /// permissive if one was missed. Accepting a filter it should not have would
    /// return everything rather than what was asked for, which is the failure that
    /// looks like a working view.
    /// </para>
    /// <para>
    /// Stated this way, a filter nobody claims is refused everywhere by default,
    /// and a test asserts every filter is claimed by at least one target - so
    /// adding one and forgetting to wire it up fails loudly instead of quietly
    /// (ADR-0020).
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<SavedViewTarget, IReadOnlySet<string>> AcceptedFilters
    { get; } = new Dictionary<SavedViewTarget, IReadOnlySet<string>>
    {
        [SavedViewTarget.People] = FreezeFilters(
            nameof(SavedViewFilters.Status),
            nameof(SavedViewFilters.CompanyId),
            nameof(SavedViewFilters.TitleContains),
            nameof(SavedViewFilters.TextContains)),

        [SavedViewTarget.Companies] = FreezeFilters(
            nameof(SavedViewFilters.Status),
            nameof(SavedViewFilters.TextContains)),

        [SavedViewTarget.Tasks] = FreezeFilters(
            nameof(SavedViewFilters.Status),
            nameof(SavedViewFilters.CompanyId),
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.TaskState),
            nameof(SavedViewFilters.DueWithinDays),
            nameof(SavedViewFilters.OverdueOnly)),

        [SavedViewTarget.Talent] = FreezeFilters(
            nameof(SavedViewFilters.Status),
            nameof(SavedViewFilters.CompanyId),
            nameof(SavedViewFilters.TitleContains),
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.Discipline),
            nameof(SavedViewFilters.ScopeArea),
            nameof(SavedViewFilters.LeadUserId),
            nameof(SavedViewFilters.ClientsOnly),
            nameof(SavedViewFilters.FormerClientsOnly)),

        [SavedViewTarget.Prospects] = FreezeFilters(
            nameof(SavedViewFilters.Status),
            nameof(SavedViewFilters.CompanyId),
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.ProspectStage),
            nameof(SavedViewFilters.OwnerUserId),
            nameof(SavedViewFilters.FollowUpWithinDays)),

        [SavedViewTarget.Projects] = FreezeFilters(
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.LeadUserId),
            nameof(SavedViewFilters.ProjectType),
            nameof(SavedViewFilters.DevelopmentStage),
            nameof(SavedViewFilters.ProjectStatus),
            nameof(SavedViewFilters.AttachedPersonId),
            nameof(SavedViewFilters.MissingRoleType)),

        [SavedViewTarget.Packages] = FreezeFilters(
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.LeadUserId),
            nameof(SavedViewFilters.PackageStatus),
            nameof(SavedViewFilters.ProjectId)),

        [SavedViewTarget.Opportunities] = FreezeFilters(
            nameof(SavedViewFilters.TextContains),
            nameof(SavedViewFilters.OwnerUserId),
            nameof(SavedViewFilters.ProjectId),
            nameof(SavedViewFilters.OpportunityKind),
            nameof(SavedViewFilters.OpportunityStatus),
            nameof(SavedViewFilters.TalentProfileId),
            nameof(SavedViewFilters.PackageId),
            nameof(SavedViewFilters.TargetCompanyId),
            nameof(SavedViewFilters.TargetPersonId),
            nameof(SavedViewFilters.TargetStage),
            nameof(SavedViewFilters.HasSubmission),
            nameof(SavedViewFilters.AwaitingResponse),
            nameof(SavedViewFilters.FollowUpDueWithinDays)),
    };

    /// <summary>Every filter the document could carry, by name.</summary>
    /// <remarks>
    /// Read from the record itself, so the accept-lists above are checked against
    /// what actually exists rather than against a second hand-written list that
    /// could drift from it.
    /// </remarks>
    public static IReadOnlySet<string> AllFilterNames { get; } =
        new HashSet<string>(
            typeof(SavedViewFilters)
                .GetProperties()
                .Select(x => x.Name)
                .Where(x => x != "EqualityContract"),
            StringComparer.Ordinal);

    /// <summary>
    /// Refuses filters that mean nothing for the chosen target.
    /// </summary>
    /// <remarks>
    /// A task filter on a people view is almost certainly a mistake, and silently
    /// ignoring it would give the user a view that does not do what they asked.
    /// </remarks>
    private void ValidateFiltersForTarget()
    {
        IReadOnlySet<string> accepted = AcceptedFilters.TryGetValue(Target, out IReadOnlySet<string>? set)
            ? set
            : FreezeFilters();

        foreach (string name in SetFilterNames(Filters))
        {
            if (!accepted.Contains(name))
            {
                throw new DomainException($"Filter '{name}' does not apply to a {Target} view.");
            }
        }

        // Current and former are contradictory: a view asking for both returns
        // nothing, which reads as a broken view rather than an impossible question.
        if (Filters.ClientsOnly && Filters.FormerClientsOnly)
        {
            throw new DomainException(
                $"Filter 'ClientsOnly with FormerClientsOnly' does not apply to a {Target} view.");
        }
    }

    /// <summary>
    /// The filters this document actually sets.
    /// </summary>
    /// <remarks>
    /// Walked by reflection rather than listed, for the same reason the mapping is
    /// verified that way: a filter added later is covered without anybody
    /// remembering to cover it. A false boolean and a null reference both count as
    /// unset, because that is what "the user did not ask for this" looks like on
    /// the wire.
    /// </remarks>
    private static IEnumerable<string> SetFilterNames(SavedViewFilters filters)
    {
        foreach (System.Reflection.PropertyInfo property in typeof(SavedViewFilters).GetProperties())
        {
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            object? value = property.GetValue(filters);

            if (value is null || value is bool set && !set)
            {
                continue;
            }

            yield return property.Name;
        }
    }

    private static IReadOnlySet<string> FreezeFilters(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

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
