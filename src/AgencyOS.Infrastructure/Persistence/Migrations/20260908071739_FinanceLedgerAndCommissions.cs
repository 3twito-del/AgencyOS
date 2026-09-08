using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceLedgerAndCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.UniqueConstraint("AK_accounts_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commission_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    fixed_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    term_code = table.Column<int>(type: "integer", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    provenance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_rules", x => x.id);
                    table.UniqueConstraint("AK_commission_rules_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "finance_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monetary_obligation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    commission_entitlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    debtor_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    void_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                    table.UniqueConstraint("AK_invoices_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_invoices_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monetary_obligations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_obligation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_term_code = table.Column<int>(type: "integer", nullable: true),
                    payer_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    amount_kind = table.Column<int>(type: "integer", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    unit_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    unit = table.Column<int>(type: "integer", nullable: true),
                    condition = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    due_kind = table.Column<int>(type: "integer", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_anchor = table.Column<int>(type: "integer", nullable: true),
                    due_offset = table.Column<int>(type: "integer", nullable: true),
                    due_unit = table.Column<int>(type: "integer", nullable: true),
                    due_before = table.Column<bool>(type: "boolean", nullable: false),
                    due_basis = table.Column<int>(type: "integer", nullable: false),
                    due_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monetary_obligations", x => x.id);
                    table.UniqueConstraint("AK_monetary_obligations_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_monetary_obligations_contract_versions_organization_id_cont~",
                        columns: x => new { x.organization_id, x.contract_version_id },
                        principalTable: "contract_versions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monetary_obligations_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    payer_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payer_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    payee_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payee_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reversed_by_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_of_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.UniqueConstraint("AK_payments_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "commission_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monetary_obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commission_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    rate_percent_snapshot = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    basis_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    entitled_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    governing_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_entitlements", x => x.id);
                    table.UniqueConstraint("AK_commission_entitlements_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_commission_entitlements_commission_rules_organization_id_co~",
                        columns: x => new { x.organization_id, x.commission_rule_id },
                        principalTable: "commission_rules",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commission_entitlements_monetary_obligations_organization_i~",
                        columns: x => new { x.organization_id, x.monetary_obligation_id },
                        principalTable: "monetary_obligations",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monetary_obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary = table.Column<int>(type: "integer", nullable: false),
                    client_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    allocated_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    adjusted_amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_closed_by_act = table.Column<bool>(type: "boolean", nullable: false),
                    is_write_off = table.Column<bool>(type: "boolean", nullable: false),
                    closure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receivables", x => x.id);
                    table.UniqueConstraint("AK_receivables_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_receivables_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receivables_monetary_obligations_organization_id_monetary_o~",
                        columns: x => new { x.organization_id, x.monetary_obligation_id },
                        principalTable: "monetary_obligations",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commission_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commission_entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_applied = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "FK_commission_adjustments_commission_entitlements_commission_e~",
                        column: x => x.commission_entitlement_id,
                        principalTable: "commission_entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_task_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_task_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_task_links_receivables_organization_id_receivable_id",
                        columns: x => new { x.organization_id, x.receivable_id },
                        principalTable: "receivables",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_task_links_tasks_organization_id_task_item_id",
                        columns: x => new { x.organization_id, x.task_item_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invoice_lines_receivables_organization_id_receivable_id",
                        columns: x => new { x.organization_id, x.receivable_id },
                        principalTable: "receivables",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    is_applied = table.Column<bool>(type: "boolean", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_adjustments", x => x.id);
                    table.UniqueConstraint("AK_payment_adjustments_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_adjustments_receivables_organization_id_receivable_~",
                        columns: x => new { x.organization_id, x.receivable_id },
                        principalTable: "receivables",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_applied = table.Column<bool>(type: "boolean", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_allocations", x => x.id);
                    table.UniqueConstraint("AK_payment_allocations_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_allocations_receivables_organization_id_receivable_~",
                        columns: x => new { x.organization_id, x.receivable_id },
                        principalTable: "receivables",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    commission_entitlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_of_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_by_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                    table.UniqueConstraint("AK_journal_entries_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_journal_entries_commission_entitlements_organization_id_com~",
                        columns: x => new { x.organization_id, x.commission_entitlement_id },
                        principalTable: "commission_entitlements",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_payment_allocations_organization_id_payment~",
                        columns: x => new { x.organization_id, x.payment_allocation_id },
                        principalTable: "payment_allocations",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_payments_organization_id_payment_id",
                        columns: x => new { x.organization_id, x.payment_id },
                        principalTable: "payments",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_receivables_organization_id_receivable_id",
                        columns: x => new { x.organization_id, x.receivable_id },
                        principalTable: "receivables",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_lines_accounts_organization_id_account_id",
                        columns: x => new { x.organization_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_organization_kind",
                table: "accounts",
                columns: new[] { "organization_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commission_adjustments_entitlement",
                table: "commission_adjustments",
                column: "commission_entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_entitlements_obligation",
                table: "commission_entitlements",
                column: "monetary_obligation_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_entitlements_organization_client",
                table: "commission_entitlements",
                columns: new[] { "organization_id", "client_person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commission_entitlements_organization_id_commission_rule_id",
                table: "commission_entitlements",
                columns: new[] { "organization_id", "commission_rule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commission_entitlements_organization_id_monetary_obligation~",
                table: "commission_entitlements",
                columns: new[] { "organization_id", "monetary_obligation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_entitlements_organization_status",
                table: "commission_entitlements",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_organization_client",
                table: "commission_rules",
                columns: new[] { "organization_id", "client_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_organization_effective",
                table: "commission_rules",
                columns: new[] { "organization_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_events_contract",
                table: "finance_events",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_finance_events_organization_occurred",
                table: "finance_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_events_receivable",
                table: "finance_events",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_task_links_organization_id_receivable_id",
                table: "finance_task_links",
                columns: new[] { "organization_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_task_links_organization_id_task_item_id",
                table: "finance_task_links",
                columns: new[] { "organization_id", "task_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_task_links_receivable",
                table: "finance_task_links",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ux_finance_task_links_task",
                table: "finance_task_links",
                column: "task_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_organization_id_receivable_id",
                table: "invoice_lines",
                columns: new[] { "organization_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_invoice_receivable",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "receivable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_contract",
                table: "invoices",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_due",
                table: "invoices",
                columns: new[] { "organization_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_organization_id_contract_id",
                table: "invoices",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_status",
                table: "invoices",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_allocation",
                table: "journal_entries",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_organization_id_commission_entitlement_id",
                table: "journal_entries",
                columns: new[] { "organization_id", "commission_entitlement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_organization_id_payment_allocation_id",
                table: "journal_entries",
                columns: new[] { "organization_id", "payment_allocation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_organization_id_payment_id",
                table: "journal_entries",
                columns: new[] { "organization_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_organization_id_receivable_id",
                table: "journal_entries",
                columns: new[] { "organization_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_organization_posting",
                table: "journal_entries",
                columns: new[] { "organization_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_organization_status",
                table: "journal_entries",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_receivable_source",
                table: "journal_entries",
                columns: new[] { "receivable_id", "source" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_account_currency",
                table: "journal_lines",
                columns: new[] { "account_id", "currency_code" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_entry",
                table: "journal_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_organization_id_account_id",
                table: "journal_lines",
                columns: new[] { "organization_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monetary_obligations_contract",
                table: "monetary_obligations",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_monetary_obligations_organization_due",
                table: "monetary_obligations",
                columns: new[] { "organization_id", "resolved_due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_monetary_obligations_organization_id_contract_id",
                table: "monetary_obligations",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_monetary_obligations_organization_id_contract_version_id",
                table: "monetary_obligations",
                columns: new[] { "organization_id", "contract_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monetary_obligations_organization_status",
                table: "monetary_obligations",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_adjustments_organization_id_receivable_id",
                table: "payment_adjustments",
                columns: new[] { "organization_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_adjustments_receivable",
                table: "payment_adjustments",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_organization_id_receivable_id",
                table: "payment_allocations",
                columns: new[] { "organization_id", "receivable_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_receivable",
                table: "payment_allocations",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_received",
                table: "payments",
                columns: new[] { "organization_id", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_reference",
                table: "payments",
                columns: new[] { "organization_id", "external_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_status",
                table: "payments",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_contract",
                table: "receivables",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_obligation",
                table: "receivables",
                column: "monetary_obligation_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_organization_client",
                table: "receivables",
                columns: new[] { "organization_id", "client_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_organization_due",
                table: "receivables",
                columns: new[] { "organization_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_organization_id_contract_id",
                table: "receivables",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_organization_id_monetary_obligation_id",
                table: "receivables",
                columns: new[] { "organization_id", "monetary_obligation_id" });

            // ---- Containment -----------------------------------------------
            //
            // The party and client links EF could not build, because the columns
            // are plain Guids at the domain boundary while the principal keys are
            // typed. Written by hand, and no weaker for it: a finance row still
            // cannot reach a party on another instrument or a person in another
            // tenant (ADR-0011, ADR-0023).
            foreach ((string table, string column, string name) in new[]
            {
                ("monetary_obligations", "payer_party_id", "payer"),
                ("monetary_obligations", "payee_party_id", "payee"),
                ("receivables", "payer_party_id", "payer"),
                ("invoices", "debtor_party_id", "debtor"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{name}_same_contract
                    FOREIGN KEY (organization_id, contract_id, {column})
                    REFERENCES contract_parties (organization_id, contract_id, id);
                    """);
            }

            foreach ((string table, string column) in new[]
            {
                ("receivables", "client_person_id"),
                ("commission_rules", "client_person_id"),
                ("commission_entitlements", "client_person_id"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT fk_{table}_client_same_tenant
                    FOREIGN KEY (organization_id, {column})
                    REFERENCES people (organization_id, id);
                    """);
            }

            // ---- Money shape -------------------------------------------------
            //
            // Every monetary column is numeric and every one that must be positive
            // says so. A row of nothing records an event that did not happen, and
            // a negative amount is a sign convention somebody would have to
            // remember (ADR-0023).
            foreach ((string table, string column) in new[]
            {
                ("receivables", "original_amount_value"),
                ("payments", "amount_value"),
                ("payment_allocations", "amount_value"),
                ("payment_adjustments", "amount_value"),
                ("invoice_lines", "amount_value"),
                ("journal_lines", "amount_value"),
                ("commission_adjustments", "amount_value"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT ck_{table}_amount_positive
                    CHECK ({column} > 0);
                    """);
            }

            foreach ((string table, string column) in new[]
            {
                ("receivables", "allocated_amount_value"),
                ("receivables", "adjusted_amount_value"),
                ("commission_entitlements", "basis_amount_value"),
                ("commission_entitlements", "entitled_amount_value"),
                ("monetary_obligations", "amount_value"),
                ("monetary_obligations", "unit_amount_value"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT ck_{table}_{column}_non_negative
                    CHECK ({column} IS NULL OR {column} >= 0);
                    """);
            }

            foreach (string table in new[]
            {
                "monetary_obligations", "receivables", "invoices", "invoice_lines", "payments",
                "payment_allocations", "payment_adjustments", "commission_rules",
                "commission_entitlements", "commission_adjustments", "journal_entries",
                "journal_lines", "finance_events",
            })
            {
                // The brace pair is doubled because this is an interpolated raw
                // string; PostgreSQL receives a single {3}.
                migrationBuilder.Sql($$"""
                    ALTER TABLE {{table}} ADD CONSTRAINT ck_{{table}}_currency_shape
                    CHECK (currency_code IS NULL OR currency_code ~ '^[A-Z]{3}$');
                    """);
            }

            // ---- Receivable arithmetic ---------------------------------------
            //
            // The running totals are a summary of rows stored elsewhere, and an
            // integration test reconciles them against those rows. These stop the
            // summary going somewhere the rows never could.
            migrationBuilder.Sql("""
                ALTER TABLE receivables ADD CONSTRAINT ck_receivables_not_over_applied
                CHECK (allocated_amount_value + adjusted_amount_value <= original_amount_value);
                """);

            // Closed by an act carries a reason; open does not carry one.
            migrationBuilder.Sql("""
                ALTER TABLE receivables ADD CONSTRAINT ck_receivables_closure_reason
                CHECK (is_closed_by_act = (closure_reason IS NOT NULL));
                """);

            // A client receivable names its client, or the money it collects has
            // nowhere to go. Beneficiary 1 is Client.
            migrationBuilder.Sql("""
                ALTER TABLE receivables ADD CONSTRAINT ck_receivables_client_named
                CHECK (beneficiary <> 1 OR client_person_id IS NOT NULL);
                """);

            // ---- Obligation amount shape -------------------------------------
            //
            // Kind 1 is Fixed, 2 Formula, 3 Contingent, 4 Unknown. The fourth is
            // the honest one: an obligation nobody can value carries no figure at
            // all rather than a zero that every total would then include
            // (ADR-0023).
            migrationBuilder.Sql("""
                ALTER TABLE monetary_obligations ADD CONSTRAINT ck_monetary_obligations_amount_shape
                CHECK (
                    CASE amount_kind
                        WHEN 1 THEN amount_value IS NOT NULL AND currency_code IS NOT NULL
                            AND quantity IS NULL AND unit_amount_value IS NULL
                        WHEN 2 THEN quantity IS NOT NULL AND unit_amount_value IS NOT NULL
                            AND currency_code IS NOT NULL AND amount_value IS NULL
                        WHEN 3 THEN condition IS NOT NULL
                            AND quantity IS NULL AND unit_amount_value IS NULL
                        WHEN 4 THEN amount_value IS NULL AND quantity IS NULL
                            AND unit_amount_value IS NULL
                        ELSE false
                    END);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE monetary_obligations ADD CONSTRAINT ck_monetary_obligations_quantity
                CHECK (quantity IS NULL OR quantity > 0);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE monetary_obligations ADD CONSTRAINT ck_monetary_obligations_two_parties
                CHECK (payer_party_id <> payee_party_id);
                """);

            // The M8 deadline rule shape, applied once more. Kind 1 is a stated
            // date, 2 an offset from an event, 3 the clause's own words.
            migrationBuilder.Sql("""
                ALTER TABLE monetary_obligations ADD CONSTRAINT ck_monetary_obligations_due_shape
                CHECK (
                    CASE due_kind
                        WHEN 1 THEN due_on IS NOT NULL
                            AND due_anchor IS NULL AND due_offset IS NULL AND due_unit IS NULL
                        WHEN 2 THEN due_anchor IS NOT NULL AND due_offset IS NOT NULL
                            AND due_unit IS NOT NULL AND due_on IS NULL
                        WHEN 3 THEN due_description IS NOT NULL
                            AND due_on IS NULL AND due_anchor IS NULL
                            AND due_offset IS NULL AND due_unit IS NULL
                        ELSE false
                    END);
                """);

            // ---- Payments and allocations ------------------------------------
            //
            // A payment never reverses itself, and an allocation reversed carries
            // its reason. Status 2 is Reversed.
            migrationBuilder.Sql("""
                ALTER TABLE payments ADD CONSTRAINT ck_payments_no_self_reversal
                CHECK (
                    (reversed_by_payment_id IS NULL OR reversed_by_payment_id <> id)
                    AND (reversal_of_payment_id IS NULL OR reversal_of_payment_id <> id));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payments ADD CONSTRAINT ck_payments_reversal_reason
                CHECK (status <> 2 OR reversal_reason IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payments ADD CONSTRAINT ck_payments_payer_named
                CHECK (payer_party_id IS NOT NULL OR payer_name IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payment_allocations ADD CONSTRAINT ck_payment_allocations_reversal
                CHECK (is_applied = (reversed_at IS NULL));
                """);

            // One canonical reversal per payment, so two people undoing the same
            // payment at once produce one reversal rather than two.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_payments_one_reversal
                ON payments (reversal_of_payment_id)
                WHERE reversal_of_payment_id IS NOT NULL;
                """);

            // ---- Invoice numbering -------------------------------------------
            //
            // AgencyOS assigns no invoice number, because numbering carries
            // statutory weight that varies by jurisdiction and the system is in no
            // position to claim compliance with any of them. What it does is refuse
            // to hold one operator-supplied number twice in a tenant, so a
            // duplicate is caught rather than filed (ADR-0023).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_invoices_organization_reference
                ON invoices (organization_id, reference)
                WHERE reference IS NOT NULL;
                """);

            // An issued invoice has a number and a date; a draft has neither
            // requirement. Status 2 is Issued, 3 is Void.
            migrationBuilder.Sql("""
                ALTER TABLE invoices ADD CONSTRAINT ck_invoices_issued_complete
                CHECK (status <> 2 OR (reference IS NOT NULL AND issued_on IS NOT NULL));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invoices ADD CONSTRAINT ck_invoices_void_reason
                CHECK (status <> 3 OR void_reason IS NOT NULL);
                """);

            // ---- Commission ---------------------------------------------------
            //
            // Basis 1 is GrossCompensation, 2 SpecificTerm, 3 FixedAmount. The
            // bound on the rate exists to catch 1000 entered for 10.00, not to
            // express a view about what a fair rate is.
            migrationBuilder.Sql("""
                ALTER TABLE commission_rules ADD CONSTRAINT ck_commission_rules_shape
                CHECK (
                    CASE basis
                        WHEN 1 THEN rate_percent IS NOT NULL
                        WHEN 2 THEN rate_percent IS NOT NULL AND term_code IS NOT NULL
                        WHEN 3 THEN fixed_amount_value IS NOT NULL AND currency_code IS NOT NULL
                        ELSE false
                    END);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE commission_rules ADD CONSTRAINT ck_commission_rules_rate_range
                CHECK (rate_percent IS NULL OR (rate_percent > 0 AND rate_percent <= 50));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE commission_rules ADD CONSTRAINT ck_commission_rules_period
                CHECK (effective_to IS NULL OR effective_to >= effective_from);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE commission_entitlements
                ADD CONSTRAINT ck_commission_entitlements_rate_range
                CHECK (rate_percent_snapshot IS NULL
                    OR (rate_percent_snapshot > 0 AND rate_percent_snapshot <= 50));
                """);

            // ---- Double entry -------------------------------------------------
            //
            // The invariant that makes a ledger a ledger. Checked three times over
            // on purpose: the aggregate refuses to post unbalanced lines, the F#
            // kernel computes the totals, and this trigger checks the same
            // arithmetic at commit.
            //
            // Deferred, because balance is a property of a set of rows and the
            // lines are inserted one at a time. A non-deferred check would fire
            // after the first line and fail every entry ever written (ADR-0023).
            //
            // Status 2 is Posted, 3 is Reversed; side 1 is Debit, 2 is Credit.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_journal_balanced()
                RETURNS trigger AS $$
                DECLARE
                    entry_status integer;
                    debit_total numeric;
                    credit_total numeric;
                    line_count integer;
                BEGIN
                    SELECT status INTO entry_status FROM journal_entries WHERE id = NEW.id;

                    -- A draft is not part of the ledger and is not checked. It
                    -- becomes canonical only by being posted.
                    IF entry_status IS NULL OR entry_status = 1 THEN
                        RETURN NEW;
                    END IF;

                    SELECT
                        COUNT(*),
                        COALESCE(SUM(CASE WHEN side = 1 THEN amount_value ELSE 0 END), 0),
                        COALESCE(SUM(CASE WHEN side = 2 THEN amount_value ELSE 0 END), 0)
                    INTO line_count, debit_total, credit_total
                    FROM journal_lines
                    WHERE journal_entry_id = NEW.id;

                    IF line_count = 0 THEN
                        RAISE EXCEPTION
                            'journal entry % was posted with no lines', NEW.id
                            USING ERRCODE = 'check_violation';
                    END IF;

                    IF debit_total <> credit_total THEN
                        RAISE EXCEPTION
                            'journal entry % does not balance: debits %, credits %',
                            NEW.id, debit_total, credit_total
                            USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER trg_journal_entries_balanced
                AFTER INSERT OR UPDATE ON journal_entries
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION agencyos_journal_balanced();
                """);

            // ---- Posted-entry immutability ------------------------------------
            //
            // A different concept from append-only audit, from M7 offer
            // immutability and from M8 term immutability, and worth stating
            // plainly: once an entry is posted, what the agency has said about its
            // own books cannot be restated. A correction is another entry saying
            // the opposite (ADR-0023).
            //
            // Status, version, updated_at and the reversal columns stay writable:
            // marking an entry reversed is how the correction is recorded.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_journal_entries_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD.status <> 1 AND (
                        NEW.source IS DISTINCT FROM OLD.source
                        OR NEW.memo IS DISTINCT FROM OLD.memo
                        OR NEW.currency_code IS DISTINCT FROM OLD.currency_code
                        OR NEW.occurred_on IS DISTINCT FROM OLD.occurred_on
                        OR NEW.posting_date IS DISTINCT FROM OLD.posting_date
                        OR NEW.posted_at IS DISTINCT FROM OLD.posted_at
                        OR NEW.posted_by IS DISTINCT FROM OLD.posted_by
                        OR NEW.receivable_id IS DISTINCT FROM OLD.receivable_id
                        OR NEW.payment_id IS DISTINCT FROM OLD.payment_id
                        OR NEW.payment_allocation_id IS DISTINCT FROM OLD.payment_allocation_id
                        OR NEW.commission_entitlement_id IS DISTINCT FROM OLD.commission_entitlement_id
                    ) THEN
                        RAISE EXCEPTION
                            'journal entry % has been posted; it is immutable', OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_entries_immutable
                BEFORE UPDATE ON journal_entries
                FOR EACH ROW EXECUTE FUNCTION agencyos_journal_entries_immutable();
                """);

            // A posted entry's lines cannot be changed or removed at all. A null
            // parent means the entry itself is being deleted in this transaction,
            // so the cascade is allowed through.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_journal_lines_immutable()
                RETURNS trigger AS $$
                DECLARE
                    target uuid;
                    parent_status integer;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        target := OLD.journal_entry_id;
                    ELSE
                        target := NEW.journal_entry_id;
                    END IF;

                    SELECT status INTO parent_status FROM journal_entries WHERE id = target;

                    IF parent_status IS NOT NULL AND parent_status <> 1 THEN
                        RAISE EXCEPTION
                            'journal entry % has been posted; its lines are immutable', target
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_lines_immutable
                BEFORE UPDATE OR DELETE ON journal_lines
                FOR EACH ROW EXECUTE FUNCTION agencyos_journal_lines_immutable();
                """);

            // ---- Payment immutability -----------------------------------------
            //
            // A payment is what somebody observed. Its amount, currency and date
            // are frozen the moment it is recorded, and a typo is corrected by
            // reversing it and recording the right one - so both the mistake and
            // the correction survive with their own dates and actors (ADR-0023).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_payments_frozen()
                RETURNS trigger AS $$
                BEGIN
                    IF NEW.amount_value IS DISTINCT FROM OLD.amount_value
                        OR NEW.currency_code IS DISTINCT FROM OLD.currency_code
                        OR NEW.received_on IS DISTINCT FROM OLD.received_on
                        OR NEW.direction IS DISTINCT FROM OLD.direction
                    THEN
                        RAISE EXCEPTION
                            'payment % records an observed movement; its amount, currency and date are immutable',
                            OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_payments_frozen
                BEFORE UPDATE ON payments
                FOR EACH ROW EXECUTE FUNCTION agencyos_payments_frozen();
                """);

            // ---- Work queues ---------------------------------------------------
            //
            // Partial indexes over the rows a finance desk actually reads. A
            // receivable with no resolvable due date carries a null and is absent
            // from the overdue queue, which is the same answer the queries give.
            migrationBuilder.Sql("""
                CREATE INDEX ix_receivables_open_due
                ON receivables (organization_id, due_on)
                WHERE is_closed_by_act = false
                  AND due_on IS NOT NULL
                  AND allocated_amount_value + adjusted_amount_value < original_amount_value;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_payments_unapplied
                ON payments (organization_id, received_on)
                WHERE status = 1;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_commission_entitlements_open
                ON commission_entitlements (organization_id, governing_on)
                WHERE status = 1;
                """);

            // ---- Search ---------------------------------------------------------
            //
            // The invoice reference and nothing else. No amount, no balance, no
            // notes and no commission rate is indexed anywhere, so a search hit
            // reveals that an invoice with that number exists and nothing about
            // what it is worth. A caller without finance.read gets no branch at
            // all (ADR-0021, ADR-0023).
            migrationBuilder.Sql("""
                ALTER TABLE invoices ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(reference, '')), 'A')
                ) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_invoices_search_vector ON invoices USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_invoices_reference_trgm ON invoices USING gin (reference gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_invoices_reference_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_invoices_search_vector;");
            migrationBuilder.Sql("ALTER TABLE invoices DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_commission_entitlements_open;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_payments_unapplied;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_receivables_open_due;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_invoices_organization_reference;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_payments_one_reversal;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_payments_frozen ON payments;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_payments_frozen();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_journal_lines_immutable ON journal_lines;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_journal_lines_immutable();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_journal_entries_immutable ON journal_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_journal_entries_immutable();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_journal_entries_balanced ON journal_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_journal_balanced();");

            migrationBuilder.DropTable(
                name: "commission_adjustments");

            migrationBuilder.DropTable(
                name: "finance_events");

            migrationBuilder.DropTable(
                name: "finance_task_links");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "payment_adjustments");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "commission_entitlements");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "commission_rules");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "receivables");

            migrationBuilder.DropTable(
                name: "monetary_obligations");
        }
    }
}
