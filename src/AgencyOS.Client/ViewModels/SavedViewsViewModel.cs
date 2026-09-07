using System.Collections.ObjectModel;
using AgencyOS.Contracts.SavedViews;

namespace AgencyOS.Client.ViewModels;

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

    public SavedViewsViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<SavedViewResponse> Views { get; } = [];

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
