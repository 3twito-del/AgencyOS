using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Windows.Dialogs;
using AgencyOS.Windows.Presentation;
using AgencyOS.Contracts.Organizations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The deal workspace: every negotiation, and one negotiation's thread.
/// </summary>
/// <remarks>
/// <para>
/// A dense list rather than a board, for the reason the pipeline gives: a board
/// shows status and hides what a negotiator actually needs, which is who owes whom
/// an answer and what lapses this week.
/// </para>
/// <para>
/// Nothing on this screen transmits an offer. Terms agreed stays a commercial fact
/// and never implies paper, because a screen that blurred the two would be a lie
/// somebody acts on (ADR-0021) — but the screen now <em>asks</em> what paper exists
/// rather than assuming none. It used to assert that no contract had been drafted
/// or signed and that AgencyOS did not track one, which was true under M7 and false
/// from the moment M8 shipped contracts. <see cref="ContractStanding"/> holds that
/// decision; this page only renders it.
/// </para>
/// </remarks>
public sealed partial class DealsPage : Page, IPaletteCommandTarget
{
    private readonly DealListViewModel? _list;
    private readonly DealDetailViewModel? _detail;
    private readonly OfferComparisonViewModel? _comparison;
    private readonly DealTermCatalogViewModel? _catalog;

    public DealsPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new DealListViewModel(api);
            _list.PropertyChanged += (_, _) => Render();

            _detail = new DealDetailViewModel(api);
            _detail.PropertyChanged += (_, _) => RenderDetail();

            _comparison = new OfferComparisonViewModel(api);
            _comparison.PropertyChanged += (_, _) => RenderComparison();

            _catalog = new DealTermCatalogViewModel(api);

            DealList.ItemsSource = _list.Deals;
            OfferList.ItemsSource = _detail.Offers;
            TaskList.ItemsSource = _detail.Tasks;
            HistoryList.ItemsSource = _detail.History;
            ComparisonList.ItemsSource = _comparison.Differences;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "deal.create":
                _ = CreateAsync();
                break;

            case "offer.record.inbound":
                _ = RecordOfferAsync("Inbound");
                break;

            case "offer.record.counter":
                _ = RecordOfferAsync("Outbound");
                break;

            case "offer.answer":
            case "offer.accept":
                _ = AnswerAsync();
                break;

            case "offer.compare":
                _ = CompareAsync();
                break;

            case "go.negotiations":
                Narrow("Negotiating", awaiting: false, agreed: false);
                break;

            case "go.terms.agreed":
                Narrow("TermsAgreed", awaiting: false, agreed: true);
                break;

            case "go.deals.awaiting":
                Narrow(null, awaiting: true, agreed: false);
                break;

