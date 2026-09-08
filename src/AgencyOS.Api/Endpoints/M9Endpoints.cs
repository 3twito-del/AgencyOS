using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Finance;
using AgencyOS.Contracts.Finance;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M9 finance surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint policies are an early gate only. The authoritative tenant-scoped check
/// lives in <see cref="FinanceQueryService"/> and the handlers, and finance
/// <strong>refuses rather than redacts</strong>: an arithmetic report with rows
/// removed is a wrong answer presented as a right one, so a caller either sees the
/// whole thing or is turned away (ADR-0007, ADR-0023).
/// </para>
/// <para>
/// Nothing here sends an invoice, chases a payer or touches a bank. Every verb says
/// record, issue or allocate, because those are the acts AgencyOS actually
/// performs. Communications are M10's.
/// </para>
/// </remarks>
internal static class M9Endpoints
{
    public static void MapFinance(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapObligations(tenant);
        MapReceivables(tenant);
        MapInvoices(tenant);
        MapPayments(tenant);
        MapCommissions(tenant);
        MapLedger(tenant);
        MapFinanceViews(tenant);
    }

    // ------------------------------------------------------------ obligations

    private static void MapObligations(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/monetary-obligations", async (
                Guid organizationId,
                FinanceQueryService queries,
                Guid? contractId,
                bool? unbilledOnly,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<MonetaryObligationModel> obligations = await queries
                    .ListObligationsAsync(
                        new OrganizationId(organizationId),
                        contractId is { } contract ? new ContractId(contract) : null,
                        unbilledOnly ?? false,
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(obligations.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("ListMonetaryObligations");

        tenant.MapGet("/monetary-obligations/{obligationId:guid}", async (
                Guid organizationId,
                Guid obligationId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                MonetaryObligationModel? obligation = await queries
                    .GetObligationAsync(
                        new OrganizationId(organizationId),
                        new MonetaryObligationId(obligationId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return obligation is null ? Results.NotFound() : Results.Ok(Map(obligation));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("GetMonetaryObligation");

        // Refuses a contract that is not operative. Agreed commercial terms are not
        // a collectible legal amount (ADR-0023).
        tenant.MapPost("/contracts/{contractId:guid}/monetary-obligations", async (
                Guid organizationId,
                Guid contractId,
                RecordMonetaryObligationRequest request,
                MonetaryObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordMonetaryObligationCommand command = new(
                    new OrganizationId(organizationId),
                    new ContractId(contractId),
                    new ContractVersionId(request.ContractVersionId),
                    request.PayerPartyId,
                    request.PayeePartyId,
                    EndpointParsing.ParseEnum<ObligationCategory>(
                        request.Category, nameof(request.Category)),
                    EndpointParsing.ParseEnum<ObligationAmountKind>(
                        request.AmountKind, nameof(request.AmountKind)),
                    ParseDue(request.Due),
                    ParseMoney(request.Amount),
                    request.Quantity,
                    ParseMoney(request.UnitAmount),
                    EndpointParsing.ParseNullableEnum<TermUnit>(request.Unit, nameof(request.Unit)),
                    request.Condition,
                    request.AnchorDate,
                    request.SourceObligationId is { } source
                        ? new ObligationId(source)
                        : null,
                    EndpointParsing.ParseNullableEnum<ContractTermCode>(
                        request.SourceTermCode, nameof(request.SourceTermCode)),
                    request.Description,
                    request.Notes);

                MonetaryObligationId id = await handler.HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.MonetaryObligationsRecorded.Add(
                    1,
                    new KeyValuePair<string, object?>("category", command.Category.ToString()),
                    new KeyValuePair<string, object?>("amount_kind", command.AmountKind.ToString()));

                return Results.Ok(new RecordMonetaryObligationResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("RecordMonetaryObligation");

        tenant.MapPost("/monetary-obligations/{obligationId:guid}/quantify", async (
                Guid organizationId,
                Guid obligationId,
                QuantifyObligationRequest request,
                MonetaryObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new QuantifyObligationCommand(
                            new OrganizationId(organizationId),
                            new MonetaryObligationId(obligationId),
                            RequireMoney(request.Amount),
                            request.ExpectedVersion,
                            request.Reason),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("QuantifyMonetaryObligation");

        tenant.MapPost("/monetary-obligations/{obligationId:guid}/release", async (
                Guid organizationId,
                Guid obligationId,
                ReleaseObligationRequest request,
                MonetaryObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ReleaseObligationCommand(
                            new OrganizationId(organizationId),
                            new MonetaryObligationId(obligationId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("ReleaseMonetaryObligation");
    }

    // ------------------------------------------------------------ receivables

    private static void MapReceivables(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/receivables", async (
                Guid organizationId,
                FinanceQueryService queries,
                string? status,
                Guid? contractId,
                Guid? payerPartyId,
                Guid? clientPersonId,
                string? beneficiary,
                bool? overdueOnly,
                bool? unreconciledOnly,
                DateOnly? dueAfter,
                DateOnly? dueBefore,
                string? currency,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ReceivableFilter filter = new(
                    EndpointParsing.ParseNullableEnum<ReceivableStatus>(status, nameof(status)),
                    contractId is { } contract ? new ContractId(contract) : null,
                    payerPartyId,
                    clientPersonId,
                    EndpointParsing.ParseNullableEnum<ReceivableBeneficiary>(
                        beneficiary, nameof(beneficiary)),
                    overdueOnly ?? false,
                    unreconciledOnly ?? false,
                    dueAfter,
                    dueBefore,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<ReceivableModel> receivables = await queries
                    .ListReceivablesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(receivables.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("ListReceivables");

        tenant.MapGet("/receivables/{receivableId:guid}", async (
                Guid organizationId,
                Guid receivableId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ReceivableModel? receivable = await queries
                    .GetReceivableAsync(
                        new OrganizationId(organizationId),
                        new ReceivableId(receivableId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return receivable is null ? Results.NotFound() : Results.Ok(Map(receivable));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("GetReceivable");

        tenant.MapPost("/monetary-obligations/{obligationId:guid}/receivables", async (
                Guid organizationId,
                Guid obligationId,
                RaiseReceivableRequest request,
                ReceivableHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ReceivableBeneficiary beneficiary =
                    EndpointParsing.ParseEnum<ReceivableBeneficiary>(
                        request.Beneficiary, nameof(request.Beneficiary));

                ReceivableId id = await handler.HandleAsync(
                        new RaiseReceivableCommand(
                            new OrganizationId(organizationId),
                            new MonetaryObligationId(obligationId),
                            beneficiary,
                            ParseMoney(request.Amount),
                            request.DueOn,
                            request.ClientPersonId,
                            request.RepresentationId,
                            request.Reference,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ReceivablesRaised.Add(
                    1, new KeyValuePair<string, object?>("beneficiary", beneficiary.ToString()));

                return Results.Ok(new RaiseReceivableResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("RaiseReceivable");

        // A financial act with a reason and a posting, never a row disappearing.
        tenant.MapPost("/receivables/{receivableId:guid}/write-off", async (
                Guid organizationId,
                Guid receivableId,
                WriteOffReceivableRequest request,
                ReceivableHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new WriteOffReceivableCommand(
                            new OrganizationId(organizationId),
                            new ReceivableId(receivableId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ReceivablesWrittenOff.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceAdjustmentsWrite))
            .WithName("WriteOffReceivable");

        tenant.MapPost("/receivables/{receivableId:guid}/cancel", async (
                Guid organizationId,
                Guid receivableId,
                CancelReceivableRequest request,
                ReceivableHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new CancelReceivableCommand(
                            new OrganizationId(organizationId),
                            new ReceivableId(receivableId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("CancelReceivable");

        // A deduction is a fact somebody recorded. AgencyOS infers none, and
        // implements no tax engine (ADR-0023).
        tenant.MapPost("/receivables/{receivableId:guid}/adjustments", async (
                Guid organizationId,
                Guid receivableId,
                RecordAdjustmentRequest request,
                ReceivableHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                PaymentAdjustmentKind kind = EndpointParsing.ParseEnum<PaymentAdjustmentKind>(
                    request.Kind, nameof(request.Kind));

                PaymentAdjustmentId id = await handler.HandleAsync(
                        new RecordAdjustmentCommand(
                            new OrganizationId(organizationId),
                            new ReceivableId(receivableId),
                            kind,
                            RequireMoney(request.Amount),
                            request.Description,
                            request.OccurredOn,
                            request.ExpectedVersion,
                            request.PaymentId is { } payment ? new PaymentId(payment) : null,
                            request.ExternalReference),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.AdjustmentsRecorded.Add(
                    1, new KeyValuePair<string, object?>("kind", kind.ToString()));

                return Results.Ok(new RecordAdjustmentResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceAdjustmentsWrite))
            .WithName("RecordPaymentAdjustment");

        tenant.MapGet("/receivables/{receivableId:guid}/reconciliation", async (
                Guid organizationId,
                Guid receivableId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.finance.reconcile");

                Application.Finance.ReconciliationModel? reconciliation = await queries
                    .ReconcileAsync(
                        new OrganizationId(organizationId),
                        new ReceivableId(receivableId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (reconciliation is null)
                {
                    return Results.NotFound();
                }

                AgencyOsTelemetry.FinanceReconciliations.Add(
                    1, new KeyValuePair<string, object?>("outcome", reconciliation.Outcome));

                return Results.Ok(Map(reconciliation));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("ReconcileReceivable");
    }

    // --------------------------------------------------------------- invoices

    private static void MapInvoices(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/invoices", async (
                Guid organizationId,
                FinanceQueryService queries,
                string? status,
                Guid? contractId,
                Guid? debtorPartyId,
                bool? overdueOnly,
                DateOnly? dueAfter,
                DateOnly? dueBefore,
                string? currency,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                InvoiceFilter filter = new(
                    EndpointParsing.ParseNullableEnum<InvoiceStatus>(status, nameof(status)),
                    contractId is { } contract ? new ContractId(contract) : null,
                    debtorPartyId,
                    overdueOnly ?? false,
                    dueAfter,
                    dueBefore,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<InvoiceModel> invoices = await queries
                    .ListInvoicesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(invoices.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("ListInvoices");

        tenant.MapGet("/invoices/{invoiceId:guid}", async (
                Guid organizationId,
                Guid invoiceId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                InvoiceModel? invoice = await queries
                    .GetInvoiceAsync(
                        new OrganizationId(organizationId), new InvoiceId(invoiceId), cancellationToken)
                    .ConfigureAwait(false);

                return invoice is null ? Results.NotFound() : Results.Ok(Map(invoice));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("GetInvoice");

        // Records an invoice. It does not send one: there is no transport, no email
        // and no attachment anywhere in M9 (ADR-0023).
        tenant.MapPost("/contracts/{contractId:guid}/invoices", async (
                Guid organizationId,
                Guid contractId,
                RecordInvoiceRequest request,
                InvoiceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                InvoiceId id = await handler.HandleAsync(
                        new RecordInvoiceCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            request.DebtorPartyId,
                            request.Currency,
                            [
                                .. request.Lines.Select(line => new InvoiceLineInput(
                                    new ReceivableId(line.ReceivableId),
                                    RequireMoney(line.Amount),
                                    line.Description)),
                            ],
                            request.Reference,
                            request.DueOn,
                            request.ExternalReference,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.InvoicesRecorded.Add(1);

                return Results.Ok(new RecordInvoiceResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("RecordInvoice");

        tenant.MapPost("/invoices/{invoiceId:guid}/issue", async (
                Guid organizationId,
                Guid invoiceId,
                IssueInvoiceRequest request,
                InvoiceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new IssueInvoiceCommand(
                            new OrganizationId(organizationId),
                            new InvoiceId(invoiceId),
                            request.IssuedOn,
                            request.ExpectedVersion,
                            request.Reference),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.InvoicesIssued.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("IssueInvoice");

        tenant.MapPost("/invoices/{invoiceId:guid}/void", async (
                Guid organizationId,
                Guid invoiceId,
                VoidInvoiceRequest request,
                InvoiceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new VoidInvoiceCommand(
                            new OrganizationId(organizationId),
                            new InvoiceId(invoiceId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceWrite))
            .WithName("VoidInvoice");
    }

    // --------------------------------------------------------------- payments

    private static void MapPayments(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/payments", async (
                Guid organizationId,
                FinanceQueryService queries,
                string? direction,
                string? status,
                Guid? payerPartyId,
                bool? unappliedOnly,
                DateOnly? recordedAfter,
                DateOnly? recordedBefore,
                string? currency,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                PaymentFilter filter = new(
                    EndpointParsing.ParseNullableEnum<PaymentDirection>(direction, nameof(direction)),
                    EndpointParsing.ParseNullableEnum<PaymentStatus>(status, nameof(status)),
                    payerPartyId,
                    unappliedOnly ?? false,
                    recordedAfter,
                    recordedBefore,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<PaymentModel> payments = await queries
                    .ListPaymentsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(payments.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsRead))
            .WithName("ListPayments");

        tenant.MapGet("/payments/{paymentId:guid}", async (
                Guid organizationId,
                Guid paymentId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                PaymentModel? payment = await queries
                    .GetPaymentAsync(
                        new OrganizationId(organizationId), new PaymentId(paymentId), cancellationToken)
                    .ConfigureAwait(false);

                return payment is null ? Results.NotFound() : Results.Ok(Map(payment));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsRead))
            .WithName("GetPayment");

        // Anything not allocated stays unapplied and is reported back. Nothing is
        // auto-matched (ADR-0023).
        tenant.MapPost("/payments", async (
                Guid organizationId,
                RecordPaymentRequest request,
                PaymentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                PaymentDirection direction = EndpointParsing.ParseEnum<PaymentDirection>(
                    request.Direction, nameof(request.Direction));

                Application.Finance.RecordPaymentResult result = await handler.HandleAsync(
                        new RecordPaymentCommand(
                            new OrganizationId(organizationId),
                            direction,
                            RequireMoney(request.Amount),
                            request.ReceivedOn,
                            EndpointParsing.ParseEnum<PaymentMethod>(
                                request.Method, nameof(request.Method)),
                            request.PayerPartyId,
                            request.PayerName,
                            request.PayeePartyId,
                            request.PayeeName,
                            request.ExternalReference,
                            request.SourceSystem,
                            request.Notes,
                            ParseAllocations(request.Allocations)),
                        cancellationToken)
                    .ConfigureAwait(false);

                // Identifiers, currency and shape. Never the amount: telemetry is
                // exported to places holding no finance permission (ADR-0023).
                AgencyOsTelemetry.PaymentsRecorded.Add(
                    1,
                    new KeyValuePair<string, object?>("direction", direction.ToString()),
                    new KeyValuePair<string, object?>("currency", request.Amount.Currency),
                    new KeyValuePair<string, object?>(
                        "fully_applied", result.Unapplied.Amount == 0m));

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/payments/{result.PaymentId.Value}",
                    Map(result));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsWrite))
            .WithName("RecordPayment");

        tenant.MapPost("/payments/{paymentId:guid}/allocations", async (
                Guid organizationId,
                Guid paymentId,
                AllocatePaymentRequest request,
                PaymentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Application.Finance.RecordPaymentResult result = await handler.HandleAsync(
                        new AllocatePaymentCommand(
                            new OrganizationId(organizationId),
                            new PaymentId(paymentId),
                            ParseAllocations(request.Allocations) ?? [],
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.AllocationsRecorded.Add(request.Allocations.Count);

                return Results.Ok(Map(result));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsWrite))
            .WithName("AllocatePayment");

        tenant.MapPost("/payments/{paymentId:guid}/allocations/reverse", async (
                Guid organizationId,
                Guid paymentId,
                ReverseAllocationRequest request,
                PaymentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ReverseAllocationCommand(
                            new OrganizationId(organizationId),
                            new PaymentId(paymentId),
                            new PaymentAllocationId(request.AllocationId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.AllocationsReversed.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsWrite))
            .WithName("ReverseAllocation");

        // The original keeps its amount, currency and date. Those are what somebody
        // observed, and observations are not edited (ADR-0023).
        tenant.MapPost("/payments/{paymentId:guid}/reverse", async (
                Guid organizationId,
                Guid paymentId,
                ReversePaymentRequest request,
                PaymentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                PaymentId reversal = await handler.HandleAsync(
                        new ReversePaymentCommand(
                            new OrganizationId(organizationId),
                            new PaymentId(paymentId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.PaymentsReversed.Add(1);

                return Results.Ok(new ReversePaymentResponse(reversal.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinancePaymentsWrite))
            .WithName("ReversePayment");
    }

    // ------------------------------------------------------------ commissions

    private static void MapCommissions(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/commission-rules", async (
                Guid organizationId,
                FinanceQueryService queries,
                Guid? clientPersonId,
                Guid? contractId,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CommissionRuleModel> rules = await queries
                    .ListCommissionRulesAsync(
                        new OrganizationId(organizationId),
                        clientPersonId,
                        contractId is { } contract ? new ContractId(contract) : null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(rules.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsRead))
            .WithName("ListCommissionRules");

        tenant.MapPost("/commission-rules", async (
                Guid organizationId,
                CreateCommissionRuleRequest request,
                CommissionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                CommissionRuleId id = await handler.HandleAsync(
                        new CreateCommissionRuleCommand(
                            new OrganizationId(organizationId),
                            request.RepresentationId,
                            request.ClientPersonId,
                            EndpointParsing.ParseEnum<CommissionBasisKind>(
                                request.Basis, nameof(request.Basis)),
                            request.EffectiveFrom,
                            request.RatePercent,
                            ParseMoney(request.FixedAmount),
                            EndpointParsing.ParseNullableEnum<ContractTermCode>(
                                request.TermCode, nameof(request.TermCode)),
                            request.ContractId is { } contract ? new ContractId(contract) : null,
                            request.EffectiveTo,
                            request.Provenance,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new CreateCommissionRuleResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsWrite))
            .WithName("CreateCommissionRule");

        tenant.MapPost("/commission-rules/{ruleId:guid}/end", async (
                Guid organizationId,
                Guid ruleId,
                EndCommissionRuleRequest request,
                CommissionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new EndCommissionRuleCommand(
                            new OrganizationId(organizationId),
                            new CommissionRuleId(ruleId),
                            request.EndsOn,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsWrite))
            .WithName("EndCommissionRule");

        tenant.MapGet("/commissions", async (
                Guid organizationId,
                FinanceQueryService queries,
                Guid? clientPersonId,
                Guid? contractId,
                Guid? representationId,
                string? status,
                bool? outstandingOnly,
                string? currency,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                CommissionFilter filter = new(
                    clientPersonId,
                    contractId is { } contract ? new ContractId(contract) : null,
                    representationId,
                    EndpointParsing.ParseNullableEnum<CommissionEntitlementStatus>(
                        status, nameof(status)),
                    outstandingOnly ?? false,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant());

                IReadOnlyList<CommissionEntitlementModel> commissions = await queries
                    .ListCommissionsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(commissions.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsRead))
            .WithName("ListCommissions");

        tenant.MapGet("/commissions/{commissionId:guid}", async (
                Guid organizationId,
                Guid commissionId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CommissionEntitlementModel? commission = await queries
                    .GetCommissionAsync(
                        new OrganizationId(organizationId),
                        new CommissionEntitlementId(commissionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return commission is null ? Results.NotFound() : Results.Ok(Map(commission));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsRead))
            .WithName("GetCommission");

        tenant.MapPost("/monetary-obligations/{obligationId:guid}/commission", async (
                Guid organizationId,
                Guid obligationId,
                CalculateCommissionRequest request,
                CommissionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.finance.commission");

                CommissionEntitlementId id = await handler.HandleAsync(
                        new CalculateCommissionCommand(
                            new OrganizationId(organizationId),
                            new MonetaryObligationId(obligationId),
                            request.ClientPersonId,
                            request.RepresentationId,
                            request.GoverningOn,
                            request.ClientReceivableId is { } receivable
                                ? new ReceivableId(receivable)
                                : null,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.CommissionsCalculated.Add(1);

                return Results.Ok(new CalculateCommissionResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceCommissionsWrite))
            .WithName("CalculateCommission");

        tenant.MapPost("/commissions/{commissionId:guid}/adjustments", async (
                Guid organizationId,
                Guid commissionId,
                AdjustCommissionRequest request,
                CommissionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new AdjustCommissionCommand(
                            new OrganizationId(organizationId),
                            new CommissionEntitlementId(commissionId),
                            EndpointParsing.ParseEnum<CommissionAdjustmentKind>(
                                request.Kind, nameof(request.Kind)),
                            RequireMoney(request.Amount),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.CommissionsAdjusted.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceAdjustmentsWrite))
            .WithName("AdjustCommission");
    }

    // ----------------------------------------------------------------- ledger

    private static void MapLedger(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/ledger/accounts", async (
                Guid organizationId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AccountModel> accounts = await queries
                    .ListAccountsAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(accounts.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerRead))
            .WithName("ListLedgerAccounts");

        // Per currency, never summed across them (ADR-0023).
        tenant.MapGet("/ledger/balances", async (
                Guid organizationId,
                FinanceQueryService queries,
                string? currency,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AccountBalanceModel> balances = await queries
                    .GetBalancesAsync(
                        new OrganizationId(organizationId),
                        string.IsNullOrWhiteSpace(currency)
                            ? null
                            : currency.Trim().ToUpperInvariant(),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(balances.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerRead))
            .WithName("GetLedgerBalances");

        tenant.MapGet("/ledger/entries", async (
                Guid organizationId,
                FinanceQueryService queries,
                string? status,
                string? source,
                Guid? accountId,
                DateOnly? postedAfter,
                DateOnly? postedBefore,
                string? currency,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                JournalFilter filter = new(
                    EndpointParsing.ParseNullableEnum<JournalEntryStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<JournalSource>(source, nameof(source)),
                    accountId is { } account ? new AccountId(account) : null,
                    postedAfter,
                    postedBefore,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant());

                IReadOnlyList<JournalEntryModel> entries = await queries
                    .ListJournalEntriesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(entries.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerRead))
            .WithName("ListJournalEntries");

        tenant.MapGet("/ledger/entries/{entryId:guid}", async (
                Guid organizationId,
                Guid entryId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                JournalEntryModel? entry = await queries
                    .GetJournalEntryAsync(
                        new OrganizationId(organizationId),
                        new JournalEntryId(entryId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return entry is null ? Results.NotFound() : Results.Ok(Map(entry));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerRead))
            .WithName("GetJournalEntry");

        // The narrowest surface in the milestone. Every other posting is a
        // consequence of a business act; this is the one route by which a person
        // writes an entry directly (ADR-0023).
        tenant.MapPost("/ledger/entries", async (
                Guid organizationId,
                PostJournalEntryRequest request,
                LedgerHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                JournalEntryId id = await handler.HandleAsync(
                        new PostJournalEntryCommand(
                            new OrganizationId(organizationId),
                            request.Memo,
                            request.Currency,
                            request.OccurredOn,
                            [
                                .. request.Lines.Select(line => new JournalLineInputCommand(
                                    EndpointParsing.ParseEnum<SystemAccount>(
                                        line.Account, nameof(line.Account)),
                                    EndpointParsing.ParseEnum<JournalSide>(
                                        line.Side, nameof(line.Side)),
                                    RequireMoney(line.Amount),
                                    line.Memo)),
                            ],
                            request.PostingDate),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.JournalEntriesPosted.Add(
                    1, new KeyValuePair<string, object?>("source", nameof(JournalSource.ManualAdjustment)));

                return Results.Ok(new PostJournalEntryResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerPost))
            .WithName("PostJournalEntry");

        // A posted entry is never edited. A correction is another entry saying the
        // opposite, and both stay readable.
        tenant.MapPost("/ledger/entries/{entryId:guid}/reverse", async (
                Guid organizationId,
                Guid entryId,
                ReverseJournalEntryRequest request,
                LedgerHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                JournalEntryId reversal = await handler.HandleAsync(
                        new ReverseJournalEntryCommand(
                            new OrganizationId(organizationId),
                            new JournalEntryId(entryId),
                            request.Reason,
                            request.PostingDate),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.JournalEntriesReversed.Add(1);

                return Results.Ok(new ReverseJournalEntryResponse(reversal.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceLedgerPost))
            .WithName("ReverseJournalEntry");
    }

    // ------------------------------------------------------------------ views

    private static void MapFinanceViews(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/finance/history", async (
                Guid organizationId,
                FinanceQueryService queries,
                Guid? contractId,
                Guid? receivableId,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<FinanceHistoryEntryModel> history = await queries
                    .GetHistoryAsync(
                        new OrganizationId(organizationId),
                        contractId is { } contract ? new ContractId(contract) : null,
                        receivableId is { } receivable ? new ReceivableId(receivable) : null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("GetFinanceHistory");

        tenant.MapGet("/finance/command-center", async (
                Guid organizationId,
                FinanceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.finance.command_center");

                FinanceCommandCenterModel centre = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(centre));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.FinanceRead))
            .WithName("GetFinanceCommandCenter");
    }

    // ---------------------------------------------------------------- parsing

    /// <summary>
    /// Reads a money value off the wire.
    /// </summary>
    /// <remarks>
    /// Goes through the M7 <see cref="Money"/> type, so the currency is validated
    /// against the same table and the precision against the same minor units. A
    /// second money parser would be a second set of rounding rules (ADR-0023).
    /// </remarks>
    private static Money? ParseMoney(MoneyRequest? request) =>
        request is null ? null : Money.Create(request.Amount, request.Currency);

    private static Money RequireMoney(MoneyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Money.Create(request.Amount, request.Currency);
    }

    private static DeadlineRule ParseDue(DueRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new DeadlineRule(
            EndpointParsing.ParseEnum<DeadlineRuleKind>(request.Kind, nameof(request.Kind)),
            request.On,
            EndpointParsing.ParseNullableEnum<DeadlineAnchor>(request.Anchor, nameof(request.Anchor)),
            request.Offset,
            EndpointParsing.ParseNullableEnum<DeadlineOffsetUnit>(request.Unit, nameof(request.Unit)),
            request.Before,
            EndpointParsing.ParseEnumOrDefault(
                request.Basis, nameof(request.Basis), DeadlineCalendarBasis.CalendarDays),
            request.Description).Validated();
    }

    private static IReadOnlyList<AllocationInputCommand>? ParseAllocations(
        IReadOnlyList<AllocationRequest>? allocations) =>
        allocations is null
            ? null
            : [
                .. allocations.Select(line => new AllocationInputCommand(
                    new ReceivableId(line.ReceivableId), RequireMoney(line.Amount), line.Notes)),
            ];

    // --------------------------------------------------------------- mapping

    private static MoneyResponse Map(MoneyModel money) => new(money.Amount, money.Currency);

    private static MoneyResponse Map(Money money) =>
        new(money.Amount, money.Currency.Value);

    private static MonetaryObligationResponse Map(MonetaryObligationModel obligation) =>
        new(
            obligation.Id.Value,
            obligation.ContractId.Value,
            obligation.ContractTitle,
            obligation.ContractVersionId.Value,
            obligation.SourceObligationId?.Value,
            obligation.SourceTermCode?.ToString(),
            obligation.PayerPartyId,
            obligation.PayerDisplayName,
            obligation.PayeePartyId,
            obligation.PayeeDisplayName,
            obligation.Category.ToString(),
            obligation.AmountKind.ToString(),
            obligation.Amount is { } amount ? Map(amount) : null,
            obligation.Quantity,
            obligation.UnitAmount is { } unit ? Map(unit) : null,
            obligation.Condition,
            obligation.DueOn,
            obligation.DueUnresolvedReason,
            obligation.DueDescription,
            obligation.Status.ToString(),
            obligation.IsQuantified,
            obligation.HasReceivable,
            obligation.Description,
            obligation.Notes,
            obligation.Version);

    /// <summary>Shared with the saved-view surface, which returns the same shape.</summary>
    internal static ReceivableResponse MapReceivable(ReceivableModel receivable) => Map(receivable);

    private static ReceivableResponse Map(ReceivableModel receivable) =>
        new(
            receivable.Id.Value,
            receivable.MonetaryObligationId.Value,
            receivable.ContractId.Value,
            receivable.ContractTitle,
            receivable.PayerPartyId,
            receivable.PayerDisplayName,
            receivable.Beneficiary.ToString(),
            receivable.ClientPersonId,
            receivable.ClientDisplayName,
            Map(receivable.OriginalAmount),
            Map(receivable.Allocated),
            Map(receivable.Adjusted),
            Map(receivable.Outstanding),
            receivable.DueOn,
            receivable.Status.ToString(),
            receivable.IsOverdue,
            receivable.Reference,
            receivable.ClosureReason,
            receivable.Notes,
            receivable.CreatedAt,
            receivable.Version);

    /// <summary>Shared with the saved-view surface.</summary>
    internal static InvoiceResponse MapInvoice(InvoiceModel invoice) => Map(invoice);

    private static InvoiceResponse Map(InvoiceModel invoice) =>
        new(
            invoice.Id.Value,
            invoice.Reference,
            invoice.ContractId.Value,
            invoice.ContractTitle,
            invoice.DebtorPartyId,
            invoice.DebtorDisplayName,
            invoice.Status.ToString(),
            invoice.IssuedOn,
            invoice.DueOn,
            Map(invoice.Total),
            Map(invoice.Outstanding),
            invoice.IsOverdue,

            // Stated rather than assumed. M9 records that an invoice exists and
            // where it lives; the document is M10's (ADR-0023).
            invoice.HoldsDocument,

            invoice.ExternalReference,
            invoice.VoidReason,
            invoice.Notes,
            [
                .. invoice.Lines.Select(line => new InvoiceLineResponse(
                    line.Id,
                    line.ReceivableId.Value,
                    Map(line.Amount),
                    line.Description,
                    line.Sequence)),
            ],
            invoice.CreatedAt,
            invoice.Version);

    /// <summary>Shared with the saved-view surface.</summary>
    internal static PaymentResponse MapPayment(PaymentModel payment) => Map(payment);

    private static PaymentResponse Map(PaymentModel payment) =>
        new(
            payment.Id.Value,
            payment.Direction.ToString(),
            payment.PayerPartyId,
            payment.PayerDisplayName,
            payment.PayeePartyId,
            payment.PayeeDisplayName,
            Map(payment.Amount),
            Map(payment.Allocated),
            Map(payment.Unapplied),
            payment.ReceivedOn,
            payment.RecordedAt,
            payment.Method.ToString(),
            payment.ExternalReference,
            payment.SourceSystem,
            payment.Status.ToString(),
            payment.ReversedByPaymentId?.Value,
            payment.ReversalOfPaymentId?.Value,
            payment.ReversalReason,
            payment.RecordedByDisplayName,
            payment.Notes,
            [.. payment.Allocations.Select(Map)],
            payment.Version);

    private static PaymentAllocationResponse Map(PaymentAllocationModel allocation) =>
        new(
            allocation.Id.Value,
            allocation.PaymentId.Value,
            allocation.ReceivableId.Value,
            allocation.ReceivableReference,
            allocation.ContractTitle,
            Map(allocation.Amount),
            allocation.IsApplied,
            allocation.AppliedAt,
            allocation.AppliedByDisplayName,
            allocation.ReversedAt,
            allocation.ReversalReason);

    private static PaymentAdjustmentResponse Map(PaymentAdjustmentModel adjustment) =>
        new(
            adjustment.Id.Value,
            adjustment.ReceivableId.Value,
            adjustment.PaymentId?.Value,
            adjustment.Kind.ToString(),
            Map(adjustment.Amount),
            adjustment.Description,
            adjustment.ExternalReference,
            adjustment.OccurredOn,
            adjustment.IsApplied,
            adjustment.RecordedByDisplayName);

    private static CommissionRuleResponse Map(CommissionRuleModel rule) =>
        new(
            rule.Id.Value,
            rule.RepresentationId,
            rule.ClientPersonId,
            rule.ClientDisplayName,
            rule.ContractId?.Value,
            rule.ContractTitle,
            rule.Basis.ToString(),
            rule.RatePercent,
            rule.FixedAmount is { } amount ? Map(amount) : null,
            rule.TermCode?.ToString(),
            rule.EffectiveFrom,
            rule.EffectiveTo,
            rule.IsInForceToday,
            rule.Provenance,
            rule.Notes,
            rule.Version);

    private static CommissionEntitlementResponse Map(CommissionEntitlementModel commission) =>
        new(
            commission.Id.Value,
            commission.MonetaryObligationId.Value,
            commission.ContractId.Value,
            commission.ContractTitle,
            commission.ClientPersonId,
            commission.ClientDisplayName,
            commission.RepresentationId,
            commission.CommissionRuleId.Value,
            commission.ClientReceivableId?.Value,
            commission.Basis.ToString(),
            commission.RatePercentSnapshot,
            Map(commission.BasisAmount),
            Map(commission.Entitled),
            Map(commission.Collected),
            Map(commission.Adjusted),
            Map(commission.Outstanding),
            commission.GoverningOn,
            commission.Status.ToString(),
            [
                .. commission.Adjustments.Select(adjustment => new CommissionAdjustmentResponse(
                    adjustment.Id,
                    adjustment.Kind.ToString(),
                    Map(adjustment.Amount),
                    adjustment.Reason,
                    adjustment.RecordedAt,
                    adjustment.RecordedByDisplayName)),
            ],
            commission.CalculatedAt,
            commission.CalculatedByDisplayName,
            commission.Notes,
            commission.Version);

    private static AccountResponse Map(AccountModel account) =>
        new(
            account.Id.Value,
            account.Kind.ToString(),
            account.Category.ToString(),
            account.Code,
            account.Name,
            account.Description);

    private static AccountBalanceResponse Map(AccountBalanceModel balance) =>
        new(
            balance.AccountId.Value,
            balance.Kind.ToString(),
            balance.Code,
            balance.Name,
            balance.Category.ToString(),
            balance.Currency,
            Map(balance.Debits),
            Map(balance.Credits),
            Map(balance.Balance));

    private static JournalEntryResponse Map(JournalEntryModel entry) =>
        new(
            entry.Id.Value,
            entry.Status.ToString(),
            entry.Source.ToString(),
            entry.Memo,
            entry.Currency,
            Map(entry.Debits),
            Map(entry.Credits),
            entry.IsBalanced,
            entry.OccurredOn,
            entry.PostingDate,
            entry.RecordedAt,
            entry.PostedAt,
            entry.PostedByDisplayName,
            entry.ReceivableId?.Value,
            entry.PaymentId?.Value,
            entry.CommissionEntitlementId?.Value,
            entry.ReversalOfEntryId?.Value,
            entry.ReversedByEntryId?.Value,
            entry.ReversalReason,
            [
                .. entry.Lines.Select(line => new JournalLineResponse(
                    line.Id,
                    line.AccountId.Value,
                    line.AccountCode,
                    line.AccountName,
                    line.Side.ToString(),
                    Map(line.Amount),
                    line.Sequence,
                    line.Memo)),
            ],
            entry.Version);

    private static ReceivableReconciliationResponse Map(Application.Finance.ReconciliationModel model) =>
        new(
            model.ReceivableId.Value,
            model.Reference,
            model.ContractId.Value,
            model.ContractTitle,
            Map(model.Expected),
            Map(model.Allocated),
            Map(model.Deductions),
            Map(model.WrittenOff),
            Map(model.Variance),
            model.VarianceDirection,
            model.Outcome,
            model.Explanation,
            [.. model.Adjustments.Select(Map)],
            [.. model.Allocations.Select(Map)]);

    private static FinanceTaskResponse Map(FinanceTaskModel task) =>
        new(
            task.Id,
            task.Title,
            task.State,
            task.Priority,
            task.DueAt,
            task.ReceivableId?.Value,
            task.InvoiceId?.Value,
            task.PaymentId?.Value);

    private static FinanceHistoryEntryResponse Map(FinanceHistoryEntryModel entry) =>
        new(
            entry.OccurredAt,
            entry.Kind,
            entry.Summary,
            entry.Amount is { } amount ? Map(amount) : null,
            entry.Detail,
            entry.ActorDisplayName);

    private static CurrencyTotalResponse Map(CurrencyTotalModel total) =>
        new(total.Currency, Map(total.Total), total.Count);

    private static RecordPaymentResponse Map(Application.Finance.RecordPaymentResult result) =>
        new(
            result.PaymentId.Value,
            Map(result.Allocated),
            Map(result.Unapplied),
            [.. result.PossibleDuplicates.Select(x => x.Value)]);

    private static FinanceCommandCenterResponse Map(FinanceCommandCenterModel centre) =>
        new(
            [.. centre.Overdue.Select(Map)],
            [.. centre.DueSoon.Select(Map)],
            [.. centre.UnappliedPayments.Select(Map)],
            [.. centre.UnreconciledVariances.Select(Map)],
            [.. centre.UncollectedCommission.Select(Map)],
            [.. centre.UnbilledObligations.Select(Map)],
            [.. centre.OverdueTasks.Select(Map)],
            [.. centre.OutstandingByCurrency.Select(Map)],
            [.. centre.UnappliedByCurrency.Select(Map)],
            [.. centre.CommissionOutstandingByCurrency.Select(Map)],
            centre.OverdueCount,
            centre.UnappliedCount);
}
