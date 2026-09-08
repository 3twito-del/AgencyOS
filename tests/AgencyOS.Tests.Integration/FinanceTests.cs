using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M9 finance chain, end to end against PostgreSQL.
/// </summary>
/// <remarks>
/// The chain is the milestone: an operative contract says money is payable, a
/// receivable says the agency expects it, a payment says it moved, an allocation
/// says what it was for, a commission says what the agency earned, and the ledger
/// records all of it in balanced entries. Tests that exercised each step alone
/// would each pass while the joins stayed broken (ADR-0023).
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class FinanceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AgencyOsTestFixture _fixture;

    public FinanceTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------ the chain

    /// <summary>
    /// From an executed contract to a balanced ledger, in the order an agency does it.
    /// </summary>
    [Fact]
    public async Task Workflow_FromOperativeContractToBalancedLedger()
    {
        Fixture f = await SetUpAsync("m9-workflow");

        // 1. An obligation exists only because an executed contract says so.
        Guid obligationId = await RecordObligationAsync(f, 1_000_000m);

        MonetaryObligationResponse obligation = await ObligationAsync(f, obligationId);

        Assert.Equal("Compensation", obligation.Category);
        Assert.Equal("Fixed", obligation.AmountKind);
        Assert.Equal(1_000_000m, obligation.Amount!.Amount);
        Assert.Equal("USD", obligation.Amount.Currency);
        Assert.True(obligation.IsQuantified);
        Assert.False(obligation.HasReceivable);

        // 2. A receivable is what the agency expects to collect. The beneficiary
        //    decides whose money it becomes, which decides where the ledger puts it.
        Guid receivableId = await RaiseReceivableAsync(f, obligationId);

        ReceivableResponse receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal("Client", receivable.Beneficiary);
        Assert.Equal(1_000_000m, receivable.OriginalAmount.Amount);
        Assert.Equal(0m, receivable.Allocated.Amount);
        Assert.Equal(1_000_000m, receivable.Outstanding.Amount);
        Assert.Equal("Open", receivable.Status);

        // 3. An invoice is optional and records something issued elsewhere. It
        //    holds no document, because AgencyOS has none to hold.
        RecordInvoiceResponse invoice = await PostAsync<RecordInvoiceResponse>(
            f,
            $"contracts/{f.ContractId}/invoices",
            new RecordInvoiceRequest(
                f.StudioPartyId,
                "USD",
                [new InvoiceLineRequest(receivableId, Money(1_000_000m), "Guaranteed compensation")],
                Reference: "AG-2027-001",
                DueOn: Today.AddDays(30)));

        InvoiceResponse recorded = await GetAsync<InvoiceResponse>(
            f.Client, $"{f.Root}/invoices/{invoice.InvoiceId}");

        Assert.Equal("Draft", recorded.Status);
        Assert.False(recorded.HoldsDocument);
        Assert.Equal(1_000_000m, recorded.Total.Amount);

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/invoices/{invoice.InvoiceId}/issue",
            new IssueInvoiceRequest(Today, recorded.Version)));

        Assert.Equal(
            "Issued",
            (await GetAsync<InvoiceResponse>(f.Client, $"{f.Root}/invoices/{invoice.InvoiceId}"))
                .Status);

        // 4. The commission rule is dated, and the entitlement is worked out under
        //    the rule that governed when the money fell due.
        await PostAsync<CreateCommissionRuleResponse>(
            f,
            "commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddYears(-1),
                RatePercent: 10m));

        CalculateCommissionResponse commission = await PostAsync<CalculateCommissionResponse>(
            f,
            $"monetary-obligations/{obligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        CommissionEntitlementResponse entitlement = await GetAsync<CommissionEntitlementResponse>(
            f.Client, $"{f.Root}/commissions/{commission.CommissionEntitlementId}");

        Assert.Equal(1_000_000m, entitlement.BasisAmount.Amount);
        Assert.Equal(100_000m, entitlement.Entitled.Amount);

        // Nothing has arrived, so nothing has been earned. Entitled and collected
        // are different numbers and the API keeps them apart.
        Assert.Equal(0m, entitlement.Collected.Amount);
        Assert.Equal(100_000m, entitlement.Outstanding.Amount);

        // 5. Money moves. Only part of it is explained, and the rest is reported
        //    back rather than assigned to whatever looks closest.
        RecordPaymentResponse payment = await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(600_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                ExternalReference: "WIRE-77",
                Allocations:
                [
                    new AllocationRequest(receivableId, Money(400_000m)),
                ]),
            HttpStatusCode.Created);

        Assert.Equal(400_000m, payment.Allocated.Amount);
        Assert.Equal(200_000m, payment.Unapplied.Amount);
        Assert.Empty(payment.PossibleDuplicates);

        receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal(400_000m, receivable.Allocated.Amount);
        Assert.Equal(600_000m, receivable.Outstanding.Amount);
        Assert.Equal("PartiallyPaid", receivable.Status);

        // Commission is earned in proportion to what actually arrived: forty
        // thousand of the hundred, not the hundred.
        entitlement = await GetAsync<CommissionEntitlementResponse>(
            f.Client, $"{f.Root}/commissions/{commission.CommissionEntitlementId}");

        Assert.Equal(40_000m, entitlement.Collected.Amount);
        Assert.Equal(60_000m, entitlement.Outstanding.Amount);

        // 6. The residual is applied when somebody says what it is for.
        PaymentResponse recordedPayment = await GetAsync<PaymentResponse>(
            f.Client, $"{f.Root}/payments/{payment.PaymentId}");

        RecordPaymentResponse applied = await PostAsync<RecordPaymentResponse>(
            f,
            $"payments/{payment.PaymentId}/allocations",
            new AllocatePaymentRequest(
                [new AllocationRequest(receivableId, Money(200_000m))],
                recordedPayment.Version));

        Assert.Equal(0m, applied.Unapplied.Amount);

        receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal(600_000m, receivable.Allocated.Amount);
        Assert.Equal(400_000m, receivable.Outstanding.Amount);

        // 7. The ledger balances, in the currency it was posted in.
        IReadOnlyList<AccountBalanceResponse> balances = await ListAsync<AccountBalanceResponse>(
            f.Client, $"{f.Root}/ledger/balances");

        Assert.NotEmpty(balances);
        Assert.All(balances, x => Assert.Equal("USD", x.Currency));

        decimal debits = balances.Sum(x => x.Debits.Amount);
        decimal credits = balances.Sum(x => x.Credits.Amount);

        Assert.Equal(debits, credits);

        // Cash holds what arrived. Client funds payable holds what is owed onward.
        // Commission revenue holds only what the agency earned, never the gross.
        AccountBalanceResponse cash = balances.Single(x => x.Kind == "Cash");
        AccountBalanceResponse clientFunds = balances.Single(x => x.Kind == "ClientFundsPayable");
        AccountBalanceResponse revenue = balances.Single(x => x.Kind == "CommissionRevenue");

        Assert.Equal(600_000m, cash.Balance.Amount);
        Assert.Equal(60_000m, revenue.Balance.Amount);
        Assert.NotEqual(600_000m, revenue.Balance.Amount);
        Assert.Equal(940_000m, clientFunds.Balance.Amount);

        // 8. Every posted entry balances, and each says which act caused it.
        IReadOnlyList<JournalEntryResponse> entries = await ListAsync<JournalEntryResponse>(
            f.Client, $"{f.Root}/ledger/entries?limit=200");

        Assert.All(entries, x => Assert.True(x.IsBalanced));
        Assert.All(entries, x => Assert.Equal("Posted", x.Status));
        Assert.DoesNotContain(entries, x => x.Source == "ManualAdjustment");

        // 9. Reconciliation states the arithmetic and nothing else.
        ReceivableReconciliationResponse reconciliation =
            await GetAsync<ReceivableReconciliationResponse>(
                f.Client, $"{f.Root}/receivables/{receivableId}/reconciliation");

        Assert.Equal("Shortfall", reconciliation.Outcome);
        Assert.Equal(400_000m, reconciliation.Variance.Amount);
        Assert.Equal(1, reconciliation.VarianceDirection);
        Assert.DoesNotContain("fraud", reconciliation.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", reconciliation.Explanation, StringComparison.OrdinalIgnoreCase);

        // 10. The history reads as business acts, not as database rows.
        IReadOnlyList<FinanceHistoryEntryResponse> history =
            await ListAsync<FinanceHistoryEntryResponse>(
                f.Client, $"{f.Root}/finance/history?contractId={f.ContractId}");

        Assert.Contains(history, x => x.Kind == "ObligationRecorded");
        Assert.Contains(history, x => x.Kind == "AllocationApplied");
        Assert.Contains(history, x => x.Kind == "CommissionCalculated");

        // A payment belongs to nobody's contract until it is allocated, and it can
        // settle receivables across several. The contract history therefore shows
        // the allocation, which is the act that connected the money to this
        // instrument, and the unfiltered history shows the arrival (ADR-0023).
        Assert.DoesNotContain(history, x => x.Kind == "PaymentRecorded");

        IReadOnlyList<FinanceHistoryEntryResponse> everything =
            await ListAsync<FinanceHistoryEntryResponse>(f.Client, $"{f.Root}/finance/history");

        Assert.Contains(everything, x => x.Kind == "PaymentRecorded");

        // 11. The command centre is counts and rows, never a forecast.
        FinanceCommandCenterResponse centre = await GetAsync<FinanceCommandCenterResponse>(
            f.Client, $"{f.Root}/finance/command-center");

        Assert.All(centre.OutstandingByCurrency, x => Assert.Equal("USD", x.Currency));
        Assert.Contains(centre.UncollectedCommission, x => x.Outstanding.Amount > 0m);
    }

    // -------------------------------------------------- the operative gate

    /// <summary>
    /// Agreed commercial terms are not a collectible legal amount.
    /// </summary>
    /// <remarks>
    /// The gate the milestone rests on. A negotiation whose terms are agreed says
    /// what the parties intend; only an executed or effective contract says what
    /// anybody can be asked to pay (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ANonOperativeContract_ProducesNoCollectibleAmount()
    {
        Fixture f = await SetUpAsync("m9-gate", execute: false);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/contracts/{f.ContractId}/monetary-obligations",
            ObligationRequest(f, 500_000m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("not yet operative", body, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------- money rules

    /// <summary>Two currencies are never mixed inside one payment.</summary>
    [Fact]
    public async Task ACrossCurrencyAllocation_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-currency");

        Guid obligationId = await RecordObligationAsync(f, 100_000m);
        Guid receivableId = await RaiseReceivableAsync(f, obligationId);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/payments",
            new RecordPaymentRequest(
                "Incoming",
                new MoneyRequest(50_000m, "EUR"),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations: [new AllocationRequest(receivableId, new MoneyRequest(50_000m, "EUR"))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Allocating more than a receivable owes is refused.</summary>
    [Fact]
    public async Task OverAllocation_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-over");

        Guid obligationId = await RecordObligationAsync(f, 100_000m);
        Guid receivableId = await RaiseReceivableAsync(f, obligationId);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(200_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations: [new AllocationRequest(receivableId, Money(200_000m))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Money survives the round trip exactly, to the minor unit.
    /// </summary>
    /// <remarks>
    /// The test that would fail immediately if any figure were held as a float. A
    /// third of a dollar is not representable in binary floating point, and a
    /// system that lost the cent would lose it silently (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task Amounts_SurviveExactlyToTheMinorUnit()
    {
        Fixture f = await SetUpAsync("m9-precision");

        Guid obligationId = await RecordObligationAsync(f, 1_234_567.89m);
        Guid receivableId = await RaiseReceivableAsync(f, obligationId);

        ReceivableResponse receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal(1_234_567.89m, receivable.OriginalAmount.Amount);

        await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(0.01m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations: [new AllocationRequest(receivableId, Money(0.01m))]),
            HttpStatusCode.Created);

        receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal(1_234_567.88m, receivable.Outstanding.Amount);
    }

    /// <summary>
    /// Cash in two currencies is reported per currency and never added.
    /// </summary>
    /// <remarks>
    /// AgencyOS holds no exchange rate, so a single "total cash" over a mixed book
    /// would be a number that does not exist (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task Balances_AreReportedPerCurrency()
    {
        Fixture f = await SetUpAsync("m9-multi-currency");

        Guid dollars = await RecordObligationAsync(f, 100_000m);
        Guid euros = await RecordObligationAsync(f, 80_000m, currency: "EUR");

        Guid dollarReceivable = await RaiseReceivableAsync(f, dollars);
        Guid euroReceivable = await RaiseReceivableAsync(f, euros);

        await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(100_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations: [new AllocationRequest(dollarReceivable, Money(100_000m))]),
            HttpStatusCode.Created);

        await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                new MoneyRequest(80_000m, "EUR"),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations:
                [
                    new AllocationRequest(euroReceivable, new MoneyRequest(80_000m, "EUR")),
                ]),
            HttpStatusCode.Created);

        IReadOnlyList<AccountBalanceResponse> balances = await ListAsync<AccountBalanceResponse>(
            f.Client, $"{f.Root}/ledger/balances");

        AccountBalanceResponse[] cash = [.. balances.Where(x => x.Kind == "Cash")];

        Assert.Equal(2, cash.Length);
        Assert.Equal(100_000m, cash.Single(x => x.Currency == "USD").Balance.Amount);
        Assert.Equal(80_000m, cash.Single(x => x.Currency == "EUR").Balance.Amount);
    }

    // ------------------------------------------------------ unknown amounts

    /// <summary>
    /// An obligation nobody can value is recorded as unknown, not as zero.
    /// </summary>
    /// <remarks>
    /// A backend participation with no figure is an obligation with no figure. Zero
    /// would be a false number in every total that touched it, and a commission
    /// calculated on it would be a false number with a decimal point (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AnUnvaluedObligation_IsUnknownRatherThanZero()
    {
        Fixture f = await SetUpAsync("m9-unknown");

        RecordMonetaryObligationResponse recorded =
            await PostAsync<RecordMonetaryObligationResponse>(
                f,
                $"contracts/{f.ContractId}/monetary-obligations",
                new RecordMonetaryObligationRequest(
                    f.ContractVersionId,
                    f.StudioPartyId,
                    f.ArtistPartyId,
                    "Participation",
                    "Unknown",
                    new DueRuleRequest(
                        "Unstructured",
                        Description: "As and when participation statements are rendered."),
                    Description: "Backend participation, terms to be determined"));

        MonetaryObligationResponse obligation = await ObligationAsync(f, recorded.ObligationId);

        Assert.Null(obligation.Amount);
        Assert.False(obligation.IsQuantified);
        Assert.Null(obligation.DueOn);

        // No basis means no entitlement. A percentage of an unknown is not zero.
        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/monetary-obligations/{recorded.ObligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        string body = await refused.Content.ReadAsStringAsync();

        Assert.Contains("not zero", body, StringComparison.OrdinalIgnoreCase);

        // It can be quantified later, when somebody knows.
        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/monetary-obligations/{recorded.ObligationId}/quantify",
            new QuantifyObligationRequest(
                Money(75_000m), obligation.Version, "First participation statement")));

        obligation = await ObligationAsync(f, recorded.ObligationId);

        Assert.Equal(75_000m, obligation.Amount!.Amount);
        Assert.True(obligation.IsQuantified);
    }

    /// <summary>
    /// A receivable with no due date is never overdue.
    /// </summary>
    /// <remarks>
    /// Overdue is derived from a resolvable date and an outstanding balance, and it
    /// is never stored. Treating silence as "due immediately" would manufacture an
    /// arrears position nobody agreed to (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AReceivableWithNoDueDate_IsNeverOverdue()
    {
        Fixture f = await SetUpAsync("m9-overdue");

        Guid undated = await RecordObligationAsync(f, 50_000m, due: null);
        Guid undatedReceivable = await RaiseReceivableAsync(f, undated, dueOn: null);

        Guid past = await RecordObligationAsync(f, 60_000m, due: Today.AddDays(-90));
        Guid overdueReceivable = await RaiseReceivableAsync(f, past, dueOn: Today.AddDays(-90));

        Assert.False((await ReceivableAsync(f, undatedReceivable)).IsOverdue);
        Assert.True((await ReceivableAsync(f, overdueReceivable)).IsOverdue);

        IReadOnlyList<ReceivableResponse> overdue = await ListAsync<ReceivableResponse>(
            f.Client, $"{f.Root}/receivables?overdueOnly=true");

        Assert.Contains(overdue, x => x.Id == overdueReceivable);
        Assert.DoesNotContain(overdue, x => x.Id == undatedReceivable);
    }

    // ---------------------------------------------------- residuals and gaps

    /// <summary>
    /// Unexplained money stays unapplied rather than being matched to something.
    /// </summary>
    /// <remarks>
    /// Auto-matching would be the system guessing at a payer's intent and then
    /// acting on the guess. Two receivables and a payment that fits either is
    /// exactly where a helpful heuristic goes wrong quietly (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AnUnexplainedResidual_IsNeverAssignedToAnything()
    {
        Fixture f = await SetUpAsync("m9-residual");

        Guid first = await RaiseReceivableAsync(f, await RecordObligationAsync(f, 50_000m));
        Guid second = await RaiseReceivableAsync(f, await RecordObligationAsync(f, 50_000m));

        RecordPaymentResponse payment = await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming", Money(50_000m), Today, "BankTransfer", PayerPartyId: f.StudioPartyId),
            HttpStatusCode.Created);

        Assert.Equal(0m, payment.Allocated.Amount);
        Assert.Equal(50_000m, payment.Unapplied.Amount);

        // Neither receivable moved. The money belongs to whoever paid it until
        // somebody says what it settles.
        Assert.Equal(50_000m, (await ReceivableAsync(f, first)).Outstanding.Amount);
        Assert.Equal(50_000m, (await ReceivableAsync(f, second)).Outstanding.Amount);
    }

    /// <summary>
    /// A gap with no recorded reason stays a gap.
    /// </summary>
    /// <remarks>
    /// The shortfall is not attributed to withholding, to a bank charge or to
    /// anything else. Inferring a tax would produce a reconciled receivable and a
    /// fabricated tax record, and nobody would look at either again (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AShortfall_IsNotAttributedToAnything()
    {
        Fixture f = await SetUpAsync("m9-variance");

        Guid receivableId = await RaiseReceivableAsync(f, await RecordObligationAsync(f, 100_000m));

        await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(92_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                Allocations: [new AllocationRequest(receivableId, Money(92_000m))]),
            HttpStatusCode.Created);

        ReceivableReconciliationResponse before =
            await GetAsync<ReceivableReconciliationResponse>(
                f.Client, $"{f.Root}/receivables/{receivableId}/reconciliation");

        Assert.Equal("Shortfall", before.Outcome);
        Assert.Equal(8_000m, before.Variance.Amount);
        Assert.Empty(before.Adjustments);

        // The deduction is a fact somebody entered, and only then does the
        // receivable reconcile.
        ReceivableResponse receivable = await ReceivableAsync(f, receivableId);

        await PostAsync<RecordAdjustmentResponse>(
            f,
            $"receivables/{receivableId}/adjustments",
            new RecordAdjustmentRequest(
                "Withholding",
                Money(8_000m),
                "State withholding, per the remittance advice",
                Today,
                receivable.Version));

        ReceivableReconciliationResponse after =
            await GetAsync<ReceivableReconciliationResponse>(
                f.Client, $"{f.Root}/receivables/{receivableId}/reconciliation");

        Assert.Equal("Reconciled", after.Outcome);
        Assert.Equal(0m, after.Variance.Amount);
        Assert.Single(after.Adjustments);
    }

    /// <summary>
    /// A repeated bank reference is reported, never used to refuse the record.
    /// </summary>
    /// <remarks>
    /// External references are not assumed globally unique. Two genuinely different
    /// payments can carry the same remittance text, so refusing on a match would
    /// lose a real payment (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ARepeatedBankReference_IsAWarningRatherThanARefusal()
    {
        Fixture f = await SetUpAsync("m9-duplicate");

        await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(1_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                ExternalReference: "REF-DUP"),
            HttpStatusCode.Created);

        RecordPaymentResponse second = await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming",
                Money(1_000m),
                Today,
                "BankTransfer",
                PayerPartyId: f.StudioPartyId,
                ExternalReference: "REF-DUP"),
            HttpStatusCode.Created);

        Assert.Single(second.PossibleDuplicates);
    }

    // ---------------------------------------------------- immutable history

    /// <summary>
    /// A write-off is a financial act, not data cleanup.
    /// </summary>
    /// <remarks>
    /// The receivable keeps its original amount and stays readable. Deleting it
    /// would destroy the evidence that the agency was ever owed the money
    /// (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AWriteOff_IsRecordedRatherThanDeleted()
    {
        Fixture f = await SetUpAsync("m9-writeoff");

        Guid receivableId = await RaiseReceivableAsync(f, await RecordObligationAsync(f, 40_000m));

        ReceivableResponse receivable = await ReceivableAsync(f, receivableId);

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/receivables/{receivableId}/write-off",
            new WriteOffReceivableRequest("Payer entered administration.", receivable.Version)));

        receivable = await ReceivableAsync(f, receivableId);

        Assert.Equal("WrittenOff", receivable.Status);
        Assert.Equal(40_000m, receivable.OriginalAmount.Amount);
        Assert.Contains("administration", receivable.ClosureReason!, StringComparison.Ordinal);

        // The loss is in the ledger, not merely in a status column.
        IReadOnlyList<AccountBalanceResponse> balances = await ListAsync<AccountBalanceResponse>(
            f.Client, $"{f.Root}/ledger/balances");

        Assert.Equal(40_000m, balances.Single(x => x.Kind == "WriteOffExpense").Balance.Amount);
    }

    /// <summary>
    /// A payment recorded in error is reversed, and the original stays as observed.
    /// </summary>
    /// <remarks>
    /// The amount, the currency and the received date are what somebody saw on a
    /// statement. Editing them would rewrite an observation (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AReversedPayment_LeavesTheOriginalIntact()
    {
        Fixture f = await SetUpAsync("m9-reverse-payment");

        RecordPaymentResponse payment = await PostAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming", Money(25_000m), Today.AddDays(-3), "BankTransfer",
                PayerPartyId: f.StudioPartyId),
            HttpStatusCode.Created);

        PaymentResponse original = await GetAsync<PaymentResponse>(
            f.Client, $"{f.Root}/payments/{payment.PaymentId}");

        ReversePaymentResponse reversal = await PostAsync<ReversePaymentResponse>(
            f,
            $"payments/{payment.PaymentId}/reverse",
            new ReversePaymentRequest("Recorded against the wrong tenant.", original.Version));

        PaymentResponse after = await GetAsync<PaymentResponse>(
            f.Client, $"{f.Root}/payments/{payment.PaymentId}");

        Assert.Equal("Reversed", after.Status);
        Assert.Equal(25_000m, after.Amount.Amount);
        Assert.Equal(original.ReceivedOn, after.ReceivedOn);
        Assert.Equal(reversal.ReversalPaymentId, after.ReversedByPaymentId);

        PaymentResponse reversing = await GetAsync<PaymentResponse>(
            f.Client, $"{f.Root}/payments/{reversal.ReversalPaymentId}");

        Assert.Equal("Outgoing", reversing.Direction);
        Assert.Equal(payment.PaymentId, reversing.ReversalOfPaymentId);

        // Cash nets to nothing, and both movements remain in the ledger.
        IReadOnlyList<AccountBalanceResponse> balances = await ListAsync<AccountBalanceResponse>(
            f.Client, $"{f.Root}/ledger/balances");

        Assert.Equal(0m, balances.Single(x => x.Kind == "Cash").Balance.Amount);
    }

    /// <summary>
    /// A posted entry is corrected by another entry, never by an edit.
    /// </summary>
    [Fact]
    public async Task APostedEntry_IsCorrectedByAReversingEntry()
    {
        Fixture f = await SetUpAsync("m9-ledger", role: AgencyRole.Owner);

        PostJournalEntryResponse posted = await PostAsync<PostJournalEntryResponse>(
            f,
            "ledger/entries",
            new PostJournalEntryRequest(
                "Opening balance correction",
                "USD",
                Today,
                [
                    new JournalLineRequest("Cash", "Debit", Money(5_000m)),
                    new JournalLineRequest("Suspense", "Credit", Money(5_000m)),
                ]));

        ReverseJournalEntryResponse reversal = await PostAsync<ReverseJournalEntryResponse>(
            f,
            $"ledger/entries/{posted.JournalEntryId}/reverse",
            new ReverseJournalEntryRequest("Posted to the wrong period."));

        JournalEntryResponse original = await GetAsync<JournalEntryResponse>(
            f.Client, $"{f.Root}/ledger/entries/{posted.JournalEntryId}");

        Assert.Equal("Reversed", original.Status);
        Assert.Equal(reversal.ReversalEntryId, original.ReversedByEntryId);
        Assert.True(original.IsBalanced);

        JournalEntryResponse reversing = await GetAsync<JournalEntryResponse>(
            f.Client, $"{f.Root}/ledger/entries/{reversal.ReversalEntryId}");

        Assert.True(reversing.IsBalanced);
        Assert.Equal(posted.JournalEntryId, reversing.ReversalOfEntryId);

        // The two entries net to nothing and both remain readable.
        IReadOnlyList<AccountBalanceResponse> balances = await ListAsync<AccountBalanceResponse>(
            f.Client, $"{f.Root}/ledger/balances");

        Assert.Equal(0m, balances.Single(x => x.Kind == "Cash").Balance.Amount);
    }

    /// <summary>An entry that does not balance is refused.</summary>
    [Fact]
    public async Task AnUnbalancedEntry_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-unbalanced", role: AgencyRole.Owner);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/ledger/entries",
            new PostJournalEntryRequest(
                "Wrong",
                "USD",
                Today,
                [
                    new JournalLineRequest("Cash", "Debit", Money(5_000m)),
                    new JournalLineRequest("Suspense", "Credit", Money(4_000m)),
                ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// An entry with lines on one side only is refused.
    /// </summary>
    /// <remarks>
    /// Two debits summing to zero would satisfy naive arithmetic and mean nothing.
    /// Double entry is about direction, not about a total (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ASingleSidedEntry_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-single-sided", role: AgencyRole.Owner);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/ledger/entries",
            new PostJournalEntryRequest(
                "One-sided",
                "USD",
                Today,
                [new JournalLineRequest("Cash", "Debit", Money(1_000m))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --------------------------------------------------------- commissions

    /// <summary>
    /// A commission is worked out under the rule that governed at the time.
    /// </summary>
    /// <remarks>
    /// What makes historical finance reproducible. Recalculating a 2027 commission
    /// in 2029 must give the 2027 answer, because the 2027 rule is what the parties
    /// were operating under (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ACommission_UsesTheRuleThatGovernedWhenTheMoneyFellDue()
    {
        Fixture f = await SetUpAsync("m9-governing");

        // Ten per cent until the end of last year, fifteen since.
        CreateCommissionRuleResponse old = await PostAsync<CreateCommissionRuleResponse>(
            f,
            "commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddYears(-3),
                RatePercent: 10m,
                EffectiveTo: Today.AddDays(-30)));

        await PostAsync<CreateCommissionRuleResponse>(
            f,
            "commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddDays(-29),
                RatePercent: 15m));

        Assert.NotEqual(Guid.Empty, old.CommissionRuleId);

        Guid obligationId = await RecordObligationAsync(f, 200_000m, due: Today.AddDays(-60));

        CalculateCommissionResponse calculated = await PostAsync<CalculateCommissionResponse>(
            f,
            $"monetary-obligations/{obligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        CommissionEntitlementResponse entitlement = await GetAsync<CommissionEntitlementResponse>(
            f.Client, $"{f.Root}/commissions/{calculated.CommissionEntitlementId}");

        // The older rule governed sixty days ago, so ten per cent, not fifteen.
        Assert.Equal(10m, entitlement.RatePercentSnapshot);
        Assert.Equal(20_000m, entitlement.Entitled.Amount);
    }

    /// <summary>
    /// With no rule in force there is no entitlement, and the refusal says why.
    /// </summary>
    /// <remarks>
    /// There is no default commission rate anywhere in AgencyOS. A default would be
    /// a number the agency never agreed with anybody (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task WithNoGoverningRule_NothingIsCalculated()
    {
        Fixture f = await SetUpAsync("m9-no-rule");

        Guid obligationId = await RecordObligationAsync(f, 100_000m);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/monetary-obligations/{obligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        IReadOnlyList<CommissionEntitlementResponse> commissions =
            await ListAsync<CommissionEntitlementResponse>(f.Client, $"{f.Root}/commissions");

        Assert.Empty(commissions);
    }

    /// <summary>Two rules in force at once are refused when the second is created.</summary>
    [Fact]
    public async Task OverlappingCommissionRules_AreRefused()
    {
        Fixture f = await SetUpAsync("m9-overlap");

        await PostAsync<CreateCommissionRuleResponse>(
            f,
            "commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddYears(-1),
                RatePercent: 10m));

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddMonths(-6),
                RatePercent: 12m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A rate above the cap is refused as a data-entry error.</summary>
    [Fact]
    public async Task AnImplausibleRate_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-rate-cap");

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddYears(-1),
                RatePercent: 95m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Recalculating supersedes rather than overwrites.
    /// </summary>
    /// <remarks>
    /// "We calculated a hundred thousand and then recalculated to ninety" is a
    /// different story from "we calculated ninety", and only the first is true
    /// (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task Recalculating_SupersedesRatherThanOverwrites()
    {
        Fixture f = await SetUpAsync("m9-supersede");

        await PostAsync<CreateCommissionRuleResponse>(
            f,
            "commission-rules",
            new CreateCommissionRuleRequest(
                f.RepresentationId,
                f.ClientPersonId,
                "GrossCompensation",
                Today.AddYears(-1),
                RatePercent: 10m));

        Guid obligationId = await RecordObligationAsync(f, 100_000m);

        await PostAsync<CalculateCommissionResponse>(
            f,
            $"monetary-obligations/{obligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        await PostAsync<CalculateCommissionResponse>(
            f,
            $"monetary-obligations/{obligationId}/commission",
            new CalculateCommissionRequest(f.ClientPersonId, f.RepresentationId));

        IReadOnlyList<CommissionEntitlementResponse> commissions =
            await ListAsync<CommissionEntitlementResponse>(f.Client, $"{f.Root}/commissions");

        Assert.Equal(2, commissions.Count);
        Assert.Single(commissions, x => x.Status == "Calculated");
        Assert.Single(commissions, x => x.Status == "Superseded");
    }

    // -------------------------------------------------------- authorization

    /// <summary>
    /// Finance access is disjoint from commercial access.
    /// </summary>
    /// <remarks>
    /// Being able to read what a deal pays confers nothing about what the agency
    /// has collected. Different disclosures, different readerships (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AnObserver_HasNoFinanceAccessAtAll()
    {
        Fixture f = await SetUpAsync("m9-observer-owner");

        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "m9-observer");

        HttpClient client = _fixture.CreateClient(observer.Subject);
        string root = $"/api/v1/organizations/{observer.Organization.Id.Value}";

        foreach (string path in new[]
        {
            "receivables", "invoices", "payments", "commissions", "ledger/balances",
            "finance/command-center",
        })
        {
            using HttpResponseMessage response = await client.GetAsync($"{root}/{path}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.NotEqual(Guid.Empty, f.ContractId);
    }

    /// <summary>
    /// Posting a hand-written journal entry is a narrower grant than doing finance.
    /// </summary>
    /// <remarks>
    /// Every other posting follows from an act already authorized and is made by
    /// the system inside the same transaction. Writing an entry nothing else
    /// produced is the one act that needs its own grant (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AMember_CannotPostAHandWrittenEntry()
    {
        Fixture f = await SetUpAsync("m9-ledger-grant");

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/ledger/entries",
            new PostJournalEntryRequest(
                "Manual",
                "USD",
                Today,
                [
                    new JournalLineRequest("Cash", "Debit", Money(100m)),
                    new JournalLineRequest("Suspense", "Credit", Money(100m)),
                ]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Finance is refused whole rather than returned with rows missing.
    /// </summary>
    /// <remarks>
    /// The one place M9 departs from the M7 and M8 pattern. A balance with three
    /// allocations hidden is not a partial view of the balance, it is a different
    /// number, and somebody will act on it (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task FinanceRefusesRatherThanRedacts()
    {
        SeededActor administrator =
            await _fixture.SeedActorAsync(AgencyRole.Administrator, "m9-refuse");

        HttpClient client = _fixture.CreateClient(administrator.Subject);
        string root = $"/api/v1/organizations/{administrator.Organization.Id.Value}";

        // An administrator may read finance but not write it. The read succeeds
        // whole; the write is refused rather than silently ignored.
        using HttpResponseMessage read = await client.GetAsync($"{root}/receivables");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using HttpResponseMessage write = await client.PostAsJsonAsync(
            $"{root}/payments",
            new RecordPaymentRequest("Incoming", Money(1_000m), Today, "BankTransfer"));

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    // --------------------------------------------- concurrency, idempotency

    /// <summary>A write against a stale version is refused.</summary>
    [Fact]
    public async Task AStaleVersion_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-concurrency");

        Guid receivableId = await RaiseReceivableAsync(f, await RecordObligationAsync(f, 10_000m));

        ReceivableResponse receivable = await ReceivableAsync(f, receivableId);

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/receivables/{receivableId}/write-off",
            new WriteOffReceivableRequest("Uncollectable.", receivable.Version)));

        using HttpResponseMessage stale = await f.Client.PostAsJsonAsync(
            $"{f.Root}/receivables/{receivableId}/write-off",
            new WriteOffReceivableRequest("Uncollectable.", receivable.Version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    /// <summary>
    /// A retried payment takes effect once.
    /// </summary>
    /// <remarks>
    /// The property that matters most in finance: a lost response must not turn one
    /// payment into two. The second call returns the stored answer rather than
    /// recording anything (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task ARetriedPayment_TakesEffectOnce()
    {
        Fixture f = await SetUpAsync("m9-idempotency");

        string key = Guid.NewGuid().ToString("N");

        RecordPaymentRequest request = new(
            "Incoming", Money(12_345m), Today, "BankTransfer", PayerPartyId: f.StudioPartyId);

        RecordPaymentResponse first = await KeyedAsync<RecordPaymentResponse>(
            f, "payments", request, key, HttpStatusCode.Created);

        RecordPaymentResponse replay = await KeyedAsync<RecordPaymentResponse>(
            f, "payments", request, key, HttpStatusCode.Created);

        Assert.Equal(first.PaymentId, replay.PaymentId);

        IReadOnlyList<PaymentResponse> payments =
            await ListAsync<PaymentResponse>(f.Client, $"{f.Root}/payments");

        Assert.Single(payments);
    }

    /// <summary>The same key with a different payment is refused.</summary>
    [Fact]
    public async Task TheSameKeyWithADifferentPayment_IsRefused()
    {
        Fixture f = await SetUpAsync("m9-idempotency-mismatch");

        string key = Guid.NewGuid().ToString("N");

        await KeyedAsync<RecordPaymentResponse>(
            f,
            "payments",
            new RecordPaymentRequest(
                "Incoming", Money(1_000m), Today, "BankTransfer", PayerPartyId: f.StudioPartyId),
            key,
            HttpStatusCode.Created);

        using HttpRequestMessage message = new(HttpMethod.Post, $"{f.Root}/payments")
        {
            Content = JsonContent.Create(new RecordPaymentRequest(
                "Incoming", Money(9_999m), Today, "BankTransfer", PayerPartyId: f.StudioPartyId)),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ------------------------------------------------------------ saved views

    /// <summary>
    /// A receivables saved view runs, and narrows by nothing economic.
    /// </summary>
    /// <remarks>
    /// A saved view is a query somebody else may run. A filter reading "outstanding
    /// over fifty thousand" would tell its reader the balance whether or not they
    /// hold <c>finance.read</c> (ADR-0023).
    /// </remarks>
    [Fact]
    public async Task AReceivablesSavedView_Runs()
    {
        Fixture f = await SetUpAsync("m9-saved-view");

        Guid receivableId = await RaiseReceivableAsync(
            f, await RecordObligationAsync(f, 30_000m, due: Today.AddDays(-10)),
            dueOn: Today.AddDays(-10));

        SavedViewResponse view = await CreatedAsync<SavedViewResponse>(f.Client.PostAsJsonAsync(
            $"{f.Root}/saved-views",
            new CreateSavedViewRequest(
                "Overdue receivables",
                new SavedViewDefinitionModel(
                    7,
                    "Receivables",
                    new SavedViewFiltersModel(
                        OverdueReceivablesOnly: true, CurrencyCode: "USD")))));

        SavedViewResultsResponse results = await GetAsync<SavedViewResultsResponse>(
            f.Client, $"{f.Root}/saved-views/{view.Id}/results");

        Assert.Equal("Receivables", results.Target);
        Assert.Contains(results.Receivables, x => x.Id == receivableId);
    }

    // ------------------------------------------------------------- fixtures

    private sealed record Fixture(
        SeededActor Actor,
        HttpClient Client,
        string Root,
        Guid ContractId,
        Guid ContractVersionId,
        Guid StudioPartyId,
        Guid ArtistPartyId,
        Guid ClientPersonId,
        Guid RepresentationId);

    /// <summary>
    /// An executed contract with two parties, and a client the agency represents.
    /// </summary>
    /// <remarks>
    /// The whole of M4 through M8 stands behind one M9 obligation, which is the
    /// point: finance is downstream of everything, and a shortcut here would be a
    /// test of a system nobody runs.
    /// </remarks>
    private async Task<Fixture> SetUpAsync(
        string label,
        bool execute = true,
        AgencyRole role = AgencyRole.Member)
    {
        SeededActor actor = await _fixture.SeedActorAsync(role, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        // A member does the setting up, because an administrator cannot write.
        SeededActor builderActor = role == AgencyRole.Member
            ? actor
            : await _fixture.SeedActorAsync(AgencyRole.Member, $"{label}-builder");

        HttpClient builder = role == AgencyRole.Member
            ? client
            : _fixture.CreateClient(builderActor.Subject);

        string builderRoot = role == AgencyRole.Member
            ? root
            : $"/api/v1/organizations/{builderActor.Organization.Id.Value}";

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/projects", new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/companies",
                new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        PersonDetailResponse writer = await CreatedAsync<PersonDetailResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/people", new CreatePersonRequest("Ada Sallow")));

        // The client the agency represents. Commission hangs off the relationship,
        // never off a rate typed into a transaction.
        ProspectResponse prospect = await CreatedAsync<ProspectResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/prospects",
                new CreateProspectRequest(
                    writer.Person.Id, builderActor.User.Id.Value, Today.AddYears(-2))));

        RepresentationResponse representation = await CreatedAsync<RepresentationResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/prospects/{prospect.Id}/convert",
                new ConvertProspectRequest(
                    Today.AddYears(-2),
                    builderActor.User.Id.Value,
                    ["Literary"],
                    prospect.Version)));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    builderActor.User.Id.Value,
                    Subjects:
                    [
                        new OpportunitySubjectRequest("Project", project.Project.Id, "Primary"),
                    ])));

        await NoContentAsync(builder.PostAsJsonAsync(
            $"{builderRoot}/opportunities/{opportunity.Opportunity.Id}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        OpportunityDetailResponse active = await GetAsync<OpportunityDetailResponse>(
            builder, $"{builderRoot}/opportunities/{opportunity.Opportunity.Id}");

        Guid targetId = await CreatedIdAsync(builder.PostAsJsonAsync(
            $"{builderRoot}/opportunities/{opportunity.Opportunity.Id}/targets",
            new AddOpportunityTargetRequest(
                active.Opportunity.Version, CompanyId: studio.Company.Id)));

        foreach (string step in new[] { "Approved", "Contacted", "Engaged", "Interested" })
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                builder, $"{builderRoot}/opportunity-targets/{targetId}");

            await NoContentAsync(builder.PostAsJsonAsync(
                $"{builderRoot}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(step, target.Version)));
        }

        DealDetailResponse deal = await CreatedAsync<DealDetailResponse>(builder.PostAsJsonAsync(
            $"{builderRoot}/deals",
            new CreateDealRequest(
                opportunity.Opportunity.Id,
                targetId,
                "The Undertow - Northgate",
                "Writing",
                builderActor.User.Id.Value)));

        Guid offerId = await CreatedOfferAsync(
            builder, builderRoot, deal.Deal.Id, deal.Deal.Version);

        OfferResponse offer = await GetAsync<OfferResponse>(
            builder, $"{builderRoot}/offers/{offerId}");

        using (HttpResponseMessage accepted = await builder.PostAsJsonAsync(
            $"{builderRoot}/offers/{offerId}/answer",
            new AnswerOfferRequest("Accept", offer.Version)))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        ContractDetailResponse contract = await CreatedAsync<ContractDetailResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/contracts",
                new CreateContractRequest(
                    deal.Deal.Id,
                    offerId,
                    "Undertow writer agreement",
                    "LongForm",
                    builderActor.User.Id.Value)));

        Guid studioParty = await AddPartyAsync(
            builder,
            builderRoot,
            contract.Contract.Id,
            new AddContractPartyRequest(
                "Studio", contract.Contract.Version, CompanyId: studio.Company.Id));

        contract = await GetAsync<ContractDetailResponse>(
            builder, $"{builderRoot}/contracts/{contract.Contract.Id}");

        Guid artistParty = await AddPartyAsync(
            builder,
            builderRoot,
            contract.Contract.Id,
            new AddContractPartyRequest(
                "Artist", contract.Contract.Version, PersonId: writer.Person.Id));

        contract = await GetAsync<ContractDetailResponse>(
            builder, $"{builderRoot}/contracts/{contract.Contract.Id}");

        RecordContractVersionResponse version = await CreatedAsync<RecordContractVersionResponse>(
            builder.PostAsJsonAsync(
                $"{builderRoot}/contracts/{contract.Contract.Id}/versions",
                new RecordContractVersionRequest(
                    "Execution copy",
                    "Inbound",
                    contract.Contract.Version,
                    Terms:
                    [
                        new ContractTermRequest(
                            "GuaranteedCompensation",
                            new TermValueRequest("Money", Amount: 1_000_000m, Currency: "USD")),
                    ])));

        if (execute)
        {
            // Through review and approval before anybody signs. The statuses that
            // mean somebody signed are reachable only by recording a signature.
            foreach (string step in new[] { "SentForReview", "ApprovedForSignature" })
            {
                contract = await GetAsync<ContractDetailResponse>(
                    builder, $"{builderRoot}/contracts/{contract.Contract.Id}");

                await NoContentAsync(builder.PostAsJsonAsync(
                    $"{builderRoot}/contracts/{contract.Contract.Id}/status",
                    new ChangeContractStatusRequest(step, contract.Contract.Version)));
            }

            foreach (Guid partyId in new[] { studioParty, artistParty })
            {
                contract = await GetAsync<ContractDetailResponse>(
                    builder, $"{builderRoot}/contracts/{contract.Contract.Id}");

                using HttpResponseMessage signed = await builder.PostAsJsonAsync(
                    $"{builderRoot}/contracts/{contract.Contract.Id}/signatures",
                    new RecordSignatureRequest(
                        partyId, Today.AddDays(-45), "Wet", contract.Contract.Version));

                Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            }
        }

        return new Fixture(
            actor,
            client,
            root,
            contract.Contract.Id,
            version.VersionId,
            studioParty,
            artistParty,
            writer.Person.Id,
            representation.Id);
    }

    // --------------------------------------------------------------- helpers

    private static MoneyRequest Money(decimal amount, string currency = "USD") =>
        new(amount, currency);

    private static RecordMonetaryObligationRequest ObligationRequest(
        Fixture f,
        decimal amount,
        string currency = "USD",
        DateOnly? due = null) =>
        new(
            f.ContractVersionId,
            f.StudioPartyId,
            f.ArtistPartyId,
            "Compensation",
            "Fixed",
            due is { } on
                ? new DueRuleRequest("Absolute", on)

                // No date is a real answer, and the clause's own words are what
                // records it. A rule with no date and no words would record
                // nothing about what is actually required (ADR-0022).
                : new DueRuleRequest(
                    "Unstructured", Description: "On terms the contract does not fix."),
            Money(amount, currency),
            Description: "Guaranteed compensation");

    private Task<Guid> RecordObligationAsync(
        Fixture f,
        decimal amount,
        string currency = "USD",
        DateOnly? due = null) =>
        RecordObligationCoreAsync(f, ObligationRequest(f, amount, currency, due));

    private static async Task<Guid> RecordObligationCoreAsync(
        Fixture f,
        RecordMonetaryObligationRequest request)
    {
        RecordMonetaryObligationResponse recorded =
            await PostAsync<RecordMonetaryObligationResponse>(
                f, $"contracts/{f.ContractId}/monetary-obligations", request);

        return recorded.ObligationId;
    }

    /// <remarks>
    /// The amount and currency are left to the obligation, which is the ordinary
    /// case: a receivable raised from an obligation collects what the obligation
    /// says, in the currency the obligation says it in.
    /// </remarks>
    private static async Task<Guid> RaiseReceivableAsync(
        Fixture f,
        Guid obligationId,
        DateOnly? dueOn = null,
        string beneficiary = "Client")
    {
        RaiseReceivableResponse raised = await PostAsync<RaiseReceivableResponse>(
            f,
            $"monetary-obligations/{obligationId}/receivables",
            new RaiseReceivableRequest(
                beneficiary,
                DueOn: dueOn,
                ClientPersonId: beneficiary == "Client" ? f.ClientPersonId : null,
                RepresentationId: beneficiary == "Client" ? f.RepresentationId : null,
                Reference: $"AR-{Guid.NewGuid():N}"[..12]));

        Assert.NotEqual(Guid.Empty, raised.ReceivableId);

        return raised.ReceivableId;
    }

    private static Task<MonetaryObligationResponse> ObligationAsync(Fixture f, Guid id) =>
        GetAsync<MonetaryObligationResponse>(f.Client, $"{f.Root}/monetary-obligations/{id}");

    private static Task<ReceivableResponse> ReceivableAsync(Fixture f, Guid id) =>
        GetAsync<ReceivableResponse>(f.Client, $"{f.Root}/receivables/{id}");

    private static async Task<T> PostAsync<T>(
        Fixture f,
        string path,
        object request,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using HttpResponseMessage response =
            await f.Client.PostAsJsonAsync($"{f.Root}/{path}", request);

        Assert.Equal(expected, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> KeyedAsync<T>(
        Fixture f,
        string path,
        object request,
        string key,
        HttpStatusCode expected)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, $"{f.Root}/{path}")
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(expected, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> CreatedOfferAsync(
        HttpClient client,
        string root,
        Guid dealId,
        int expectedVersion)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{root}/deals/{dealId}/offers",
            new RecordOfferRequest(
                "Inbound",
                [
                    new OfferTermRequest(
                        "GuaranteedCompensation",
                        new TermValueRequest("Money", Amount: 1_000_000m, Currency: "USD")),
                ],
                expectedVersion));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordOfferResponse>())!.OfferId;
    }

    private static async Task<Guid> AddPartyAsync(
        HttpClient client,
        string root,
        Guid contractId,
        AddContractPartyRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{root}/contracts/{contractId}/parties", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AddContractPartyResponse>())!
            .ContractPartyId;
    }

    private sealed record CreatedId(Guid Id);

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<IReadOnlyList<T>> ListAsync<T>(HttpClient client, string uri) =>
        await GetAsync<T[]>(client, uri);
}