            default:
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_list is null)
        {
            ListError.Message = AppServices.Settings.Describe();
            ListError.IsOpen = true;
            return;
        }

        // Three cases, not two. An empty tag is the default and means live work;
        // "all" means the operator asked for everything including the closed ones;
        // anything else is one status they picked, which is sent on its own.
        string? selected = SelectedTag(StatusBox);

        _list.OpenOnly = string.IsNullOrEmpty(selected);
        _list.Status = string.Equals(selected, "all", StringComparison.Ordinal) ? null : selected;
        _list.Kind = SelectedTag(KindBox);
        _list.AwaitingResponse = AwaitingBox.IsChecked == true;
        _list.TermsAgreedOnly = TermsAgreedBox.IsChecked == true;
        _list.Search = SearchBox.Text ?? string.Empty;

        await _list.LoadAsync().ConfigureAwait(true);

        if (_catalog is { Terms.Count: 0 })
        {
            await _catalog.LoadAsync().ConfigureAwait(true);
        }

        Render();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = LoadAsync();

    private void OnDealSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || DealList.SelectedItem is not DealSummaryResponse selected)
        {
            return;
        }

        _ = _detail.LoadAsync(selected.Id);
    }

    private void OnOfferSelected(object sender, SelectionChangedEventArgs e) => RenderTerms();

    private void OnNewClick(object sender, RoutedEventArgs e) => _ = CreateAsync();

    private void OnRecordInboundClick(object sender, RoutedEventArgs e) => _ = RecordOfferAsync("Inbound");

    private void OnRecordOutboundClick(object sender, RoutedEventArgs e) => _ = RecordOfferAsync("Outbound");

    private void OnAnswerClick(object sender, RoutedEventArgs e) => _ = AnswerAsync();

    private void OnCompareClick(object sender, RoutedEventArgs e) => _ = CompareAsync();

    private async Task CreateAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<OpportunitySummaryResponse> opportunities = [];
        IReadOnlyList<OrganizationMemberResponse> members = [];


        await Guarded(async () =>
                opportunities = await api.ListOpportunitiesAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (opportunities.Count == 0)
        {
            DetailNotice(
                "No pursuits to open a deal against",
                "A negotiation comes out of a market conversation, and there are none yet. "
                    + "Open the opportunity in Pipeline first, then bring a target to terms.");

            return;
        }

        // The owner is chosen from this organization's people rather than typed
        // as an identifier (AOS-R001-006).
        await Guarded(async () =>
                members = await api.ListOrganizationMembersAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        CreateDealDialog dialog = new(api, opportunities, members) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreateDealAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Records an offer as made or received.
    /// </summary>
    /// <remarks>
    /// A counter is a new offer answering the standing one. It never edits the
    /// earlier offer: what the other side put on the table stays exactly what they
    /// put on the table.
    /// </remarks>
    private async Task RecordOfferAsync(string direction)
    {
        if (AppServices.Api is not { } api || _detail?.Deal is not { } deal)
        {
            DetailError("Select a deal first.");
            return;
        }

        RecordOfferDialog dialog = new(
            direction,
            deal.Deal.CounterpartyDisplayName,
            _detail.OpenOffer,
            _catalog?.Terms)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordOfferAsync(
                deal.Deal.Id,
                dialog.ToRequest(deal.Deal.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(deal.Deal.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Records what happened to the standing offer.
    /// </summary>
    /// <remarks>
    /// Accepting is consequential and the dialog says so: it records that
    /// commercial terms are agreed, and explicitly not that a contract exists.
    /// </remarks>
    private async Task AnswerAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Deal is not { } deal
            || _detail.OpenOffer is not { } offer)
        {
            DetailError("There is no offer on the table to respond to.");
            return;
        }

        AnswerOfferDialog dialog = new(offer, deal.Deal.CounterpartyDisplayName)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AnswerOfferAsync(
                offer.Id, dialog.ToRequest(offer.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(deal.Deal.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task CompareAsync()
    {
        if (_comparison is null || _detail?.Deal is not { } deal)
        {
            DetailError("Select a deal first.");
            return;
        }

        OfferResponse? current = _detail.OpenOffer ?? _detail.AcceptedOffer;
        OfferResponse? previous = _detail.PreviousOffer;

        if (current is null || previous is null)
        {
            DetailError("A comparison needs two recorded offers in this negotiation.");
            return;
        }

        await _comparison.LoadAsync(deal.Deal.Id, previous.Id, current.Id).ConfigureAwait(true);

        SelectTab("Comparison");
    }

    private void Narrow(string? status, bool awaiting, bool agreed)
    {
        SelectTag(StatusBox, status);
        AwaitingBox.IsChecked = awaiting;
        TermsAgreedBox.IsChecked = agreed;

        _ = LoadAsync();
    }

    /// <summary>Runs a call and shows the server's own explanation if it refuses.</summary>
    /// <remarks>
    /// The message comes from the server. When a counter is refused because the
    /// offer was accepted an hour ago, the useful sentence is the one the domain
    /// wrote.
    /// </remarks>
    private async Task Guarded(Func<Task> action)
    {
        try
        {
            DetailBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            DetailError(failure.Detail ?? failure.Message);
        }
    }

    /// <summary>Says why a workflow cannot start, without calling it a failure.</summary>
    private void DetailNotice(string title, string message)
    {
        DetailBar.Title = title;
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Informational;
        DetailBar.IsOpen = true;
    }

    private void DetailError(string message)
    {
        DetailBar.Title = "That did not happen";
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Error;
        DetailBar.IsOpen = true;
    }

    private void Render()
    {
        if (_list is null)
        {
            return;
        }

        ListBusy.Visibility = _list.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        // See PipelinePage: an absence notice needs the same authority a count does.
        ListEmpty.IsOpen = SummaryAuthority.Knows(_list) && _list.IsEmpty;

        ListError.IsOpen = _list.HasError;
        ListError.Message = _list.ErrorMessage ?? string.Empty;

        SummaryText.Text = SummaryAuthority.Of(
            () => string.Create(
                CultureInfo.InvariantCulture,
                $"{_list.Deals.Count} deal(s); {_list.Awaiting} awaiting an answer, "
                    + $"{_list.TermsAgreed} with terms agreed, {_list.ExpiringSoon} lapsing within a week."),
            _list);
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        bool loaded = _detail.Deal is not null;

        InboundButton.IsEnabled = loaded && _detail.AcceptsOffers;
        OutboundButton.IsEnabled = loaded && _detail.AcceptsOffers;
        AnswerButton.IsEnabled = loaded && _detail.OpenOffer is not null;

        // Offered only when the caller can actually see the numbers. The server
        // refuses a comparison without deals.economics.read, and a button that
        // always failed would be worse than no button.
        CompareButton.IsEnabled =
            loaded && _detail.HasEconomics && _detail.PreviousOffer is not null;

        if (_detail.Deal is not { } deal)
        {
            DetailTitle.Text = "Select a deal";
            DetailStanding.Text = string.Empty;
            StrategyText.Visibility = Visibility.Collapsed;
            ContractStandingBar.IsOpen = false;
            NextActionBar.IsOpen = false;
            return;
        }

        DetailTitle.Text = deal.Deal.Name;
        DetailStanding.Text = _detail.Standing;

        ShowStanding(_detail.ContractStanding);
        ShowNextAction(_detail.NextAction);

        // Nothing is shown when the strategy is absent, and absent is
        // indistinguishable from empty by design.
        StrategyText.Visibility = _detail.HasStrategy ? Visibility.Visible : Visibility.Collapsed;
        StrategyText.Text = deal.StrategyNotes ?? string.Empty;

        RenderTerms();
    }

    /// <summary>Says what the paper is doing, in the words the view model chose.</summary>
    private void ShowStanding(ContractStanding standing)
    {
        ContractStandingBar.IsOpen = standing.Show;

        if (!standing.Show)
        {
            return;
        }

        ContractStandingBar.Title = standing.Title;
        ContractStandingBar.Message = standing.Message;
        ContractStandingBar.Severity = standing.Severity switch
        {
            StandingSeverity.Success => InfoBarSeverity.Success,
            StandingSeverity.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational,
        };
    }

    /// <summary>Offers the open task to do next, if there is one.</summary>
    private void ShowNextAction(NextAction? next) => NextActionBanner.Apply(NextActionBar, next);

    private void RenderTerms()
    {
        OfferResponse? offer = OfferList.SelectedItem as OfferResponse
            ?? _detail?.OpenOffer
            ?? _detail?.AcceptedOffer;

        TermList.ItemsSource = offer?.Terms;

        TermsCaption.Text = offer is null
            ? "Select an offer to see its terms."
            : offer.Terms.Count == 0

                // Empty is what a caller without deals.economics.read sees, and it
                // is also what an offer with only structural terms looks like. The
                // wording deliberately does not distinguish the two.
                ? "No terms to show."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{offer.Direction} offer {offer.Sequence}, {offer.Status.ToLowerInvariant()}.");
    }

    private void RenderComparison()
    {
        if (_comparison is null)
        {
            return;
        }

        ComparisonBar.IsOpen = _comparison.HasError;
        ComparisonBar.Severity = InfoBarSeverity.Error;
        ComparisonBar.Title = "Could not compare";
        ComparisonBar.Message = _comparison.ErrorMessage ?? string.Empty;

        ComparisonCaption.Text = _comparison.Comparison is null
            ? "Compare offers to see what changed."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{_comparison.ChangedCount} term(s) changed between these two offers.");
    }

    private void SelectTab(string header)
    {
        foreach (object item in DetailTabs.TabItems)
        {
            if (item is TabViewItem tab && (tab.Header as string) == header)
            {
                DetailTabs.SelectedItem = tab;
                return;
            }
        }
    }

    private static void SelectTag(ComboBox box, string? tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem entry && (entry.Tag as string) == (tag ?? string.Empty))
            {
                box.SelectedItem = entry;
                return;
            }
        }
    }

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
