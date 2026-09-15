using System;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Attaches existing work to a research case.
/// </summary>
/// <remarks>
/// Attaching never copies. The source, signal, thesis, prediction or task stays where
/// it lives and keeps its own classification, which is why a case can be Internal
/// while something attached to it is not — and why a reader sees only the parts they
/// are entitled to (§28).
/// </remarks>
public sealed partial class LinkResearchItemDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;

    /// <param name="api">
    /// Used to list whichever kind the operator picks. One list at a time rather
    /// than five up front: a case usually gathers one kind of thing.
    /// </param>
    public LinkResearchItemDialog(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        InitializeComponent();

        _api = api;

        _ = LoadItemsAsync();
    }

    /// <summary>The record the operator chose, or null while none is chosen.</summary>
    public EntityChoice? Chosen() => ItemBox.SelectedItem as EntityChoice;

    /// <summary>Source, Signal, Thesis, Prediction or Task.</summary>
    public string Kind => (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Source";

    public Guid LinkedId => Chosen()?.Id ?? Guid.Empty;

    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnRequiredChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Chosen() is not null;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => _ = LoadItemsAsync();

    /// <summary>
    /// Lists the chosen kind, dropping whatever was chosen under the previous one.
    /// </summary>
    /// <remarks>
    /// Five kinds, five explicit arms and five existing endpoints. Nothing here
    /// takes an endpoint or a display function as an argument: each kind names its
    /// own call and its own labelling, which is what keeps this a switch rather
    /// than a framework (SS6).
    /// </remarks>
    private async Task LoadItemsAsync()
    {
        if (ItemBox is null)
        {
            return;
        }

        ItemBox.SelectedItem = null;
        ItemBox.ItemsSource = Array.Empty<EntityChoice>();
        ItemBusy.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;

        string kind = Kind;

        try
        {
            System.Collections.Generic.IReadOnlyList<EntityChoice> items = kind switch
            {
                "Signal" => EntityChoice.ForSignals(
                    await _api.ListSignalsAsync().ConfigureAwait(true)),
                "Thesis" => EntityChoice.ForTheses(
                    await _api.ListThesesAsync().ConfigureAwait(true)),
                "Prediction" => EntityChoice.ForPredictions(
                    await _api.ListPredictionsAsync().ConfigureAwait(true)),
                "Task" => EntityChoice.ForTasks(
                    await _api.ListTasksAsync().ConfigureAwait(true)),
                _ => EntityChoice.ForSources(
                    await _api.ListIntelligenceSourcesAsync().ConfigureAwait(true)),
            };

            // The kind may have changed while this was in flight. The later
            // request owns the list; this one drops its result rather than
            // overwriting it (SS27).
            if (Kind != kind)
            {
                return;
            }

            ItemBox.ItemsSource = items;
            ItemHint.Text = items.Count == 0
                ? "There is nothing of that kind to attach yet."
                : string.Empty;
        }
        catch (AgencyOsApiException failure)
        {
            ItemHint.Text = failure.Message;
        }
        finally
        {
            if (Kind == kind)
            {
                ItemBusy.Visibility = Visibility.Collapsed;
            }
        }
    }
}
