using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Deals;
using AgencyOS.Windows.Dialogs;
using AgencyOS.Contracts.Organizations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The contract workspace: every instrument, and one instrument's paper trail.
/// </summary>
/// <remarks>
/// <para>
/// Four dates are shown separately and never merged: when it was signed, when it
/// became executed, when it takes effect and when it ended. Collapsing them would
/// be wrong in exactly the way that matters, because an agreement effective from
/// January and signed in March is ordinary and the difference is the whole
/// question when somebody asks what was in force (ADR-0022).
/// </para>
/// <para>
/// Nothing on this screen stores a document, sends a notice or verifies a
/// signature. Every verb says record, and the surfaces that could be mistaken for
/// more carry a standing note saying what AgencyOS actually did.
/// </para>
/// </remarks>
public sealed partial class ContractsPage : Page, IPaletteCommandTarget
{
    private readonly ContractListViewModel? _list;
    private readonly ContractDetailViewModel? _detail;
    private readonly ReconciliationViewModel? _reconciliation;

    public ContractsPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new ContractListViewModel(api);
            _list.PropertyChanged += (_, _) => Render();

            _detail = new ContractDetailViewModel(api);
            _detail.PropertyChanged += (_, _) => RenderDetail();

            _reconciliation = new ReconciliationViewModel(api);
            _reconciliation.PropertyChanged += (_, _) => RenderReconciliation();

            ContractList.ItemsSource = _list.Contracts;
            VersionList.ItemsSource = _detail.Versions;
            PartyList.ItemsSource = _detail.Parties;
            RightsList.ItemsSource = _detail.RightsGrants;
            OptionList.ItemsSource = _detail.Options;
            ObligationList.ItemsSource = _detail.Obligations;
            NoticeRequirementList.ItemsSource = _detail.NoticeRequirements;
            NoticeList.ItemsSource = _detail.RecordedNotices;
            TaskList.ItemsSource = _detail.Tasks;
            HistoryList.ItemsSource = _detail.History;
            ReconcileList.ItemsSource = _reconciliation.Lines;
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

            case "contract.create":
                _ = CreateAsync();
                break;

            case "contract.version.record":
                _ = RecordVersionAsync();
                break;

            case "contract.reconcile":
                _ = ReconcileAsync();
                break;

            case "contract.signature.record":
                _ = RecordSignatureAsync();
                break;

            case "notice.record":
                _ = RecordNoticeAsync();
                break;

            case "option.resolve":
                _ = ResolveOptionAsync();
                break;

            case "obligation.resolve":
                _ = ResolveObligationAsync();
                break;

            case "go.contracts.awaiting":
                Narrow(awaiting: true, effective: false, differences: false);
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

        _list.Status = SelectedTag(StatusBox);
        _list.Kind = SelectedTag(KindBox);
        _list.AwaitingSignature = AwaitingBox.IsChecked == true;
        _list.EffectiveOnly = EffectiveBox.IsChecked == true;
        _list.DifferencesOnly = DifferencesBox.IsChecked == true;
        _list.Search = SearchBox.Text ?? string.Empty;

        await _list.LoadAsync().ConfigureAwait(true);

        Render();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = LoadAsync();

