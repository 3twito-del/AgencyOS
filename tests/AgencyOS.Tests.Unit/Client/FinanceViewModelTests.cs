using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// Money is rendered with its currency, and two currencies are never added.
/// </summary>
/// <remarks>
/// The rule the whole milestone rests on, tested at the presentation layer because
/// that is the last place it can be broken and the first place a user would
/// believe the result. AgencyOS holds no exchange rate, so a single "total" over a
/// mixed book would be a number that does not exist (ADR-0023).
/// </remarks>
public sealed class MoneyFormattingTests
{
    [Fact]
    public void AnAmount_CarriesItsCurrency()
    {
        Assert.Equal("1,250.00 EUR", MoneyFormatting.Format(new MoneyResponse(1250m, "EUR")));
    }

    /// <summary>
    /// Formatting does not follow the machine's locale.
    /// </summary>
    /// <remarks>
    /// <c>ToString("C")</c> would render a euro amount with a dollar sign on a
    /// US-locale machine, which is precisely the confusion the currency code exists
    /// to prevent.
    /// </remarks>
    [Fact]
    public void AnAmount_IsNotRenderedWithALocaleCurrencySymbol()
    {
        string formatted = MoneyFormatting.Format(new MoneyResponse(1250m, "EUR"));

        Assert.DoesNotContain("$", formatted, StringComparison.Ordinal);
        Assert.Contains("EUR", formatted, StringComparison.Ordinal);
    }

    /// <summary>An unknown amount says so rather than showing zero.</summary>
    /// <remarks>
    /// A contingent bonus nobody can value is not worth nothing. Showing zero would
    /// put a false figure into the reader's head and into every total below it.
    /// </remarks>
    [Fact]
    public void AnAbsentAmount_IsNotZero()
    {
        Assert.Equal("Not yet known", MoneyFormatting.FormatOrUnknown(null));
        Assert.DoesNotContain("0", MoneyFormatting.FormatOrUnknown(null), StringComparison.Ordinal);
    }

    /// <summary>Amounts in different currencies stay in different totals.</summary>
    [Fact]
    public void MixedCurrencies_AreGroupedRatherThanSummed()
    {
        IReadOnlyList<CurrencyTotalResponse> totals = MoneyFormatting.GroupByCurrency(
        [
            new MoneyResponse(100m, "USD"),
            new MoneyResponse(50m, "EUR"),
            new MoneyResponse(25m, "USD"),
        ]);

        Assert.Equal(2, totals.Count);
        Assert.Equal(125m, totals.Single(x => x.Currency == "USD").Total.Amount);
        Assert.Equal(50m, totals.Single(x => x.Currency == "EUR").Total.Amount);
    }

    /// <summary>The rendered summary shows every currency, side by side.</summary>
    [Fact]
    public void ASummaryOverTwoCurrencies_ShowsBoth()
    {
        string summary = MoneyFormatting.FormatTotals(MoneyFormatting.GroupByCurrency(
        [
            new MoneyResponse(100m, "USD"),
            new MoneyResponse(50m, "GBP"),
        ]));

        Assert.Contains("USD", summary, StringComparison.Ordinal);
        Assert.Contains("GBP", summary, StringComparison.Ordinal);
    }
}

/// <summary>The receivable list reports the arithmetic, not a chip.</summary>
public sealed class ReceivableListViewModelTests
{
    [Fact]
    public async Task Loading_ReportsOutstandingPerCurrency()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(FakeAgencyOsApi.Receivable(1000m, allocated: 400m));
        api.Receivables.Add(FakeAgencyOsApi.Receivable(500m, currency: "EUR"));

        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Assert.Equal(2, model.Receivables.Count);
        Assert.Equal(2, model.OutstandingByCurrency.Count);
        Assert.Equal(600m, model.OutstandingByCurrency.Single(x => x.Currency == "USD").Total.Amount);
        Assert.Contains("EUR", model.OutstandingSummary, StringComparison.Ordinal);
    }

    /// <summary>Client money is counted apart from the agency's own.</summary>
    /// <remarks>
    /// The distinction decides whether collected money is revenue or a liability,
    /// and a list that did not surface it would let a desk read somebody else's
    /// money as income (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ClientAndAgencyMoney_AreCountedSeparately()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(FakeAgencyOsApi.Receivable(1000m, beneficiary: "Client"));
        api.Receivables.Add(FakeAgencyOsApi.Receivable(200m, beneficiary: "Agency"));

        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Assert.Equal(1, model.ClientMoney);
    }

    [Fact]
    public async Task OverdueFilter_ReachesTheServer()
    {
        FakeAgencyOsApi api = new();
        api.Receivables.Add(FakeAgencyOsApi.Receivable(1000m, overdue: true));
        api.Receivables.Add(FakeAgencyOsApi.Receivable(1000m));

        ReceivableListViewModel model = new(api) { OverdueOnly = true };

        await model.LoadAsync();

        Assert.True(api.LastReceivableFilter.Overdue);
        Assert.Single(model.Receivables);
        Assert.Equal(1, model.Overdue);
    }

    /// <summary>A refusal surfaces as the server's own explanation.</summary>
    /// <remarks>
    /// Finance refuses rather than redacts, so a caller without the grant must see
    /// that they were refused. Rendering an empty list would look like an agency
    /// with no receivables (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ARefusal_IsShownRatherThanRenderedAsAnEmptyList()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                System.Net.HttpStatusCode.Forbidden,
                "Forbidden",
                "finance.read is required."),
        };

        ReceivableListViewModel model = new(api);

        await model.LoadAsync();

        Assert.True(model.HasError);
        Assert.Contains("finance.read", model.ErrorMessage!, StringComparison.Ordinal);
    }
}

