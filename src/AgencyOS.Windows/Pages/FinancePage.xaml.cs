using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Representation;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The finance workspace: what is owed, what arrived, and where it went.
/// </summary>
/// <remarks>
/// <para>
/// Seven surfaces over one chain: an operative contract says money is payable, a
/// receivable says the agency expects it, an invoice optionally asks for it, a
/// payment says it moved, an allocation says what it was for, a commission says
/// what the agency earned from it, and the ledger records all of it in balanced
/// entries. Every step is separate because conflating any two of them produces a
/// number somebody will act on and cannot defend (ADR-0023).
/// </para>
/// <para>
/// Every verb here is one AgencyOS actually performs. Record invoice, not send
/// invoice. Record payment, not collect payment. Allocate payment, not match
/// payment. The system holds no document, moves no money and matches nothing on
/// its own, and the wording says so rather than leaving the operator to find out.
/// </para>
/// <para>
/// Nothing on this page is cached. Finance is classified ONLINE_ONLY in
/// <c>docs/13_OFFLINE_CLASSIFICATION.md</c>, reads included: a balance from a
/// four-hour-old copy is not a slightly stale balance, it is a different number,
/// and the reader has no way to tell which one is in front of them.
/// </para>
/// </remarks>
public sealed partial class FinancePage : Page, IPaletteCommandTarget
{
    private readonly ReceivableListViewModel? _receivables;
    private readonly InvoiceListViewModel? _invoices;
    private readonly PaymentListViewModel? _payments;
    private readonly CommissionListViewModel? _commissions;
    private readonly LedgerViewModel? _ledger;
    private readonly ReceivableReconciliationViewModel? _reconciliation;
    private readonly FinanceHistoryViewModel? _history;

