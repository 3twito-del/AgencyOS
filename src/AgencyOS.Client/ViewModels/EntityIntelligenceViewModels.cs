using System.Collections.ObjectModel;
using AgencyOS.Contracts.Intelligence;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// One piece of intelligence that names an entity, and what kind it is.
/// </summary>
/// <param name="Kind">Thesis, Signal or Research, as the workspace calls them.</param>
/// <param name="Id">The record, so the row can open it where it lives.</param>
/// <param name="Title">What it says, in its own words.</param>
/// <param name="Status">Where it stands.</param>
/// <remarks>
/// A presentation shape, not a new domain concept. It carries the kind because a
/// row has to be able to say which workspace tab owns it, and because a thesis
/// and a signal are different claims that must not read alike.
/// </remarks>
public sealed record EntityIntelligenceRow(
    string Kind,
    Guid Id,
    string Title,
    string Status);

/// <summary>
/// The intelligence that names one person or company.
/// </summary>
/// <remarks>
/// <para>
/// Signals, theses, predictions and research cases all carry subjects and render
/// them, so the intelligence surface can say who it concerns. The person and
/// company surfaces could not say what was believed about them: the relationship
/// was recorded in one direction only, and an operator standing on the entity had
/// no way back to it.
/// </para>
/// <para>
/// <strong>Nothing new is stored or inferred.</strong> Each list is fetched
/// through the subject filter the endpoints already publish, so a row appears
/// here only because somebody recorded that subject against that record. No name
/// matching, and no association guessed from prose.
/// </para>
/// <para>
/// Predictions are deliberately absent: a prediction hangs off a thesis, and
/// listing both would show the same judgment twice under two names.
/// </para>
/// </remarks>
public sealed class EntityIntelligenceViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    public EntityIntelligenceViewModel(IAgencyOsApi api) => _api = api;

    /// <summary>What is on record about this entity, theses first.</summary>
    public ObservableCollection<EntityIntelligenceRow> Items { get; } = [];

    public override bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Loads what names this subject, or clears where there is no subject.
    /// </summary>
    /// <param name="subjectKind">
    /// <c>Person</c> or <c>Company</c>, as <c>IntelligenceSubjectKind</c> spells
    /// it. Passed through rather than inferred, because the same identifier can
    /// be a person and a talent profile and they are different subjects.
    /// </param>
    /// <param name="subjectId">The entity being read.</param>
    public Task LoadAsync(
        string subjectKind,
        Guid subjectId,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ThesisResponse> theses = await _api
                .ListThesesAsync(subjectKind: subjectKind, subjectId: subjectId,
                    cancellationToken: token)
                .ConfigureAwait(true);

            IReadOnlyList<SignalResponse> signals = await _api
                .ListSignalsAsync(subjectKind: subjectKind, subjectId: subjectId,
                    cancellationToken: token)
                .ConfigureAwait(true);

            IReadOnlyList<ResearchCaseResponse> research = await _api
                .ListResearchCasesAsync(subjectKind: subjectKind, subjectId: subjectId,
                    cancellationToken: token)
                .ConfigureAwait(true);

            // Theses first: what the agency believes outranks what it noticed and
            // what it is still asking.
            Replace(
                Items,
                [
                    .. theses.Select(x =>
                        new EntityIntelligenceRow("Thesis", x.Id, x.Title, x.Status)),
                    .. signals.Select(x =>
                        new EntityIntelligenceRow("Signal", x.Id, x.Title, x.Verification)),
                    .. research.Select(x =>
                        new EntityIntelligenceRow("Research", x.Id, x.Question, x.Status)),
                ]);

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);

    /// <summary>Forgets what was loaded, when the selection goes away.</summary>
    public void Clear()
    {
        Items.Clear();

        OnPropertyChanged(nameof(IsEmpty));
    }
}