/// <summary>The payment list makes unapplied cash the visible fact.</summary>
public sealed class PaymentListViewModelTests
{
    [Fact]
    public async Task UnappliedCash_IsCountedAndTotalled()
    {
        FakeAgencyOsApi api = new();
        api.Payments.Add(FakeAgencyOsApi.Payment(1000m, allocated: 600m));
        api.Payments.Add(FakeAgencyOsApi.Payment(500m, allocated: 500m));

        PaymentListViewModel model = new(api);

        await model.LoadAsync();

        Assert.Equal(1, model.WithUnappliedCash);
        Assert.Equal(400m, model.UnappliedByCurrency.Single().Total.Amount);
    }

    [Fact]
    public async Task TheUnappliedFilter_ReachesTheServer()
    {
        FakeAgencyOsApi api = new();
        api.Payments.Add(FakeAgencyOsApi.Payment(1000m, allocated: 1000m));

        PaymentListViewModel model = new(api) { UnappliedOnly = true };

        await model.LoadAsync();

        Assert.True(api.LastPaymentFilter.UnappliedOnly);
        Assert.Empty(model.Payments);
    }
}

/// <summary>
/// Recording a payment says what is left over before anything is written.
/// </summary>
/// <remarks>
/// The reason the screen exists. A dialog that took an amount and said "saved"
/// would let somebody record eighty thousand against a hundred-thousand receivable
/// and walk away believing the account was settled (ADR-0023).
/// </remarks>
public sealed class RecordPaymentViewModelTests
{
    [Fact]
    public void AnUnallocatedResidual_IsShownBeforeTheCommit()
    {
        RecordPaymentViewModel model = new(new FakeAgencyOsApi())
        {
            Amount = 1000m,
            Currency = "USD",
        };

        PaymentAllocationDraft line = model.AddAllocation();
        line.ReceivableId = Guid.NewGuid();
        line.Amount = 600m;

        Assert.Equal(600m, model.AllocatedPreview);
        Assert.Equal(400m, model.UnappliedPreview);
        Assert.Contains("unapplied", model.PreviewSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(model.CanRecord);
    }

    [Fact]
    public void AFullyAllocatedPayment_SaysSo()
    {
        RecordPaymentViewModel model = new(new FakeAgencyOsApi())
        {
            Amount = 1000m,
            Currency = "USD",
        };

        PaymentAllocationDraft line = model.AddAllocation();
        line.ReceivableId = Guid.NewGuid();
        line.Amount = 1000m;

        Assert.Equal(0m, model.UnappliedPreview);
        Assert.Contains("All", model.PreviewSummary, StringComparison.Ordinal);
    }

    /// <summary>Allocating more than arrived is refused before it is sent.</summary>
    [Fact]
    public void OverAllocation_CannotBeRecorded()
    {
        RecordPaymentViewModel model = new(new FakeAgencyOsApi())
        {
            Amount = 500m,
            Currency = "USD",
        };

        PaymentAllocationDraft line = model.AddAllocation();
        line.ReceivableId = Guid.NewGuid();
        line.Amount = 900m;

        Assert.True(model.IsOverAllocated);
        Assert.False(model.CanRecord);
    }

    /// <summary>A payment with no currency is not a payment.</summary>
    [Fact]
    public void AnAmountWithoutACurrency_CannotBeRecorded()
    {
        RecordPaymentViewModel model = new(new FakeAgencyOsApi()) { Amount = 500m };

        Assert.False(model.CanRecord);
    }

    /// <summary>
    /// The residual reported afterwards is the server's figure, not the preview.
    /// </summary>
    /// <remarks>
    /// The server recomputes every figure inside the transaction that writes it.
    /// What the operator is told must be what was recorded, not what was intended.
    /// </remarks>
    [Fact]
    public async Task TheRecordedResidual_ComesBackFromTheServer()
    {
        FakeAgencyOsApi api = new();

        RecordPaymentViewModel model = new(api) { Amount = 1000m, Currency = "USD" };

        PaymentAllocationDraft line = model.AddAllocation();
        line.ReceivableId = Guid.NewGuid();
        line.Amount = 250m;

        await model.RecordAsync("key-1");

        Assert.Equal(750m, model.Result!.Unapplied.Amount);
        Assert.Equal("750.00 USD", model.RecordedUnapplied);
        Assert.Equal(1, api.Effects["key-1"]);
    }

    /// <summary>
    /// A shared bank reference is reported, never used to refuse the record.
    /// </summary>
    /// <remarks>
    /// Two genuinely different payments can carry the same remittance text, so a
    /// match is a warning about something worth checking rather than a rule
    /// (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ADuplicateReference_IsAWarningRatherThanARefusal()
    {
        FakeAgencyOsApi api = new();
        api.Payments.Add(FakeAgencyOsApi.Payment(400m, externalReference: "REF-9"));

        RecordPaymentViewModel model = new(api)
        {
            Amount = 1000m,
            Currency = "USD",
            ExternalReference = "REF-9",
        };

        await model.RecordAsync();

        Assert.NotNull(model.Result);
        Assert.True(model.HasPossibleDuplicates);
        Assert.False(model.HasError);
    }

    /// <summary>Every allocation carries the payment's currency.</summary>
    [Fact]
    public async Task EveryAllocation_CarriesTheCurrency()
    {
        FakeAgencyOsApi api = new();

        RecordPaymentViewModel model = new(api) { Amount = 300m, Currency = "gbp" };

        PaymentAllocationDraft line = model.AddAllocation();
        line.ReceivableId = Guid.NewGuid();
        line.Amount = 300m;

        await model.RecordAsync();

        RecordPaymentRequest sent = Assert.Single(api.RecordedPayments);

        Assert.Equal("GBP", sent.Amount.Currency);
        Assert.All(sent.Allocations!, x => Assert.Equal("GBP", x.Amount.Currency));
    }
}

/// <summary>Entitled, collected and outstanding are three columns, not one.</summary>
public sealed class CommissionListViewModelTests
{
    [Fact]
    public async Task EntitlementAndCollection_AreReportedSeparately()
    {
        FakeAgencyOsApi api = new();
        api.Commissions.Add(FakeAgencyOsApi.Commission(1_000_000m, 100_000m, collected: 40_000m));

        CommissionListViewModel model = new(api);

        await model.LoadAsync();

        Assert.Equal(40_000m, model.CollectedByCurrency.Single().Total.Amount);
        Assert.Equal(60_000m, model.OutstandingByCurrency.Single().Total.Amount);
    }

