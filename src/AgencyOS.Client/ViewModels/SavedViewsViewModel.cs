using System.Collections.ObjectModel;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.SavedViews;

namespace AgencyOS.Client.ViewModels;

/// <summary>One row a saved view returned, flattened for display.</summary>
/// <param name="Id">Identifier of the record, so the row can be opened.</param>
/// <param name="Title">Primary label.</param>
/// <param name="Subtitle">Supporting context.</param>
/// <param name="Kind">Person, Company or Task.</param>
public sealed record SavedViewRow(Guid Id, string Title, string? Subtitle, string Kind);

/// <summary>
/// The user's own saved views: list, create, rename, replace, delete.
/// </summary>
/// <remarks>
/// <para>
/// Server-backed rather than cached. A saved view is a small, personal record the
/// user changes deliberately, so there is nothing to gain from caching it and a
/// stale definition would quietly run the wrong query.
/// </para>
/// <para>
/// Updates carry the version the user was shown, so editing a view from a stale
/// list is refused rather than silently overwriting a change made elsewhere.
/// </para>
/// </remarks>
public sealed class SavedViewsViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private SavedViewResponse? _selected;
    private bool _loaded;
    private bool _hasRun;

    public SavedViewsViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<SavedViewResponse> Views { get; } = [];

    /// <summary>Rows the selected view returned, as one flat list for display.</summary>
    /// <remarks>
    /// Flattened here rather than in the contract: the response is typed per target
    /// so a caller can render each kind properly, and this screen wants one list.
    /// </remarks>
    public ObservableCollection<SavedViewRow> Results { get; } = [];

    /// <summary>Gets a value indicating whether the selected view has been run.</summary>
    public bool HasRun => _hasRun;

    /// <summary>The view the user is looking at, if any.</summary>
    public SavedViewResponse? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public override bool IsEmpty => _loaded && Views.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<SavedViewResponse> views = await _api
                .ListSavedViewsAsync(token)
                .ConfigureAwait(true);

            Replace(views);
        }, cancellationToken);

    /// <summary>Saves a new view and selects it.</summary>
    public Task CreateAsync(
        string name,
        SavedViewDefinitionModel definition,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            SavedViewResponse created = await _api
                .CreateSavedViewAsync(new CreateSavedViewRequest(name, definition), token)
                .ConfigureAwait(true);

            IReadOnlyList<SavedViewResponse> views = await _api.ListSavedViewsAsync(token).ConfigureAwait(true);

            Replace(views);
            Selected = Views.FirstOrDefault(x => x.Id == created.Id);
        }, cancellationToken);

    /// <summary>
    /// Renames a view and replaces its definition.
    /// </summary>
    /// <remarks>
    /// The version comes from the record on screen, not from a fresh read. Reading
    /// the current version first and then sending it would defeat the check
    /// entirely - it would agree with the server by construction, including with a
    /// change the user never saw.
    /// </remarks>
    public Task UpdateAsync(
        SavedViewResponse view,
        string name,
        SavedViewDefinitionModel definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        return RunAsync(async token =>
        {
            await _api
                .UpdateSavedViewAsync(view.Id, new UpdateSavedViewRequest(name, definition, view.Version), token)
                .ConfigureAwait(true);

            IReadOnlyList<SavedViewResponse> views = await _api.ListSavedViewsAsync(token).ConfigureAwait(true);

            Replace(views);
            Selected = Views.FirstOrDefault(x => x.Id == view.Id);
        }, cancellationToken);
    }

    public Task DeleteAsync(SavedViewResponse view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        return RunAsync(async token =>
        {
            await _api.DeleteSavedViewAsync(view.Id, token).ConfigureAwait(true);

            IReadOnlyList<SavedViewResponse> views = await _api.ListSavedViewsAsync(token).ConfigureAwait(true);

            Replace(views);

            if (Selected?.Id == view.Id)
            {
                Selected = null;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Replaces the list with what the server just returned.
    /// </summary>
    /// <remarks>
    /// Marks the list loaded, because every caller reaches here only after a
    /// successful read. Setting the flag in the initial load alone would leave the
    /// empty state hidden after deleting the last view - the list would be empty
    /// and the screen would show nothing at all.
    /// </remarks>
    /// <summary>
    /// Runs the selected view and shows what it returned.
    /// </summary>
    /// <remarks>
    /// The server re-checks the target's read permission when it runs, so a view
    /// composed while a grant was held stops working once it is revoked. The
    /// client does not decide that and does not cache the answer.
    /// </remarks>
    public Task RunAsync(SavedViewResponse view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        return RunAsync(async token =>
        {
            SavedViewResultsResponse results = await _api
                .RunSavedViewAsync(view.Id, limit: null, token)
                .ConfigureAwait(true);

            Results.Clear();

            foreach (PersonSummaryResponse person in results.People)
            {
                Results.Add(new SavedViewRow(person.Id, person.DisplayName, person.Title ?? person.Email, "Person"));
            }

            foreach (CompanySummaryResponse company in results.Companies)
            {
                Results.Add(new SavedViewRow(company.Id, company.Name, company.Type, "Company"));
            }

            foreach (TaskResponse task in results.Tasks)
            {
                Results.Add(new SavedViewRow(
                    task.Id,
                    task.Title,
                    task.DueAt is { } due ? $"due {due:d}" : "no due date",
                    "Task"));
            }

            _hasRun = true;
            OnPropertyChanged(nameof(HasRun));
            OnPropertyChanged(nameof(HasNoResults));
        }, cancellationToken);
    }

    /// <summary>Gets a value indicating whether the view ran and matched nothing.</summary>
    /// <remarks>
    /// Distinct from <see cref="IsEmpty"/>, which is about having no saved views at
    /// all. A view that matches nothing is a useful, correct answer.
    /// </remarks>
    public bool HasNoResults => _hasRun && Results.Count == 0;

    private void Replace(IReadOnlyList<SavedViewResponse> views)
    {
        _loaded = true;

        Views.Clear();

        foreach (SavedViewResponse view in views)
        {
            Views.Add(view);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
