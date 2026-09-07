using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractsRightsAndObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    legal_analysis = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    strategy_notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    privilege = table.Column<int>(type: "integer", nullable: false),
                    executed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_on = table.Column<DateOnly>(type: "date", nullable: true),
                    terminated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contracts", x => x.id);
                    table.UniqueConstraint("AK_contracts_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_contracts_deals_organization_id_deal_id",
                        columns: x => new { x.organization_id, x.deal_id },
                        principalTable: "deals",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contracts_offers_organization_id_accepted_offer_id",
                        columns: x => new { x.organization_id, x.accepted_offer_id },
                        principalTable: "offers",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contracts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contracts_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    transition = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_contract_events_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provenance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_required_signatory = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_parties", x => x.id);
                    table.UniqueConstraint("AK_contract_parties_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_contract_parties_companies_organization_id_company_id",
                        columns: x => new { x.organization_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_parties_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_parties_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_relationships", x => x.id);
                    table.ForeignKey(
                        name: "FK_contract_relationships_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_relationships_contracts_organization_id_related_co~",
                        columns: x => new { x.organization_id, x.related_contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    media_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_versions", x => x.id);
                    table.UniqueConstraint("AK_contract_versions_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_contract_versions_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_signatures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_signatures", x => x.id);
                    table.ForeignKey(
                        name: "FK_contract_signatures_contract_parties_organization_id_contra~",
                        columns: x => new { x.organization_id, x.contract_party_id },
                        principalTable: "contract_parties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_signatures_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clause_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    holder_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    window_opens_on = table.Column<DateOnly>(type: "date", nullable: true),
                    deadline_kind = table.Column<int>(type: "integer", nullable: false),
                    deadline_on = table.Column<DateOnly>(type: "date", nullable: true),
                    deadline_anchor = table.Column<int>(type: "integer", nullable: true),
                    deadline_offset = table.Column<int>(type: "integer", nullable: true),
                    deadline_unit = table.Column<int>(type: "integer", nullable: true),
                    deadline_before = table.Column<bool>(type: "boolean", nullable: false),
                    deadline_basis = table.Column<int>(type: "integer", nullable: false),
                    deadline_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_deadline_on = table.Column<DateOnly>(type: "date", nullable: true),
                    exercise_method = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notice_requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    economics_term_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_options", x => x.id);
                    table.UniqueConstraint("AK_contract_options_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_contract_options_contract_parties_organization_id_holder_pa~",
                        columns: x => new { x.organization_id, x.holder_party_id },
                        principalTable: "contract_parties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_options_contract_versions_organization_id_contract~",
                        columns: x => new { x.organization_id, x.contract_version_id },
                        principalTable: "contract_versions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_options_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    value_kind = table.Column<int>(type: "integer", nullable: false),
                    amount_value = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    numeric_value = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    integer_value = table.Column<long>(type: "bigint", nullable: true),
                    text_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true),
                    date_value = table.Column<DateOnly>(type: "date", nullable: true),
                    unit = table.Column<int>(type: "integer", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    clause_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    privilege = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_terms", x => x.id);
                    table.UniqueConstraint("AK_contract_terms_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_contract_terms_contract_versions_contract_version_id",
                        column: x => x.contract_version_id,
                        principalTable: "contract_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notice_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clause_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    obligor_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    due_kind = table.Column<int>(type: "integer", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_anchor = table.Column<int>(type: "integer", nullable: true),
                    due_offset = table.Column<int>(type: "integer", nullable: true),
                    due_unit = table.Column<int>(type: "integer", nullable: true),
                    due_before = table.Column<bool>(type: "boolean", nullable: false),
                    due_basis = table.Column<int>(type: "integer", nullable: false),
                    due_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    method = table.Column<int>(type: "integer", nullable: false),
                    address_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    related_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_obligation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notice_requirements", x => x.id);
                    table.UniqueConstraint("AK_notice_requirements_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_notice_requirements_contract_parties_organization_id_obligo~",
                        columns: x => new { x.organization_id, x.obligor_party_id },
                        principalTable: "contract_parties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notice_requirements_contract_versions_organization_id_contr~",
                        columns: x => new { x.organization_id, x.contract_version_id },
                        principalTable: "contract_versions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notice_requirements_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rights_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clause_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    grantor_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    right_type = table.Column<int>(type: "integer", nullable: false),
                    medium = table.Column<int>(type: "integer", nullable: false),
                    territory = table.Column<int>(type: "integer", nullable: false),
                    territory_detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    exclusivity = table.Column<int>(type: "integer", nullable: false),
                    period_kind = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source_property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reservations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    superseded_by_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rights_grants", x => x.id);
                    table.UniqueConstraint("AK_rights_grants_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_rights_grants_contract_parties_organization_id_grantor_part~",
                        columns: x => new { x.organization_id, x.grantor_party_id },
                        principalTable: "contract_parties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rights_grants_contract_versions_organization_id_contract_ve~",
                        columns: x => new { x.organization_id, x.contract_version_id },
                        principalTable: "contract_versions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rights_grants_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rights_grants_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "option_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    transition = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_option_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_option_events_contract_options_contract_option_id",
                        column: x => x.contract_option_id,
                        principalTable: "contract_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notice_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    sender_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notice_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_notice_records_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notice_records_notice_requirements_organization_id_notice_r~",
                        columns: x => new { x.organization_id, x.notice_requirement_id },
                        principalTable: "notice_requirements",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "obligations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clause_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    obligor_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligee_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    resolved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    related_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_rights_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    privilege = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_obligations", x => x.id);
                    table.UniqueConstraint("AK_obligations_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_obligations_contract_options_organization_id_related_option~",
                        columns: x => new { x.organization_id, x.related_option_id },
                        principalTable: "contract_options",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_obligations_contract_parties_organization_id_obligor_party_~",
                        columns: x => new { x.organization_id, x.obligor_party_id },
                        principalTable: "contract_parties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_obligations_contract_versions_organization_id_contract_vers~",
                        columns: x => new { x.organization_id, x.contract_version_id },
                        principalTable: "contract_versions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_obligations_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_obligations_rights_grants_organization_id_related_rights_gr~",
                        columns: x => new { x.organization_id, x.related_rights_grant_id },
                        principalTable: "rights_grants",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_task_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_task_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_contract_task_links_contract_options_organization_id_contra~",
                        columns: x => new { x.organization_id, x.contract_option_id },
                        principalTable: "contract_options",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_task_links_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_task_links_obligations_organization_id_obligation_~",
                        columns: x => new { x.organization_id, x.obligation_id },
                        principalTable: "obligations",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_task_links_tasks_organization_id_task_item_id",
                        columns: x => new { x.organization_id, x.task_item_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "obligation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    transition = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_obligation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_obligation_events_obligations_obligation_id",
                        column: x => x.obligation_id,
                        principalTable: "obligations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_events_contract_recorded",
                table: "contract_events",
                columns: new[] { "contract_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_options_contract",
                table: "contract_options",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_options_organization_deadline",
                table: "contract_options",
                columns: new[] { "organization_id", "resolved_deadline_on" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_options_organization_id_contract_id",
                table: "contract_options",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_options_organization_id_contract_version_id",
                table: "contract_options",
                columns: new[] { "organization_id", "contract_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_options_organization_id_holder_party_id",
                table: "contract_options",
                columns: new[] { "organization_id", "holder_party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_options_organization_status",
                table: "contract_options",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_parties_contract",
                table: "contract_parties",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_parties_organization_company",
                table: "contract_parties",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_parties_organization_person",
                table: "contract_parties",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_relationships_organization_id_contract_id",
                table: "contract_relationships",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_relationships_organization_id_related_contract_id",
                table: "contract_relationships",
                columns: new[] { "organization_id", "related_contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_relationships_related",
                table: "contract_relationships",
                column: "related_contract_id");

            migrationBuilder.CreateIndex(
                name: "ux_contract_relationships_pair_kind",
                table: "contract_relationships",
                columns: new[] { "contract_id", "related_contract_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_signatures_organization_id_contract_party_id",
                table: "contract_signatures",
                columns: new[] { "organization_id", "contract_party_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_signatures_contract_party",
                table: "contract_signatures",
                columns: new[] { "contract_id", "contract_party_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_task_links_contract",
                table: "contract_task_links",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_contract_task_links_organization_id_contract_id",
                table: "contract_task_links",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_task_links_organization_id_contract_option_id",
                table: "contract_task_links",
                columns: new[] { "organization_id", "contract_option_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_task_links_organization_id_obligation_id",
                table: "contract_task_links",
                columns: new[] { "organization_id", "obligation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_task_links_organization_id_task_item_id",
                table: "contract_task_links",
                columns: new[] { "organization_id", "task_item_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_task_links_task",
                table: "contract_task_links",
                column: "task_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_contract_terms_version_code",
                table: "contract_terms",
                columns: new[] { "contract_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_versions_organization_id_contract_id",
                table: "contract_versions",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_versions_organization_status",
                table: "contract_versions",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_versions_contract_number",
                table: "contract_versions",
                columns: new[] { "contract_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_accepted_offer",
                table: "contracts",
                column: "accepted_offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_deal",
                table: "contracts",
                columns: new[] { "organization_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_organization_id_accepted_offer_id",
                table: "contracts",
                columns: new[] { "organization_id", "accepted_offer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_owner",
                table: "contracts",
                columns: new[] { "organization_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_status",
                table: "contracts",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_updated",
                table: "contracts",
                columns: new[] { "organization_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_owner_user_id",
                table: "contracts",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notice_records_contract_occurred",
                table: "notice_records",
                columns: new[] { "contract_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "IX_notice_records_organization_id_contract_id",
                table: "notice_records",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notice_records_organization_id_notice_requirement_id",
                table: "notice_records",
                columns: new[] { "organization_id", "notice_requirement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notice_requirements_contract",
                table: "notice_requirements",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_notice_requirements_organization_due",
                table: "notice_requirements",
                columns: new[] { "organization_id", "resolved_due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_notice_requirements_organization_id_contract_id",
                table: "notice_requirements",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notice_requirements_organization_id_contract_version_id",
                table: "notice_requirements",
                columns: new[] { "organization_id", "contract_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notice_requirements_organization_id_obligor_party_id",
                table: "notice_requirements",
                columns: new[] { "organization_id", "obligor_party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_obligation_events_obligation_recorded",
                table: "obligation_events",
                columns: new[] { "obligation_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_obligations_contract",
                table: "obligations",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_obligations_organization_due",
                table: "obligations",
                columns: new[] { "organization_id", "resolved_due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_obligations_organization_id_contract_id",
                table: "obligations",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_obligations_organization_id_contract_version_id",
                table: "obligations",
                columns: new[] { "organization_id", "contract_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_obligations_organization_id_obligor_party_id",
                table: "obligations",
                columns: new[] { "organization_id", "obligor_party_id" });

            migrationBuilder.CreateIndex(
                name: "IX_obligations_organization_id_related_option_id",
                table: "obligations",
                columns: new[] { "organization_id", "related_option_id" });

            migrationBuilder.CreateIndex(
                name: "IX_obligations_organization_id_related_rights_grant_id",
                table: "obligations",
                columns: new[] { "organization_id", "related_rights_grant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_obligations_organization_status",
                table: "obligations",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_option_events_option_recorded",
                table: "option_events",
                columns: new[] { "contract_option_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rights_grants_contract",
                table: "rights_grants",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_rights_grants_organization_id_contract_id",
                table: "rights_grants",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rights_grants_organization_id_contract_version_id",
                table: "rights_grants",
                columns: new[] { "organization_id", "contract_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rights_grants_organization_id_grantor_party_id",
                table: "rights_grants",
                columns: new[] { "organization_id", "grantor_party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rights_grants_organization_project",
                table: "rights_grants",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rights_grants_organization_right_medium",
                table: "rights_grants",
                columns: new[] { "organization_id", "right_type", "medium" });

            migrationBuilder.CreateIndex(
                name: "ix_rights_grants_organization_status",
                table: "rights_grants",
                columns: new[] { "organization_id", "status" });

            // ---- Containment -----------------------------------------------
            //
            // A party, a version and an offer each belong to one contract, and the
            // rows that point at them must not be able to reach across to another
            // instrument in the same tenant. Expressed as foreign keys over the
            // whole triple rather than as checks, because a check cannot see
            // another row - the M7 offer-chain pattern, applied to five more
            // relationships (ADR-0011, ADR-0022).
            migrationBuilder.Sql("""
                ALTER TABLE contract_parties ADD CONSTRAINT ak_contract_parties_org_contract_id
                UNIQUE (organization_id, contract_id, id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_versions ADD CONSTRAINT ak_contract_versions_org_contract_id
                UNIQUE (organization_id, contract_id, id);
                """);

            foreach ((string table, string column, string name) in new[]
            {
                ("contract_signatures", "contract_party_id", "signatory"),
                ("rights_grants", "grantor_party_id", "grantor"),
                ("rights_grants", "grantee_party_id", "grantee"),
                ("contract_options", "holder_party_id", "holder"),
                ("obligations", "obligor_party_id", "obligor"),
                ("obligations", "obligee_party_id", "obligee"),
                ("notice_requirements", "obligor_party_id", "obligor"),
                ("notice_requirements", "recipient_party_id", "recipient"),
                ("notice_records", "sender_party_id", "sender"),
                ("notice_records", "recipient_party_id", "recipient"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{name}_same_contract
                    FOREIGN KEY (organization_id, contract_id, {column})
                    REFERENCES contract_parties (organization_id, contract_id, id);
                    """);
            }

            foreach (string table in new[] { "rights_grants", "contract_options", "obligations", "notice_requirements" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT fk_{table}_version_same_contract
                    FOREIGN KEY (organization_id, contract_id, contract_version_id)
                    REFERENCES contract_versions (organization_id, contract_id, id);
                    """);
            }

            // The contract papers an offer from the negotiation it names. That the
            // offer is the deal's *accepted* one is checked by the handler and not
            // here, and deliberately: acceptance is a status the offer row carries
            // and M7 can move it - reopening a negotiation unwinds an agreement -
            // so a key that pinned it would turn a legitimate M7 act into a
            // constraint violation on an M8 row (ADR-0021, ADR-0022).
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT fk_contracts_offer_same_deal
                FOREIGN KEY (organization_id, deal_id, accepted_offer_id)
                REFERENCES offers (organization_id, deal_id, id);
                """);

            // ---- Distinct dates ---------------------------------------------
            //
            // Signature, execution, effectiveness and termination are four
            // separate facts and the schema keeps them apart. There is
            // deliberately no constraint ordering effective_on against
            // executed_on: an agreement effective as of January and signed in
            // March is ordinary, and a check forbidding it would force somebody
            // to record a date the contract does not state (ADR-0022).
            //
            // Status 5 is Executed and 8 is Terminated.
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ck_contracts_executed_has_date
                CHECK (status <> 5 OR executed_on IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ck_contracts_terminated_has_date
                CHECK (status <> 8 OR terminated_on IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ck_contracts_effective_before_end
                CHECK (
                    effective_on IS NULL
                    OR terminated_on IS NULL
                    OR effective_on <= terminated_on);
                """);

            // ---- Party identity ---------------------------------------------
            //
            // A party is somebody AgencyOS knows or a name it was told, never
            // both and never neither. A free-text name recorded beside a real
            // record is how a party silently stops being the same party.
            migrationBuilder.Sql("""
                ALTER TABLE contract_parties ADD CONSTRAINT ck_contract_parties_identity
                CHECK (
                    (person_id IS NOT NULL)::int
                    + (company_id IS NOT NULL)::int
                    + (external_name IS NOT NULL)::int = 1);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_versions ADD CONSTRAINT ck_contract_versions_number_positive
                CHECK (version_number >= 1);
                """);

            // Two sides, not one. A party does not grant to itself, owe itself, or
            // serve notice on itself.
            migrationBuilder.Sql("""
                ALTER TABLE rights_grants ADD CONSTRAINT ck_rights_grants_two_parties
                CHECK (grantor_party_id <> grantee_party_id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE obligations ADD CONSTRAINT ck_obligations_two_parties
                CHECK (obligor_party_id <> obligee_party_id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE notice_requirements ADD CONSTRAINT ck_notice_requirements_two_parties
                CHECK (obligor_party_id <> recipient_party_id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE notice_records ADD CONSTRAINT ck_notice_records_two_parties
                CHECK (sender_party_id <> recipient_party_id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_relationships ADD CONSTRAINT ck_contract_relationships_no_self
                CHECK (contract_id <> related_contract_id);
                """);

            // ---- Term value shape -------------------------------------------
            //
            // Character for character the offer_terms rule, because reconciliation
            // compares the two and a second money representation would make that
            // comparison a conversion. Money is numeric and never floating point
            // (CLAUDE.md section 5, ADR-0022).
            migrationBuilder.Sql("""
                ALTER TABLE contract_terms ADD CONSTRAINT ck_contract_terms_value_shape
                CHECK (
                    CASE value_kind
                        WHEN 1 THEN amount_value IS NOT NULL AND currency_code IS NOT NULL
                            AND numeric_value IS NULL AND integer_value IS NULL
                            AND text_value IS NULL AND boolean_value IS NULL
                            AND date_value IS NULL AND unit IS NULL
                        WHEN 2 THEN numeric_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND integer_value IS NULL AND text_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL AND unit IS NULL
                        WHEN 3 THEN integer_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND text_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL AND unit IS NULL
                        WHEN 4 THEN numeric_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND integer_value IS NULL AND text_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL AND unit IS NULL
                        WHEN 5 THEN text_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND integer_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL AND unit IS NULL
                        WHEN 6 THEN boolean_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND integer_value IS NULL
                            AND text_value IS NULL AND date_value IS NULL AND unit IS NULL
                        WHEN 7 THEN date_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND integer_value IS NULL
                            AND text_value IS NULL AND boolean_value IS NULL AND unit IS NULL
                        WHEN 8 THEN integer_value IS NOT NULL AND unit IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND text_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL
                        WHEN 9 THEN integer_value IS NOT NULL
                            AND amount_value IS NULL AND currency_code IS NULL
                            AND numeric_value IS NULL AND text_value IS NULL
                            AND boolean_value IS NULL AND date_value IS NULL
                        ELSE false
                    END);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_terms ADD CONSTRAINT ck_contract_terms_money_non_negative
                CHECK (amount_value IS NULL OR amount_value >= 0);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_terms ADD CONSTRAINT ck_contract_terms_percentage_range
                CHECK (
                    value_kind <> 2
                    OR (numeric_value >= 0 AND numeric_value <= 1000));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE contract_terms ADD CONSTRAINT ck_contract_terms_currency_shape
                CHECK (currency_code IS NULL OR currency_code ~ '^[A-Z]{3}$');
                """);

            // ---- Rights periods ---------------------------------------------
            //
            // Kind 1 is Perpetual, 2 Fixed, 3 OpenEnded and 4 Unstated. The fourth
            // exists so a contract that states no period this build can structure
            // is recorded as saying nothing, rather than as running forever.
            migrationBuilder.Sql("""
                ALTER TABLE rights_grants ADD CONSTRAINT ck_rights_grants_period_shape
                CHECK (
                    CASE period_kind
                        WHEN 1 THEN ends_on IS NULL
                        WHEN 2 THEN starts_on IS NOT NULL AND ends_on IS NOT NULL
                        WHEN 3 THEN ends_on IS NULL
                        WHEN 4 THEN starts_on IS NULL AND ends_on IS NULL
                        ELSE false
                    END);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE rights_grants ADD CONSTRAINT ck_rights_grants_period_order
                CHECK (starts_on IS NULL OR ends_on IS NULL OR ends_on >= starts_on);
                """);

            // Status 2 is Superseded. A grant that names its replacement is
            // superseded, and one that is superseded names its replacement.
            migrationBuilder.Sql("""
                ALTER TABLE rights_grants ADD CONSTRAINT ck_rights_grants_supersession
                CHECK ((superseded_by_grant_id IS NOT NULL) = (status = 2));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE rights_grants ADD CONSTRAINT ck_rights_grants_no_self_supersede
                CHECK (superseded_by_grant_id IS NULL OR superseded_by_grant_id <> id);
                """);

            // ---- Deadline rules ---------------------------------------------
            //
            // Kind 1 is a stated date, 2 an offset from an event, 3 wording this
            // build cannot structure. The third is the honest one: a clause that
            // says "promptly following delivery" is recorded as those words and
            // resolves to no date at all, rather than being flattened onto a day
            // nobody agreed to (ADR-0022).
            foreach ((string table, string prefix) in new[]
            {
                ("contract_options", "deadline"),
                ("obligations", "due"),
                ("notice_requirements", "due"),
            })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT ck_{table}_{prefix}_shape
                    CHECK (
                        CASE {prefix}_kind
                            WHEN 1 THEN {prefix}_on IS NOT NULL
                                AND {prefix}_anchor IS NULL AND {prefix}_offset IS NULL
                                AND {prefix}_unit IS NULL
                            WHEN 2 THEN {prefix}_anchor IS NOT NULL AND {prefix}_offset IS NOT NULL
                                AND {prefix}_unit IS NOT NULL AND {prefix}_on IS NULL
                            WHEN 3 THEN {prefix}_description IS NOT NULL
                                AND {prefix}_on IS NULL AND {prefix}_anchor IS NULL
                                AND {prefix}_offset IS NULL AND {prefix}_unit IS NULL
                            ELSE false
                        END);
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT ck_{table}_{prefix}_offset_positive
                    CHECK ({prefix}_offset IS NULL OR {prefix}_offset > 0);
                    """);
            }

            // ---- Historical immutability -------------------------------------
            //
            // One trigger, and only one. A drafting version that has been recorded
            // is a milestone somebody read and formed a view on, so the terms
            // transcribed from it are frozen; correcting a transcription means
            // recording the next version, which is what a lawyer does anyway.
            //
            // The domain refuses this first. The trigger exists so a future code
            // path that forgets fails loudly instead of quietly rewriting what a
            // draft is recorded as having said (ADR-0021, ADR-0022).
            //
            // Status 1 is Draft. A null parent means the version itself is being
            // deleted in this transaction, so the cascade is allowed through.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_contract_terms_immutable()
                RETURNS trigger AS $$
                DECLARE
                    target uuid;
                    parent_status integer;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        target := OLD.contract_version_id;
                    ELSE
                        target := NEW.contract_version_id;
                    END IF;

                    SELECT status INTO parent_status
                    FROM contract_versions
                    WHERE id = target;

                    IF parent_status IS NOT NULL AND parent_status <> 1 THEN
                        RAISE EXCEPTION
                            'contract version % has been recorded; its terms are immutable',
                            target
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
                CREATE TRIGGER trg_contract_terms_immutable
                BEFORE UPDATE OR DELETE ON contract_terms
                FOR EACH ROW EXECUTE FUNCTION agencyos_contract_terms_immutable();
                """);

            // ---- Work queues -------------------------------------------------
            //
            // Partial indexes over the rows a legal desk actually reads: options
            // still available and obligations still outstanding, ordered by the
            // date their rule resolved to. Rows whose deadline could not be
            // resolved carry a null and are absent from both, which is the same
            // answer the queries give.
            //
            // Option status 1 is Available; obligation status 1 is Pending.
            migrationBuilder.Sql("""
                CREATE INDEX ix_contract_options_open_deadline
                ON contract_options (organization_id, resolved_deadline_on)
                WHERE status = 1 AND resolved_deadline_on IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_obligations_outstanding_due
                ON obligations (organization_id, resolved_due_on)
                WHERE status = 1 AND resolved_due_on IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_notice_requirements_open_due
                ON notice_requirements (organization_id, resolved_due_on)
                WHERE resolved_due_on IS NOT NULL;
                """);

            // ---- Search -------------------------------------------------------
            //
            // Contracts join the M3 ranked search on their title, reference and
            // factual summary. 'simple' rather than 'english', for the reason M2
            // gave: these are names and references, and stemming them loses the
            // thing being searched for.
            //
            // legal_analysis and strategy_notes are absent, and so is every term
            // value. A caller without contracts.privileged.read must not be able
            // to confirm that counsel wrote something by searching for a phrase
            // and watching a contract surface, and a caller without
            // deals.economics.read must not be able to confirm a figure the same
            // way. A redaction that leaves a search hit behind is not a redaction
            // (ADR-0021, ADR-0022).
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(reference, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(summary, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_contracts_search_vector ON contracts USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_contracts_title_trgm ON contracts USING gin (title gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_contracts_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_contracts_search_vector;");
            migrationBuilder.Sql("ALTER TABLE contracts DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_notice_requirements_open_due;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_obligations_outstanding_due;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_contract_options_open_deadline;");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_contract_terms_immutable ON contract_terms;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_contract_terms_immutable();");

            migrationBuilder.Sql(
                "ALTER TABLE contracts DROP CONSTRAINT IF EXISTS fk_contracts_offer_same_deal;");

            migrationBuilder.DropTable(
                name: "contract_events");

            migrationBuilder.DropTable(
                name: "contract_relationships");

            migrationBuilder.DropTable(
                name: "contract_signatures");

            migrationBuilder.DropTable(
                name: "contract_task_links");

            migrationBuilder.DropTable(
                name: "contract_terms");

            migrationBuilder.DropTable(
                name: "notice_records");

            migrationBuilder.DropTable(
                name: "obligation_events");

            migrationBuilder.DropTable(
                name: "option_events");

            migrationBuilder.DropTable(
                name: "notice_requirements");

            migrationBuilder.DropTable(
                name: "obligations");

            migrationBuilder.DropTable(
                name: "contract_options");

            migrationBuilder.DropTable(
                name: "rights_grants");

            migrationBuilder.DropTable(
                name: "contract_parties");

            migrationBuilder.DropTable(
                name: "contract_versions");

            migrationBuilder.DropTable(
                name: "contracts");
        }
    }
}