    [Fact]
    public async Task CommissionInTwoCurrencies_IsNeverSummed()
    {
        FakeAgencyOsApi api = new();
        api.Commissions.Add(FakeAgencyOsApi.Commission(100_000m, 10_000m, 10_000m));
        api.Commissions.Add(FakeAgencyOsApi.Commission(80_000m, 8_000m, 0m, currency: "EUR"));

        CommissionListViewModel model = new(api);

        await model.LoadAsync();

        Assert.Equal(2, model.CollectedByCurrency.Count);
    }
}

/// <summary>The ledger shows what it computed, including whether it balances.</summary>
public sealed class LedgerViewModelTests
{
    [Fact]
    public async Task PostedEntries_ReportWhetherTheyBalance()
    {
        FakeAgencyOsApi api = new();
        api.JournalEntries.Add(FakeAgencyOsApi.JournalEntry(500m));
        api.JournalEntries.Add(FakeAgencyOsApi.JournalEntry(300m, source: "ManualAdjustment"));

        LedgerViewModel model = new(api);

        await model.LoadAsync();

        Assert.True(model.AllBalanced);
        Assert.Equal(1, model.ManualEntries);
    }

    /// <summary>An unbalanced entry is reported rather than smoothed over.</summary>
    [Fact]
    public async Task AnUnbalancedEntry_IsVisible()
    {
        FakeAgencyOsApi api = new();
        api.JournalEntries.Add(FakeAgencyOsApi.JournalEntry(500m, balanced: false));

        LedgerViewModel model = new(api);

        await model.LoadAsync();

        Assert.False(model.AllBalanced);
    }
}

/// <summary>A reconciliation states the arithmetic and stops.</summary>
public sealed class ReceivableReconciliationViewModelTests
{
    [Fact]
    public async Task AShortfallWithNoAdjustment_StaysUnexplained()
    {
        FakeAgencyOsApi api = new()
        {
            ReceivableReconciliation = Reconciliation(
                expected: 1000m, allocated: 800m, variance: 200m, outcome: "Shortfall"),
        };

        ReceivableReconciliationViewModel model = new(api);

        await model.LoadAsync(Guid.NewGuid());

        Assert.Equal("Shortfall", model.Outcome);
        Assert.True(model.HasUnexplainedVariance);
        Assert.False(model.IsReconciled);
    }