    private void OnContractSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || ContractList.SelectedItem is not ContractSummaryResponse selected)
        {
            return;
        }

        _ = _detail.LoadAsync(selected.Id);
    }

    private void OnVersionSelected(object sender, SelectionChangedEventArgs e) => RenderTerms();

    private void OnOptionSelected(object sender, SelectionChangedEventArgs e) =>
        ResolveOptionButton.IsEnabled = OptionList.SelectedItem is ContractOptionResponse option
            && option.Status == "Available";

    private void OnObligationSelected(object sender, SelectionChangedEventArgs e) =>
        ResolveObligationButton.IsEnabled = ObligationList.SelectedItem is ObligationResponse;

    private void OnNewClick(object sender, RoutedEventArgs e) => _ = CreateAsync();

    private void OnRecordVersionClick(object sender, RoutedEventArgs e) => _ = RecordVersionAsync();

    private void OnReconcileClick(object sender, RoutedEventArgs e) => _ = ReconcileAsync();

    private void OnRecordSignatureClick(object sender, RoutedEventArgs e) => _ = RecordSignatureAsync();

    private void OnRecordNoticeClick(object sender, RoutedEventArgs e) => _ = RecordNoticeAsync();

    private void OnResolveOptionClick(object sender, RoutedEventArgs e) => _ = ResolveOptionAsync();

    private void OnResolveObligationClick(object sender, RoutedEventArgs e) =>
        _ = ResolveObligationAsync();

    private void OnDifferencesOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_reconciliation is not null)
        {
            _reconciliation.DifferencesOnly = DifferencesOnlyBox.IsChecked == true;
        }
    }

    private async Task CreateAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<DealSummaryResponse> deals = [];
        IReadOnlyList<OrganizationMemberResponse> members = [];


        await Guarded(async () =>
                deals = await api.ListDealsAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (deals.Count == 0)
        {
            DetailNotice(
                "No negotiations to paper",
                "A contract papers a deal whose commercial terms are already agreed, and "
                    + "there are no deals yet. Open the negotiation first.");

            return;
        }

        // The owner is chosen from this organization's people rather than typed
        // as an identifier (AOS-R001-006).
        await Guarded(async () =>
                members = await api.ListOrganizationMembersAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        CreateContractDialog dialog = new(api, deals, members) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreateContractAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Records a drafting version.
    /// </summary>
    /// <remarks>
    /// A new version never edits an earlier one. What a previous draft said stays
    /// exactly what it said, because somebody read it and formed a view on it.
    /// </remarks>
    private async Task RecordVersionAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        RecordContractVersionDialog dialog = new(
            (contract.Contract.LatestVersionNumber ?? 0) + 1,
            contract.Contract.Title)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordContractVersionAsync(
                contract.Contract.Id,
                dialog.ToRequest(contract.Contract.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Compares the selected draft against what was agreed.
    /// </summary>
    /// <remarks>
    /// Offered only when the caller can actually see the terms. The server refuses
    /// a reconciliation without <c>contracts.terms.read</c> and
    /// <c>deals.economics.read</c>, and a button that always failed would be worse
    /// than no button.
    /// </remarks>
    private async Task ReconcileAsync()
    {
        if (_reconciliation is null || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        ContractVersionResponse? version =
            VersionList.SelectedItem as ContractVersionResponse ?? _detail.LatestVersion;

        if (version is null)
        {
            DetailError("Record a drafting version before reconciling.");
            return;
        }

        await _reconciliation.LoadAsync(contract.Contract.Id, version.Id).ConfigureAwait(true);

        SelectTab("Reconciliation");
    }

    private async Task RecordSignatureAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (_detail.OutstandingSignatories.Count == 0)
        {
            DetailError("Every required signature is already recorded.");
            return;
        }

        RecordSignatureDialog dialog = new(_detail.Parties) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordContractSignatureAsync(
                contract.Contract.Id,
                dialog.ToRequest(contract.Contract.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RecordNoticeAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (_detail.Parties.Count < 2)
        {
            DetailError("A notice needs two parties on the contract.");
            return;
        }

        RecordNoticeDialog dialog = new(_detail.Parties, _detail.NoticeRequirements)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordNoticeAsync(
                contract.Contract.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    private async Task ResolveOptionAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Contract is not { } contract
            || OptionList.SelectedItem is not ContractOptionResponse option)
        {
            DetailError("Select an option first.");
            return;
        }

        ResolveOptionDialog dialog = new(option) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.ResolveContractOptionAsync(
                option.Id, dialog.ToRequest(option.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    private async Task ResolveObligationAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Contract is not { } contract
            || ObligationList.SelectedItem is not ObligationResponse obligation)
        {
            DetailError("Select an obligation first.");
            return;
        }

        ResolveObligationDialog dialog = new(obligation) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.ResolveObligationAsync(
                obligation.Id, dialog.ToRequest(obligation.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    private void Narrow(bool awaiting, bool effective, bool differences)
    {
        AwaitingBox.IsChecked = awaiting;
        EffectiveBox.IsChecked = effective;
        DifferencesBox.IsChecked = differences;

        _ = LoadAsync();
    }

    /// <summary>Runs a call and shows the server's own explanation if it refuses.</summary>
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
                $"{_list.Contracts.Count} contract(s); {_list.Unsigned} awaiting signature, "
                    + $"{_list.Effective} in force, {_list.WithDifferences} differing from agreed terms, "
                    + $"{_list.DueThisWeek} with a date within a week."),
            _list);
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        bool loaded = _detail.Contract is not null;

        VersionButton.IsEnabled = loaded && _detail.AcceptsNewVersions;
        SignatureButton.IsEnabled = loaded && _detail.OutstandingSignatories.Count > 0;
        NoticeButton.IsEnabled = loaded && _detail.Parties.Count >= 2;

        // Offered only when the caller can see the terms. The server refuses a
        // reconciliation without them, and a diff with the rows removed would say
        // the draft matched when it did not.
        ReconcileButton.IsEnabled = loaded && _detail.HasTerms && _detail.LatestVersion is not null;

        if (_detail.Contract is not { } contract)
        {
            DetailTitle.Text = "Select a contract";
            DetailStanding.Text = string.Empty;
            DatesText.Text = string.Empty;
            AnalysisText.Visibility = Visibility.Collapsed;
            ReconciliationBar.IsOpen = false;
            UnresolvedBar.IsOpen = false;
            return;
        }

        DetailTitle.Text = contract.Contract.Title;
        DetailStanding.Text = _detail.Standing;
        DatesText.Text = DescribeDates(contract.Contract);

        ReconciliationBar.IsOpen = true;
        ReconciliationBar.Message = _detail.ReconciliationStanding;

        ReconciliationBar.Severity = contract.Contract.UnresolvedDifferenceCount > 0
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;

        // Shown rather than hidden. A clause whose date nobody can work out is
        // exactly what a legal calendar would otherwise lose (ADR-0022).
        UnresolvedBar.IsOpen = _detail.UnresolvedDeadlines.Count > 0;

        // Absent is indistinguishable from empty by design, so nothing is shown
        // when the analysis is not there.
        AnalysisText.Visibility = _detail.HasLegalAnalysis ? Visibility.Visible : Visibility.Collapsed;
        AnalysisText.Text = contract.LegalAnalysis ?? string.Empty;

        PartiesCaption.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_detail.Parties.Count} part(ies); "
                + $"{_detail.OutstandingSignatories.Count} required signature(s) outstanding.");

        OptionsCaption.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_detail.Options.Count} option(s) recorded.");

        // Past due and breached are counted separately, because they are separate
        // facts: one is a date, the other is a determination somebody made.
        ObligationsCaption.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_detail.Obligations.Count} obligation(s); {_detail.PastDue.Count} past due.");

        RenderTerms();
    }

    /// <summary>
    /// Describes the four dates without collapsing any of them.
    /// </summary>
    /// <remarks>
    /// Each clause appears only when the contract carries that date, and none is
    /// inferred from another. "Not yet effective" and "not yet executed" are
    /// different sentences because they are different facts (ADR-0022).
    /// </remarks>
    private static string DescribeDates(ContractSummaryResponse contract)
    {
        string executed = contract.ExecutedOn is { } signed
            ? string.Create(CultureInfo.InvariantCulture, $"Executed {signed:yyyy-MM-dd}")
            : "Not fully executed";

        string effective = contract.EffectiveOn is { } from
            ? string.Create(CultureInfo.InvariantCulture, $"effective from {from:yyyy-MM-dd}")
            : "no effective date recorded";

        string ended = contract.TerminatedOn is { } terminated
            ? string.Create(CultureInfo.InvariantCulture, $", terminated {terminated:yyyy-MM-dd}")
            : string.Empty;

        string next = contract.NextDeadlineOn is { } due
            ? string.Create(
                CultureInfo.InvariantCulture,
                $". Next legal date {due:yyyy-MM-dd}: {contract.NextDeadlineDescription}")
            : ". No legal date this build can work out yet";

        return $"{executed}, {effective}{ended}{next}.";
    }

    private void RenderTerms()
    {
        ContractVersionResponse? version =
            VersionList.SelectedItem as ContractVersionResponse ?? _detail?.LatestVersion;

        TermList.ItemsSource = version?.Terms;

        TermsCaption.Text = version is null
            ? "Select a version to see the terms read out of it."
            : version.Terms.Count == 0

                // Empty is what a caller without contracts.terms.read sees, and it
                // is also what a version nobody has transcribed yet looks like. The
                // wording deliberately does not distinguish the two.
                ? "No terms to show."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Version {version.VersionNumber} ({version.Label}), "
                        + $"{version.Status.ToLowerInvariant()}.");
    }

    private void RenderReconciliation()
    {
        if (_reconciliation is null)
        {
            return;
        }

        ReconcileError.IsOpen = _reconciliation.HasError;
        ReconcileError.Title = "Could not reconcile";
        ReconcileError.Message = _reconciliation.ErrorMessage ?? string.Empty;

        ReconcileCaption.Text = _reconciliation.Reconciliation is null
            ? "Reconcile a version to see what the draft did to what was agreed."
            : _reconciliation.Summary;
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

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
