using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
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

            case "option.record":
                _ = RecordOptionAsync();
                break;

            case "obligation.record":
                _ = RecordObligationAsync();
                break;

            case "contract.effective-date.record":
                _ = RecordEffectiveDateAsync();
                break;

            case "obligation.quantify":
                _ = QuantifyObligationAsync();
                break;

            case "obligation.release":
                _ = ReleaseObligationAsync();
                break;

            case "commission.calculate":
                _ = CalculateCommissionAsync();
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

        // The money a contract obliges is a separate projection from its detail,
        // so selecting a contract has to ask for it too.
        _ = LoadMoneyAsync(selected.Id);
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

    private void OnAddPartyClick(object sender, RoutedEventArgs e) => _ = AddPartyAsync();

    private void OnApproveForSignatureClick(object sender, RoutedEventArgs e) =>
        _ = ApproveForSignatureAsync();

    private void OnRecordMoneyObligationClick(object sender, RoutedEventArgs e) =>
        _ = RecordMoneyObligationAsync();

    private void OnRaiseReceivableClick(object sender, RoutedEventArgs e) =>
        _ = RaiseReceivableAsync();

    private void OnQuantifyObligationClick(object sender, RoutedEventArgs e) =>
        _ = QuantifyObligationAsync();

    private void OnReleaseObligationClick(object sender, RoutedEventArgs e) =>
        _ = ReleaseObligationAsync();

    private void OnCalculateCommissionClick(object sender, RoutedEventArgs e) =>
        _ = CalculateCommissionAsync();

    private void OnRecordEffectiveDateClick(object sender, RoutedEventArgs e) =>
        _ = RecordEffectiveDateAsync();

    /// <summary>
    /// What may be done to the obligation that is selected.
    /// </summary>
    /// <remarks>
    /// Each mirrors one explicit guard the server states, and no more. Quantify
    /// refuses an obligation that already carries a figure; release refuses one
    /// already released or cancelled; commission needs a figure to take a share of.
    /// Whether the rest holds is the server's answer, arriving as a refusal the
    /// page shows rather than a rule the client keeps a second copy of.
    /// </remarks>
    private void OnMoneyObligationSelected(object sender, SelectionChangedEventArgs e)
    {
        MonetaryObligationResponse? owed =
            MoneyObligationList.SelectedItem as MonetaryObligationResponse;

        RaiseReceivableButton.IsEnabled = owed is not null;
        QuantifyButton.IsEnabled = owed is { IsQuantified: false, Status: "Expected" };
        ReleaseButton.IsEnabled = owed is { Status: "Expected" or "Raised" };
        CommissionButton.IsEnabled = owed is { IsQuantified: true };
    }

    private void OnRecordSignatureClick(object sender, RoutedEventArgs e) => _ = RecordSignatureAsync();

    private void OnRecordNoticeClick(object sender, RoutedEventArgs e) => _ = RecordNoticeAsync();

    private void OnRecordOptionClick(object sender, RoutedEventArgs e) => _ = RecordOptionAsync();

    private void OnRecordObligationClick(object sender, RoutedEventArgs e) =>
        _ = RecordObligationAsync();

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

    /// <summary>
    /// Puts a party on the contract.
    /// </summary>
    /// <remarks>
    /// The head of the legal chain. Signatures are recorded against parties, so
    /// without this the signature command has nobody to offer and the contract can
    /// never be executed — the gap Reality Closure recorded as F-13.
    /// </remarks>
    private async Task AddPartyAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        IReadOnlyList<PersonSummaryResponse> people = [];
        IReadOnlyList<CompanySummaryResponse> companies = [];

        await Guarded(async () =>
                people = await api.ListPeopleAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        await Guarded(async () =>
                companies = await api.ListCompaniesAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        AddContractPartyDialog dialog = new(
            contract.Contract.Title,
            EntityChoice.ForPeople(people),
            EntityChoice.ForCompanies(companies))
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AddContractPartyAsync(
                contract.Contract.Id,
                dialog.ToRequest(contract.Contract.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    /// <summary>
    /// Clears the contract for signature.
    /// </summary>
    /// <remarks>
    /// The second half of the same break. A signature is refused outside
    /// <c>ApprovedForExecution</c>, so adding parties alone still left the chain
    /// unreachable. The transition itself is the domain's; this asks for it.
    /// </remarks>
    private async Task ApproveForSignatureAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        ContentDialog confirm = new()
        {
            Title = "Approve for signature",
            Content = "The contract is cleared for signing. Drafting versions can still "
                + "be recorded, and AgencyOS neither sends the paper nor collects a "
                + "signature - it records that one happened.",
            PrimaryButtonText = "Approve",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.ChangeContractStatusAsync(
                contract.Contract.Id,
                new ChangeContractStatusRequest("ApprovedForSignature", contract.Contract.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Records what the paper obliges somebody to pay.
    /// </summary>
    /// <remarks>
    /// The head of the money chain. Receivables are raised from an obligation and
    /// payments are allocated against receivables, so the whole delivered finance
    /// tail had nothing to attach to without this.
    /// </remarks>
    private async Task RecordMoneyObligationAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (_detail.LatestVersion is not { } version)
        {
            DetailError("Record a version before recording what it obliges anybody to pay.");
            return;
        }

        RecordMonetaryObligationDialog dialog =
            new(contract.Contract.Title, version.Id, _detail.Parties) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordMonetaryObligationAsync(
                contract.Contract.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadMoneyAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    /// <summary>Turns an obligation into something the agency can collect.</summary>
    private async Task RaiseReceivableAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (MoneyObligationList.SelectedItem is not MonetaryObligationResponse obligation)
        {
            DetailError("Choose what is owed first.");
            return;
        }

        RaiseReceivableDialog dialog = new(obligation) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RaiseReceivableAsync(
                obligation.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadMoneyAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    /// <summary>What this contract obliges anybody to pay, as the server holds it.</summary>
    private async Task LoadMoneyAsync(Guid contractId)
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<MonetaryObligationResponse> owed = [];

        await Reading(async () =>
                owed = await api.ListMonetaryObligationsAsync(contractId).ConfigureAwait(true))
            .ConfigureAwait(true);

        MoneyObligationList.ItemsSource = owed;

        RaiseReceivableButton.IsEnabled = false;
        QuantifyButton.IsEnabled = false;
        ReleaseButton.IsEnabled = false;
        CommissionButton.IsEnabled = false;
    }

    /// <summary>
    /// Puts a figure on an obligation recorded without one.
    /// </summary>
    /// <remarks>
    /// The other half of the four honest answers. An obligation can be recorded as
    /// contingent or genuinely unknown rather than as a false zero, which leaves
    /// the moment the answer arrives - and until now that moment had no route.
    /// </remarks>
    private async Task QuantifyObligationAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (MoneyObligationList.SelectedItem is not MonetaryObligationResponse obligation)
        {
            DetailError("Choose what is owed first.");
            return;
        }

        QuantifyObligationDialog dialog = new(obligation) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await Guarded(() => api.QuantifyObligationAsync(
                    obligation.Id,
                    dialog.ToRequest(obligation.Version),
                    Guid.NewGuid().ToString("N")))
                .ConfigureAwait(true))
        {
            await LoadMoneyAsync(contract.Contract.Id).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Records that an obligation will not fall due after all.
    /// </summary>
    /// <remarks>
    /// Released, never deleted. A contingent payment whose condition did not occur
    /// and one the parties agreed to waive are different facts, and the reason is
    /// what tells them apart three years later - so the server demands one.
    /// </remarks>
    private async Task ReleaseObligationAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (MoneyObligationList.SelectedItem is not MonetaryObligationResponse obligation)
        {
            DetailError("Choose what is owed first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Release obligation",
            obligation.Description ?? obligation.Category,
            "The obligation stays on the contract, marked released, with the reason on the record. Nothing is deleted, and no receivable can be raised from it afterwards.",
            "Release")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await Guarded(() => api.ReleaseObligationAsync(
                    obligation.Id,
                    new ReleaseObligationRequest(dialog.Reason, obligation.Version),
                    Guid.NewGuid().ToString("N")))
                .ConfigureAwait(true))
        {
            await LoadMoneyAsync(contract.Contract.Id).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Works out what the agency is entitled to from an obligation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entitlement, not revenue: what this produces is what the agency may charge
    /// against the whole obligation, and what it has actually earned depends on
    /// what arrives. The two are kept apart everywhere downstream (ADR-0023).
    /// </para>
    /// <para>
    /// The entitlement lives on the Finance workspace, so the outcome says both the
    /// figure and where it went. An act whose only evidence is on another screen
    /// leaves the operator with nothing to check.
    /// </para>
    /// </remarks>
    private async Task CalculateCommissionAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (MoneyObligationList.SelectedItem is not MonetaryObligationResponse obligation)
        {
            DetailError("Choose what is owed first.");
            return;
        }

        IReadOnlyList<TalentSummaryResponse> clients = [];

        await Guarded(async () =>
                clients = await api.ListTalentAsync(clientsOnly: true).ConfigureAwait(true))
            .ConfigureAwait(true);

        if (clients.Count == 0)
        {
            DetailNotice(
                "No clients on file",
                "Commission is worked out for a represented client, and this organization has none recorded yet.");
            return;
        }

        CalculateCommissionDialog dialog = new(obligation, clients) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Looked up, never typed. A commission hangs off the relationship it arises
        // under, and pasting an identifier is how a rate gets attached to the wrong
        // one.
        Guid representationId =
            await RepresentationLookup.ForClientAsync(dialog.SelectedClientPersonId)
                .ConfigureAwait(true);

        if (representationId == Guid.Empty)
        {
            DetailNotice(
                "No representation on file",
                "That client has no representation recorded, so there is nothing for a commission entitlement to arise under.");
            return;
        }

        Guid entitlement = Guid.Empty;

        await Guarded(async () =>
                entitlement = (await api.CalculateCommissionAsync(
                        obligation.Id,
                        dialog.ToRequest(representationId),
                        Guid.NewGuid().ToString("N"))
                    .ConfigureAwait(true)).CommissionEntitlementId)
            .ConfigureAwait(true);

        if (entitlement == Guid.Empty)
        {
            return;
        }

        await ReportCommissionAsync(entitlement).ConfigureAwait(true);
        await LoadMoneyAsync(contract.Contract.Id).ConfigureAwait(true);
    }

    /// <summary>Says what was worked out, and where the operator will find it.</summary>
    private async Task ReportCommissionAsync(Guid commissionId)
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        string figure = "The entitlement was recorded";

        await Reading(async () =>
            {
                CommissionEntitlementResponse commission =
                    await api.GetCommissionAsync(commissionId).ConfigureAwait(true);

                figure = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{MoneyFormatting.Format(commission.Entitled)} entitled for "
                        + $"{commission.ClientDisplayName}");
            })
            .ConfigureAwait(true);

        MoneyOutcomeBar.Title = "Commission calculated";
        MoneyOutcomeBar.Message =
            $"{figure}. It appears under Finance, on the Commissions tab, where what has "
                + "actually been collected against it is shown separately.";
        MoneyOutcomeBar.IsOpen = true;
    }

    /// <summary>
    /// Records when the contract takes effect.
    /// </summary>
    /// <remarks>
    /// The one date AgencyOS will not infer. Execution is derived from signatures;
    /// effectiveness is derived from nothing, because a contract signed in March
    /// and in force from January is ordinary. Recording this changes no status, and
    /// the dialog says so (ADR-0022).
    /// </remarks>
    private async Task RecordEffectiveDateAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        RecordEffectiveDateDialog dialog = new(contract.Contract) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await Guarded(() => api.RecordContractEffectiveDateAsync(
                    contract.Contract.Id,
                    dialog.ToRequest(contract.Contract.Version),
                    Guid.NewGuid().ToString("N")))
                .ConfigureAwait(true))
        {
            await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
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

    /// <summary>
    /// Records an option the contract grants.
    /// </summary>
    /// <remarks>
    /// The other half of an option's life. M8 delivered recording what became of
    /// one and delivered no way to record that it existed, so the resolve control
    /// stood over a list nothing could fill.
    /// </remarks>
    private async Task RecordOptionAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (_detail.LatestVersion is not { } version)
        {
            DetailError("Record a version before recording what it grants.");
            return;
        }

        RecordContractOptionDialog dialog =
            new(contract.Contract.Title, version, _detail.Parties) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await Guarded(() => api.RecordContractOptionAsync(
                    contract.Contract.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
                .ConfigureAwait(true))
        {
            await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Records a promise the contract contains.
    /// </summary>
    /// <remarks>
    /// The non-monetary obligation, which is a different aggregate from the money
    /// owed further down the same tab: one is satisfied, waived or breached, the
    /// other is billed.
    /// </remarks>
    private async Task RecordObligationAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Contract is not { } contract)
        {
            DetailError("Select a contract first.");
            return;
        }

        if (_detail.LatestVersion is not { } version)
        {
            DetailError("Record a version before recording what it obliges anybody to do.");
            return;
        }

        RecordObligationDialog dialog =
            new(contract.Contract.Title, version, _detail.Parties) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await Guarded(() => api.RecordObligationAsync(
                    contract.Contract.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
                .ConfigureAwait(true))
        {
            await _detail.LoadAsync(contract.Contract.Id).ConfigureAwait(true);
        }
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

    /// <summary>
    /// Runs a call and shows the server's own explanation if it refuses.
    /// </summary>
    /// <returns>
    /// Whether the call went through, so a caller can decline to refresh after a
    /// refusal. Reality Closure wave 4 found that it had to: a refused act was
    /// followed by a reload, the reload cleared the bar on its way in, and the
    /// explanation the operator needed was gone before they could read it. An act
    /// that did not happen has nothing new to show either way.
    /// </returns>
    private async Task<bool> Guarded(Func<Task> action)
    {
        try
        {
            DetailBar.IsOpen = false;

            await action().ConfigureAwait(true);

            return true;
        }
        catch (AgencyOsApiException failure)
        {
            DetailError(failure.Detail ?? failure.Message);

            return false;
        }
    }

    /// <summary>
    /// Reads without disturbing what the surface is currently saying.
    /// </summary>
    /// <remarks>
    /// A refresh is not an act. Clearing the message area on the way into one
    /// erases whatever the last act said, including a refusal, which is the one
    /// message an operator most needs to keep.
    /// </remarks>
    private async Task Reading(Func<Task> action)
    {
        try
        {
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
        // A party may be added while the contract still accepts them. Approval is
        // offered where the domain allows it, and the domain remains the authority:
        // these gates only keep the operator from starting something certain to be
        // refused, they do not decide whether it is legal.
        PartyButton.IsEnabled = loaded && _detail.AcceptsNewVersions;
        ApproveButton.IsEnabled = loaded && _detail.Contract?.Contract.Status
            is "Draft" or "UnderReview";
        SignatureButton.IsEnabled = loaded && _detail.OutstandingSignatories.Count > 0;
        NoticeButton.IsEnabled = loaded && _detail.Parties.Count >= 2;

        // The only state the server names as unable to take effect. Notably not
        // gated on execution: a contract can be in force from a date before it was
        // signed, and requiring execution here would invent a rule the domain does
        // not hold.
        EffectiveDateButton.IsEnabled =
            loaded && _detail.Contract?.Contract.Status != "Abandoned";

        // Offered only when the caller can see the terms. The server refuses a
        // reconciliation without them, and a diff with the rows removed would say
        // the draft matched when it did not.
        ReconcileButton.IsEnabled = loaded && _detail.HasTerms && _detail.LatestVersion is not null;

        // Both are read out of a version, so both need one. Whether the version
        // still accepts them is the server's answer, not this gate's.
        RecordOptionButton.IsEnabled = loaded && _detail.LatestVersion is not null
            && _detail.Parties.Count >= 1;
        RecordObligationButton.IsEnabled = loaded && _detail.LatestVersion is not null
            && _detail.Parties.Count >= 2;

        // Money owed is recorded against a drafting version, so it needs one.
        MoneyObligationButton.IsEnabled = loaded && _detail.LatestVersion is not null
            && _detail.Parties.Count >= 2;
        MoneyCaption.Text = _detail.LatestVersion is null
            ? "Record a version before recording what it obliges anybody to pay."
            : _detail.Parties.Count < 2
                ? "Money owed is between two parties on this contract."
                : string.Empty;

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

        // A disabled control with no reason is a dead end, so the caption carries
        // what is missing when recording is not yet possible.
        OptionsCaption.Text = _detail.LatestVersion is null
            ? "Record a version before recording what it grants."
            : _detail.Parties.Count < 1
                ? "An option is held by a party on this contract."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_detail.Options.Count} option(s) recorded.");

        // Past due and breached are counted separately, because they are separate
        // facts: one is a date, the other is a determination somebody made.
        ObligationsCaption.Text = _detail.LatestVersion is null
            ? "Record a version before recording what it obliges anybody to do."
            : _detail.Parties.Count < 2
                ? "An obligation runs between two parties on this contract."
                : string.Create(
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