    /// <summary>
    /// A shortfall with a recorded deduction against it is explained.
    /// </summary>
    /// <remarks>
    /// The deduction is a fact somebody entered. AgencyOS never infers one from a
    /// gap, because that would produce a reconciled receivable and a fabricated tax
    /// record nobody would look at again (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AShortfallWithARecordedDeduction_IsNoLongerUnexplained()
    {
        FakeAgencyOsApi api = new()
        {
            ReceivableReconciliation = Reconciliation(
                expected: 1000m,
                allocated: 800m,
                variance: 200m,
                outcome: "Shortfall",
                adjustments:
                [
                    new PaymentAdjustmentResponse(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        null,
                        "Withholding",
                        new MoneyResponse(200m, "USD"),
                        "State withholding on the remittance advice",
                        null,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        true,
                        "Operator"),
                ]),
        };

        ReceivableReconciliationViewModel model = new(api);

        await model.LoadAsync(Guid.NewGuid());

        Assert.False(model.HasUnexplainedVariance);
    }

    [Fact]
    public async Task AnExactMatch_IsReconciled()
    {
        FakeAgencyOsApi api = new()
        {
            ReceivableReconciliation = Reconciliation(
                expected: 1000m, allocated: 1000m, variance: 0m, outcome: "Reconciled"),
        };

        ReceivableReconciliationViewModel model = new(api);

        await model.LoadAsync(Guid.NewGuid());

        Assert.True(model.IsReconciled);
        Assert.False(model.HasUnexplainedVariance);
    }

    private static ReceivableReconciliationResponse Reconciliation(
        decimal expected,
        decimal allocated,
        decimal variance,
        string outcome,
        IReadOnlyList<PaymentAdjustmentResponse>? adjustments = null) =>
        new(
            Guid.NewGuid(),
            "AR-1",
            Guid.NewGuid(),
            "Feature deal",
            new MoneyResponse(expected, "USD"),
            new MoneyResponse(allocated, "USD"),
            new MoneyResponse(0m, "USD"),
            new MoneyResponse(0m, "USD"),
            new MoneyResponse(variance, "USD"),
            variance > 0m ? 1 : variance < 0m ? -1 : 0,
            outcome,
            "Expected 1,000.00 USD; 800.00 USD accounted for.",
            adjustments ?? [],
            []);
}

/// <summary>
/// The palette offers finance commands in the words AgencyOS can honour.
/// </summary>
/// <remarks>
/// A palette entry is a promise. "Send invoice" would be a promise the build
/// cannot keep, and one an operator would rely on during a dispute about whether
/// something was ever sent (ADR-0023).
/// </remarks>
public sealed class FinancePaletteTests
{
    [Fact]
    public void TheFinanceCommands_AreOffered()
    {
        IReadOnlyList<PaletteCommand> commands = CommandPaletteViewModel.DefaultCommands();

        foreach (string id in new[]
        {
            "go.finance",
            "go.receivables",
            "go.payments.unapplied",
            "invoice.record",
            "payment.record",
            "payment.allocate",
            "commission.calculate",
            "journal.post",
            "receivable.reconcile",
        })
        {
            Assert.Contains(commands, x => x.Id == id);
        }
    }

    /// <summary>No finance command claims an act AgencyOS does not perform.</summary>
    [Fact]
    public void NoFinanceCommand_ClaimsToSendOrCollect()
    {
        IEnumerable<PaletteCommand> finance = CommandPaletteViewModel.DefaultCommands()
            .Where(x => x.Category == "Finance");

        foreach (PaletteCommand command in finance)
        {
            Assert.DoesNotContain("Send", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Collect", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Chase", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Match", command.Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Write-off is a financial act, and the wording says so.</summary>
    [Fact]
    public void WriteOff_IsNamedAsAFinancialActRatherThanADeletion()
    {
        PaletteCommand writeOff = CommandPaletteViewModel.DefaultCommands()
            .Single(x => x.Id == "receivable.write-off");

        Assert.DoesNotContain("Delete", writeOff.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove", writeOff.Title, StringComparison.OrdinalIgnoreCase);
    }
}