    public FinancePage()
    {
        InitializeComponent();

        if (AppServices.Api is not { } api)
        {
            return;
        }

        _receivables = new ReceivableListViewModel(api);
        _receivables.PropertyChanged += (_, _) => RenderReceivables();

        _invoices = new InvoiceListViewModel(api);
        _invoices.PropertyChanged += (_, _) => RenderInvoices();

        _payments = new PaymentListViewModel(api);
        _payments.PropertyChanged += (_, _) => RenderPayments();

        _commissions = new CommissionListViewModel(api);
        _commissions.PropertyChanged += (_, _) => RenderCommissions();

        _ledger = new LedgerViewModel(api);
        _ledger.PropertyChanged += (_, _) => RenderLedger();

        _reconciliation = new ReceivableReconciliationViewModel(api);
        _reconciliation.PropertyChanged += (_, _) => RenderReconciliation();

        _history = new FinanceHistoryViewModel(api);

        ReceivableList.ItemsSource = _receivables.Receivables;
        InvoiceList.ItemsSource = _invoices.Invoices;
        PaymentList.ItemsSource = _payments.Payments;
        CommissionList.ItemsSource = _commissions.Commissions;
        CommissionRuleList.ItemsSource = _commissions.Rules;
        BalanceList.ItemsSource = _ledger.Balances;
        JournalList.ItemsSource = _ledger.Entries;
        HistoryList.ItemsSource = _history.Entries;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "go.receivables":
                SelectTab("Receivables");
                break;

            case "go.receivables.overdue":
                OverdueBox.IsChecked = true;
                SelectTab("Receivables");
                _ = LoadReceivablesAsync();
                break;

            case "go.invoices":
                SelectTab("Invoices");
                break;

            case "go.payments":
                SelectTab("Payments");
                break;

            case "go.payments.unapplied":
                UnappliedBox.IsChecked = true;
                SelectTab("Payments");
                _ = LoadPaymentsAsync();
                break;

            case "go.commissions":
                SelectTab("Commissions");
                break;

            case "go.ledger":
                SelectTab("Ledger");
                break;

            case "payment.record":
                _ = RecordPaymentAsync();
                break;

            case "payment.allocate":
                _ = AllocateAsync();
                break;

            case "payment.reverse":
                _ = ReversePaymentAsync();
                break;

            case "invoice.record":
                _ = RecordInvoiceAsync();
                break;

            case "adjustment.record":
                _ = RecordAdjustmentAsync();
                break;

            case "receivable.write-off":
                _ = WriteOffAsync();
                break;

            case "receivable.reconcile":
                _ = ReconcileAsync();
                break;

            case "commission.rule.create":
                _ = CreateCommissionRuleAsync();
                break;

            case "journal.post":
                _ = PostJournalEntryAsync();
                break;

            case "journal.reverse":
                _ = ReverseJournalEntryAsync();
                break;

            default:
                break;
        }
    }

    // ------------------------------------------------------------------ load

    private async Task LoadAsync()
    {
        if (_receivables is null)
        {
            ShowError(AppServices.Settings.Describe());
            return;
        }

        Busy.Visibility = Visibility.Visible;

        await LoadReceivablesAsync().ConfigureAwait(true);
        await LoadInvoicesAsync().ConfigureAwait(true);
        await LoadPaymentsAsync().ConfigureAwait(true);
        await LoadCommissionsAsync().ConfigureAwait(true);
        await LoadLedgerAsync().ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);

        Busy.Visibility = Visibility.Collapsed;

        RenderSummary();
    }

    private async Task LoadReceivablesAsync()
    {
        if (_receivables is null)
        {
            return;
        }

        _receivables.Status = SelectedTag(ReceivableStatusBox);
        _receivables.Beneficiary = SelectedTag(BeneficiaryBox);
        _receivables.OverdueOnly = OverdueBox.IsChecked == true;
        _receivables.UnreconciledOnly = UnreconciledBox.IsChecked == true;
        _receivables.Currency = Currency(ReceivableCurrencyBox.Text);

        await _receivables.LoadAsync().ConfigureAwait(true);

        RenderReceivables();
    }

    private async Task LoadInvoicesAsync()
    {
        if (_invoices is null)
        {
            return;
        }

        _invoices.Status = SelectedTag(InvoiceStatusBox);
        _invoices.OverdueOnly = InvoiceOverdueBox.IsChecked == true;

        await _invoices.LoadAsync().ConfigureAwait(true);

        RenderInvoices();
    }

    private async Task LoadPaymentsAsync()
    {
        if (_payments is null)
        {
            return;
        }

        _payments.Direction = SelectedTag(PaymentDirectionBox);
        _payments.UnappliedOnly = UnappliedBox.IsChecked == true;

        await _payments.LoadAsync().ConfigureAwait(true);

        RenderPayments();
    }

    private async Task LoadCommissionsAsync()
    {
        if (_commissions is null)
        {
            return;
        }

        _commissions.OutstandingOnly = CommissionOutstandingBox.IsChecked == true;

        await _commissions.LoadAsync().ConfigureAwait(true);

        RenderCommissions();
    }

    private async Task LoadLedgerAsync()
    {
        if (_ledger is null)
        {
            return;
        }

        _ledger.Currency = Currency(LedgerCurrencyBox.Text);

        await _ledger.LoadAsync().ConfigureAwait(true);

        RenderLedger();
    }

    private Task LoadHistoryAsync() =>
        _history is null ? Task.CompletedTask : _history.LoadAsync();

    // --------------------------------------------------------------- filters

    private void OnReceivableFilterChanged(object sender, RoutedEventArgs e) =>
        _ = LoadReceivablesAsync();

    private void OnInvoiceFilterChanged(object sender, RoutedEventArgs e) => _ = LoadInvoicesAsync();

    private void OnPaymentFilterChanged(object sender, RoutedEventArgs e) => _ = LoadPaymentsAsync();

    private void OnCommissionFilterChanged(object sender, RoutedEventArgs e) =>
        _ = LoadCommissionsAsync();

    private void OnLedgerFilterChanged(object sender, RoutedEventArgs e) => _ = LoadLedgerAsync();

    private void OnReceivableSelected(object sender, SelectionChangedEventArgs e)
    {
        bool selected = ReceivableList.SelectedItem is ReceivableResponse;

        AdjustmentButton.IsEnabled = selected;

        // Write-off is offered only where something is actually outstanding.
        // Writing off a settled receivable would be a posting with nothing behind
        // it.
        WriteOffButton.IsEnabled =
            ReceivableList.SelectedItem is ReceivableResponse { Outstanding.Amount: > 0m };
    }

    private void OnPaymentSelected(object sender, SelectionChangedEventArgs e)
    {
        PaymentResponse? payment = PaymentList.SelectedItem as PaymentResponse;

        AllocateButton.IsEnabled = payment is { Unapplied.Amount: > 0m, Status: "Recorded" };
        ReversePaymentButton.IsEnabled = payment is { Status: "Recorded" };
    }

    private void OnJournalSelected(object sender, SelectionChangedEventArgs e) =>
        ReverseEntryButton.IsEnabled =
            JournalList.SelectedItem is JournalEntryResponse { Status: "Posted" };

    // ---------------------------------------------------------------- actions

    private void OnRecordPaymentClick(object sender, RoutedEventArgs e) => _ = RecordPaymentAsync();

    private void OnAllocateClick(object sender, RoutedEventArgs e) => _ = AllocateAsync();

    private void OnReversePaymentClick(object sender, RoutedEventArgs e) => _ = ReversePaymentAsync();

    private void OnRecordAdjustmentClick(object sender, RoutedEventArgs e) =>
        _ = RecordAdjustmentAsync();

    private void OnWriteOffClick(object sender, RoutedEventArgs e) => _ = WriteOffAsync();

    private void OnReconcileClick(object sender, RoutedEventArgs e) => _ = ReconcileAsync();

    private void OnCreateRuleClick(object sender, RoutedEventArgs e) => _ = CreateCommissionRuleAsync();

    private void OnPostEntryClick(object sender, RoutedEventArgs e) => _ = PostJournalEntryAsync();

    private void OnReverseEntryClick(object sender, RoutedEventArgs e) =>
        _ = ReverseJournalEntryAsync();

    /// <summary>
    /// Records that money moved.
    /// </summary>
    /// <remarks>
    /// The dialog shows received, allocated and unapplied together before the
    /// commit. What comes back says what the server actually left unapplied, which
    /// is the figure that counts, and any payment sharing the external reference is
    /// reported as a possible duplicate rather than refused (ADR-0023).
    /// </remarks>
    private async Task RecordPaymentAsync()
    {
        if (AppServices.Api is not { } api || _receivables is null)
        {
            return;
        }

        RecordPaymentDialog dialog = new([.. _receivables.Receivables]) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RecordPaymentResponse? result = await Guarded(
                () => api.RecordPaymentAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        if (result is not null)
        {
            ReportRecordedPayment(result);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AllocateAsync()
    {
        if (AppServices.Api is not { } api
            || _receivables is null
            || PaymentList.SelectedItem is not PaymentResponse payment)
        {
            ShowError("Select a payment with unapplied cash first.");
            return;
        }

        AllocatePaymentDialog dialog = new(payment, [.. _receivables.Receivables])
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RecordPaymentResponse? result = await Guarded(
                () => api.AllocatePaymentAsync(
                    payment.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        if (result is not null)
        {
            ReportRecordedPayment(result);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Undoes a payment recorded in error.
    /// </summary>
    /// <remarks>
    /// A reversing payment, not an edit. The original keeps its amount, its
    /// currency and its date, because those are what somebody observed, and both
    /// rows stay readable afterwards (ADR-0023).
    /// </remarks>
    private async Task ReversePaymentAsync()
    {
        if (AppServices.Api is not { } api
            || PaymentList.SelectedItem is not PaymentResponse payment)
        {
            ShowError("Select a payment first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Reverse payment",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{MoneyFormatting.Format(payment.Amount)} from {payment.PayerDisplayName}"),
            "The original payment stays exactly as recorded. A second, reversing payment is written beside it, and both remain readable.",
            "Reverse")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await Guarded(() => api.ReversePaymentAsync(
                payment.Id,
                new ReversePaymentRequest(dialog.Reason, payment.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RecordInvoiceAsync()
    {
        if (AppServices.Api is not { } api
            || _receivables is null
            || ReceivableList.SelectedItem is not ReceivableResponse receivable)
        {
            ShowError("Select a receivable to bill first.");
            return;
        }

        IReadOnlyList<ReceivableResponse> billable =
        [
            .. _receivables.Receivables.Where(
                x => x.ContractId == receivable.ContractId && x.Outstanding.Amount > 0m),
        ];

        RecordInvoiceDialog dialog = new(receivable.ContractTitle, receivable.PayerPartyId, billable)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await Guarded(() => api.RecordInvoiceAsync(
                receivable.ContractId, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RecordAdjustmentAsync()
    {
        if (AppServices.Api is not { } api
            || ReceivableList.SelectedItem is not ReceivableResponse receivable)
        {
            ShowError("Select a receivable first.");
            return;
        }

        // Offers the current unexplained variance as the amount, when a
        // reconciliation is on screen for the same receivable. A suggestion, never
        // an inference: the operator says what the deduction actually was.
        MoneyResponse? variance =
            _reconciliation?.Reconciliation is { } model && model.ReceivableId == receivable.Id
                ? model.Variance
                : null;

        RecordAdjustmentDialog dialog = new(receivable, variance) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await Guarded(() => api.RecordAdjustmentAsync(
                receivable.Id, dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Gives up on collecting what remains.
    /// </summary>
    /// <remarks>
    /// A financial act with a reason and a posting. The receivable keeps its
    /// original amount and stays on the books as written off, because deleting it
    /// would remove the evidence that the agency was ever owed the money
    /// (ADR-0023).
    /// </remarks>
    private async Task WriteOffAsync()
    {
        if (AppServices.Api is not { } api
            || ReceivableList.SelectedItem is not ReceivableResponse receivable)
        {
            ShowError("Select a receivable first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Write off receivable",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{receivable.ContractTitle} - {MoneyFormatting.Format(receivable.Outstanding)} outstanding"),
            "The receivable keeps its original amount and stays on the books, marked written off, with a journal entry recording the loss. Nothing is deleted.",
            "Write off")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.WriteOffReceivableAsync(
                receivable.Id,
                new WriteOffReceivableRequest(dialog.Reason, receivable.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ReconcileAsync()
    {
        if (_reconciliation is null
            || ReceivableList.SelectedItem is not ReceivableResponse receivable)
        {
            ShowError("Select a receivable first.");
            return;
        }

        await _reconciliation.LoadAsync(receivable.Id).ConfigureAwait(true);

        SelectTab("Reconciliation");
    }

    private async Task CreateCommissionRuleAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<TalentSummaryResponse> clients =
            await api.ListTalentAsync(clientsOnly: true).ConfigureAwait(true);

        IReadOnlyList<ContractTermDefinitionResponse> terms =
            await api.ListContractTermsAsync().ConfigureAwait(true);

        CreateCommissionRuleDialog dialog = new(clients, terms) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // The representation is looked up rather than typed. A commission rule
        // hangs off the relationship it arises under, and asking somebody to paste
        // an identifier would be an invitation to attach a rate to the wrong one.
        Guid representationId = await ResolveRepresentationAsync(dialog.SelectedClientPersonId)
            .ConfigureAwait(true);

        if (representationId == Guid.Empty)
        {
            ShowError(
                $"{dialog.SelectedClientName} has no representation on file, so there is nothing for a commission rule to hang off.");
            return;
        }

        _ = await Guarded(() => api.CreateCommissionRuleAsync(
                dialog.ToRequest(representationId), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadCommissionsAsync().ConfigureAwait(true);
    }

    private async Task PostJournalEntryAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        PostJournalEntryDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await Guarded(() => api.PostJournalEntryAsync(
                dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadLedgerAsync().ConfigureAwait(true);
    }

    private async Task ReverseJournalEntryAsync()
    {
        if (AppServices.Api is not { } api
            || JournalList.SelectedItem is not JournalEntryResponse entry)
        {
            ShowError("Select a posted entry first.");
            return;
        }

        FinanceReasonDialog dialog = new(
            "Reverse journal entry",
            entry.Memo,
            "The posted entry is never edited. A second entry saying the opposite is posted beside it, and both stay readable for ever.",
            "Reverse")
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await Guarded(() => api.ReverseJournalEntryAsync(
                entry.Id,
                new ReverseJournalEntryRequest(dialog.Reason),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadLedgerAsync().ConfigureAwait(true);
    }

    /// <summary>Finds the representation a client is currently under.</summary>
    private static async Task<Guid> ResolveRepresentationAsync(Guid personId)
    {
        if (AppServices.Api is not { } api || personId == Guid.Empty)
        {
            return Guid.Empty;
        }

        try
        {
            ClientOverviewResponse overview =
                await api.GetClientOverviewAsync(personId).ConfigureAwait(true);

            return overview.Representation?.Id ?? Guid.Empty;
        }
        catch (AgencyOsApiException)
        {
            return Guid.Empty;
        }
    }

    // --------------------------------------------------------------- render

    private void RenderSummary()
    {
        if (_receivables is null || _payments is null)
        {
            return;
        }

        SummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_receivables.Receivables.Count} receivable(s), {_receivables.Overdue} overdue. "
                + $"Outstanding: {_receivables.OutstandingSummary}. "
                + $"Unapplied: {_payments.UnappliedSummary}.");
    }

    private void RenderReceivables()
    {
        if (_receivables is null)
        {
            return;
        }

        ReceivableEmpty.IsOpen = _receivables.IsEmpty;
        ShowErrorFrom(_receivables.ErrorMessage);

        ReceivableSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_receivables.Receivables.Count} row(s); {_receivables.Overdue} overdue, "
                + $"{_receivables.ClientMoney} held for clients. "
                + $"Outstanding by currency: {_receivables.OutstandingSummary}.");

        RenderSummary();
    }

    private void RenderInvoices()
    {
        if (_invoices is null)
        {
            return;
        }

        ShowErrorFrom(_invoices.ErrorMessage);

        InvoiceSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_invoices.Invoices.Count} invoice(s); {_invoices.Issued} issued, "
                + $"{_invoices.Overdue} past due. Outstanding: {_invoices.OutstandingSummary}.");
    }

    private void RenderPayments()
    {
        if (_payments is null)
        {
            return;
        }

        ShowErrorFrom(_payments.ErrorMessage);

        UnappliedBar.IsOpen = _payments.WithUnappliedCash > 0;

        PaymentSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_payments.Payments.Count} payment(s); {_payments.WithUnappliedCash} with unapplied cash, "
                + $"{_payments.Reversed} reversed. Unapplied: {_payments.UnappliedSummary}.");

        RenderSummary();
    }

    private void RenderCommissions()
    {
        if (_commissions is null)
        {
            return;
        }

        ShowErrorFrom(_commissions.ErrorMessage);

        CommissionSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_commissions.Commissions.Count} entitlement(s); {_commissions.RulesInForce} rule(s) in force. "
                + $"Collected: {_commissions.CollectedSummary}. "
                + $"Not yet collected: {_commissions.OutstandingSummary}.");
    }

    private void RenderLedger()
    {
        if (_ledger is null)
        {
            return;
        }

        ShowErrorFrom(_ledger.ErrorMessage);

        LedgerSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_ledger.Entries.Count} entr(ies) across {_ledger.Currencies.Count} currenc(ies); "
                + $"{_ledger.ManualEntries} written by hand, {_ledger.Reversed} reversed. "
                + $"{(_ledger.AllBalanced ? "All balanced." : "One or more does not balance.")}");
    }

    private void RenderReconciliation()
    {
        if (_reconciliation is null)
        {
            return;
        }

        ShowErrorFrom(_reconciliation.ErrorMessage);

        ReconcileOutcome.Text = _reconciliation.IsEmpty
            ? "Reconcile a receivable to see what arrived against what was expected."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{_reconciliation.Outcome} - {_reconciliation.VarianceDisplay}");

        ReconcileExplanation.Text = _reconciliation.Explanation;

        // Unexplained means unexplained. The bar says so rather than the screen
        // quietly attributing the gap to something plausible (ADR-0023).
        VarianceBar.IsOpen = _reconciliation.HasUnexplainedVariance;

        ReconcileList.ItemsSource = _reconciliation.Reconciliation?.Adjustments;
    }

    /// <summary>
    /// Says what the server did with the money, in the server's own figures.
    /// </summary>
    /// <remarks>
    /// The unapplied figure comes back from the write rather than being recomputed
    /// here, so what the operator is told is what was actually recorded.
    /// </remarks>
    private void ReportRecordedPayment(RecordPaymentResponse result)
    {
        // A shared remittance reference is reported, never acted on. Two
        // genuinely different payments can carry the same text, so a match is a
        // warning rather than a refusal (ADR-0023).
        string duplicates = result.PossibleDuplicates.Count > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" {result.PossibleDuplicates.Count} existing payment(s) carry the same reference. "
                    + $"That is a warning, not a refusal: two payments can share a remittance text.")
            : string.Empty;

        RefusedBar.Title = "Recorded";
        RefusedBar.Severity = result.Unapplied.Amount > 0m || result.PossibleDuplicates.Count > 0
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;

        RefusedBar.Message = string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.Format(result.Allocated)} allocated, "
                + $"{MoneyFormatting.Format(result.Unapplied)} unapplied.{duplicates}");

        RefusedBar.IsOpen = true;
    }

    // -------------------------------------------------------------- plumbing

    /// <summary>Runs a call and shows the server explanation when it refuses.</summary>
    private async Task<T?> Guarded<T>(Func<Task<T>> action)
        where T : class
    {
        try
        {
            ErrorBar.IsOpen = false;

            return await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            ShowError(failure.Detail ?? failure.Message);
            return null;
        }
    }

    private async Task Guarded(Func<Task> action)
    {
        try
        {
            ErrorBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void ShowErrorFrom(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowError(message);
        }
    }

    private void SelectTab(string header)
    {
        foreach (object item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && (tab.Header as string) == header)
            {
                Tabs.SelectedItem = tab;
                return;
            }
        }
    }

    private static string? Currency(string? value)
    {
        string code = (value ?? string.Empty).Trim().ToUpperInvariant();

        return code.Length == 3 ? code : null;
    }

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
