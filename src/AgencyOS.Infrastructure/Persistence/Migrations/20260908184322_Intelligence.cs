using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Intelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "intelligence_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    radar_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    research_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thesis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    watchlist_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intelligence_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "intelligence_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reliability = table.Column<int>(type: "integer", nullable: false),
                    reliability_rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reliability_assessed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reliability_assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intelligence_sources", x => x.id);
                    table.UniqueConstraint("AK_intelligence_sources_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "predictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    resolution_criteria = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    resolves_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_predictions", x => x.id);
                    table.UniqueConstraint("AK_predictions_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "research_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    context = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_cases", x => x.id);
                    table.UniqueConstraint("AK_research_cases_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    claim = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verification = table.Column<int>(type: "integer", nullable: false),
                    verification_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    verification_changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verification_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signals", x => x.id);
                    table.UniqueConstraint("AK_signals_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "talent_radar_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    intended_disciplines = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    prospect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    converted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    dismissed_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_talent_radar_entries", x => x.id);
                    table.UniqueConstraint("AK_talent_radar_entries_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "theses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    proposition = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    rationale = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    superseded_by_thesis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_theses", x => x.id);
                    table.UniqueConstraint("AK_theses_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "watchlists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensitivity = table.Column<int>(type: "integer", nullable: false),
                    last_reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlists", x => x.id);
                    table.UniqueConstraint("AK_watchlists_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "prediction_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prediction_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_prediction_evidence_predictions_organization_id_prediction_~",
                        columns: x => new { x.organization_id, x.prediction_id },
                        principalTable: "predictions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prediction_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    probability = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prediction_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_prediction_revisions_predictions_organization_id_prediction~",
                        columns: x => new { x.organization_id, x.prediction_id },
                        principalTable: "predictions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_case_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    linked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thesis_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_case_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_case_links_research_cases_organization_id_research~",
                        columns: x => new { x.organization_id, x.research_case_id },
                        principalTable: "research_cases",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "signal_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    excerpt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    locator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signal_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_signal_evidence_signals_organization_id_signal_id",
                        columns: x => new { x.organization_id, x.signal_id },
                        principalTable: "signals",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thesis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stance = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_thesis_evidence_theses_organization_id_thesis_id",
                        columns: x => new { x.organization_id, x.thesis_id },
                        principalTable: "theses",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thesis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    proposition = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    rationale = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    change_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_thesis_revisions_theses_organization_id_thesis_id",
                        columns: x => new { x.organization_id, x.thesis_id },
                        principalTable: "theses",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intelligence_subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_kind = table.Column<int>(type: "integer", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prediction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    research_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thesis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    watchlist_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intelligence_subjects", x => x.id);
                    table.ForeignKey(
                        name: "FK_intelligence_subjects_predictions_organization_id_predictio~",
                        columns: x => new { x.organization_id, x.prediction_id },
                        principalTable: "predictions",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_intelligence_subjects_research_cases_organization_id_resear~",
                        columns: x => new { x.organization_id, x.research_case_id },
                        principalTable: "research_cases",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_intelligence_subjects_signals_organization_id_signal_id",
                        columns: x => new { x.organization_id, x.signal_id },
                        principalTable: "signals",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_intelligence_subjects_theses_organization_id_thesis_id",
                        columns: x => new { x.organization_id, x.thesis_id },
                        principalTable: "theses",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_intelligence_subjects_watchlists_organization_id_watchlist_~",
                        columns: x => new { x.organization_id, x.watchlist_id },
                        principalTable: "watchlists",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_events_organization_occurred",
                table: "intelligence_events",
                columns: new[] { "organization_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_events_owner",
                table: "intelligence_events",
                columns: new[] { "organization_id", "owner_kind", "owner_id", "occurred_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_sources_organization_kind",
                table: "intelligence_sources",
                columns: new[] { "organization_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_sources_organization_observed",
                table: "intelligence_sources",
                columns: new[] { "organization_id", "observed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_intelligence_subjects_organization_id_prediction_id",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "prediction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_intelligence_subjects_organization_id_research_case_id",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "research_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_intelligence_subjects_organization_id_signal_id",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "signal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_intelligence_subjects_organization_id_thesis_id",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "thesis_id" });

            migrationBuilder.CreateIndex(
                name: "IX_intelligence_subjects_organization_id_watchlist_id",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "watchlist_id" });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_subjects_owner_subject",
                table: "intelligence_subjects",
                columns: new[] { "owner_kind", "subject_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_intelligence_subjects_subject",
                table: "intelligence_subjects",
                columns: new[] { "organization_id", "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "IX_prediction_evidence_organization_id_prediction_id",
                table: "prediction_evidence",
                columns: new[] { "organization_id", "prediction_id" });

            migrationBuilder.CreateIndex(
                name: "ix_prediction_evidence_prediction",
                table: "prediction_evidence",
                column: "prediction_id");

            migrationBuilder.CreateIndex(
                name: "IX_prediction_revisions_organization_id_prediction_id",
                table: "prediction_revisions",
                columns: new[] { "organization_id", "prediction_id" });

            migrationBuilder.CreateIndex(
                name: "ux_prediction_revisions_prediction_sequence",
                table: "prediction_revisions",
                columns: new[] { "prediction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_predictions_organization_outcome",
                table: "predictions",
                columns: new[] { "organization_id", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_predictions_organization_owner",
                table: "predictions",
                columns: new[] { "organization_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_predictions_organization_resolves",
                table: "predictions",
                columns: new[] { "organization_id", "resolves_by" },
                filter: "outcome IS NULL AND cancelled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_research_case_links_organization_id_research_case_id",
                table: "research_case_links",
                columns: new[] { "organization_id", "research_case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_research_case_links_target",
                table: "research_case_links",
                columns: new[] { "organization_id", "kind", "linked_id" });

            migrationBuilder.CreateIndex(
                name: "ux_research_case_links_case_target",
                table: "research_case_links",
                columns: new[] { "research_case_id", "kind", "linked_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_research_cases_organization_status",
                table: "research_cases",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_signal_evidence_organization_id_signal_id",
                table: "signal_evidence",
                columns: new[] { "organization_id", "signal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_signal_evidence_source",
                table: "signal_evidence",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ux_signal_evidence_signal_source",
                table: "signal_evidence",
                columns: new[] { "signal_id", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signals_organization_observed",
                table: "signals",
                columns: new[] { "organization_id", "observed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_signals_organization_sensitivity",
                table: "signals",
                columns: new[] { "organization_id", "sensitivity" });

            migrationBuilder.CreateIndex(
                name: "ix_signals_organization_verification",
                table: "signals",
                columns: new[] { "organization_id", "verification" });

            migrationBuilder.CreateIndex(
                name: "ix_talent_radar_organization_reviewed",
                table: "talent_radar_entries",
                columns: new[] { "organization_id", "last_reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_talent_radar_organization_status",
                table: "talent_radar_entries",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_talent_radar_open_person",
                table: "talent_radar_entries",
                columns: new[] { "organization_id", "person_id" },
                unique: true,
                filter: "status IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_theses_organization_status",
                table: "theses",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_theses_organization_updated",
                table: "theses",
                columns: new[] { "organization_id", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_thesis_evidence_organization_id_thesis_id",
                table: "thesis_evidence",
                columns: new[] { "organization_id", "thesis_id" });

            migrationBuilder.CreateIndex(
                name: "ix_thesis_evidence_signal",
                table: "thesis_evidence",
                column: "signal_id");

            migrationBuilder.CreateIndex(
                name: "ux_thesis_evidence_thesis_signal",
                table: "thesis_evidence",
                columns: new[] { "thesis_id", "signal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thesis_revisions_organization_id_thesis_id",
                table: "thesis_revisions",
                columns: new[] { "organization_id", "thesis_id" });

            migrationBuilder.CreateIndex(
                name: "ux_thesis_revisions_thesis_sequence",
                table: "thesis_revisions",
                columns: new[] { "thesis_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_watchlists_organization_status",
                table: "watchlists",
                columns: new[] { "organization_id", "status" });

            // ---- Tenant-safe arcs, coherence, immutability and search (M11) ----
            // 
            // Everything below is SQL because none of it is expressible in the EF
            // model: composite foreign keys through alternate keys, exclusive-arc
            // checks, cross-column coherence, triggers that refuse to rewrite a stated
            // position, and generated search vectors.
            // A subject arc points at a real record in the same tenant. Ten typed
            // columns, each carrying a composite key, rather than an untyped
            // (kind, guid) pair that cheerfully points at deleted or foreign-tenant
            // rows. Adding an eleventh subject kind is deliberately not free
            // (ADR-0011, ADR-0025, §59).
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_person_id
                    FOREIGN KEY (organization_id, person_id)
                    REFERENCES people (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_company_id
                    FOREIGN KEY (organization_id, company_id)
                    REFERENCES companies (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_talent_profile_id
                    FOREIGN KEY (organization_id, talent_profile_id)
                    REFERENCES talent_profiles (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_project_id
                    FOREIGN KEY (organization_id, project_id)
                    REFERENCES projects (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_source_property_id
                    FOREIGN KEY (organization_id, source_property_id)
                    REFERENCES source_properties (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_package_id
                    FOREIGN KEY (organization_id, package_id)
                    REFERENCES packages (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_project_role_id
                    FOREIGN KEY (organization_id, project_role_id)
                    REFERENCES project_roles (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_opportunity_id
                    FOREIGN KEY (organization_id, opportunity_id)
                    REFERENCES opportunities (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_deal_id
                    FOREIGN KEY (organization_id, deal_id)
                    REFERENCES deals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT fk_intelligence_subjects_contract_id
                    FOREIGN KEY (organization_id, contract_id)
                    REFERENCES contracts (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Exactly one typed subject column, it must be the one the kind names, and
            // it must hold the same identifier as subject_id. Without this a subject
            // could carry a stale value in a second column and name two records at
            // once.
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT ck_intelligence_subjects_subject_arc CHECK (
                    ((CASE WHEN person_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN company_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN talent_profile_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN project_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN source_property_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN package_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN project_role_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN opportunity_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN deal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN contract_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE subject_kind
                                        WHEN 1 THEN (person_id IS NOT NULL AND person_id = subject_id)
                                        WHEN 2 THEN (company_id IS NOT NULL AND company_id = subject_id)
                                        WHEN 3 THEN (talent_profile_id IS NOT NULL AND talent_profile_id = subject_id)
                                        WHEN 4 THEN (project_id IS NOT NULL AND project_id = subject_id)
                                        WHEN 5 THEN (source_property_id IS NOT NULL AND source_property_id = subject_id)
                                        WHEN 6 THEN (package_id IS NOT NULL AND package_id = subject_id)
                                        WHEN 7 THEN (project_role_id IS NOT NULL AND project_role_id = subject_id)
                                        WHEN 8 THEN (opportunity_id IS NOT NULL AND opportunity_id = subject_id)
                                        WHEN 9 THEN (deal_id IS NOT NULL AND deal_id = subject_id)
                                        WHEN 10 THEN (contract_id IS NOT NULL AND contract_id = subject_id)
                        ELSE false
                    END
                );
                """);
            // And exactly one owner. The five kinds of intelligence share this table so
            // "what does the agency know about this person" is one scan rather than
            // five, which only holds if a row belongs to exactly one of them.
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_subjects
                ADD CONSTRAINT ck_intelligence_subjects_owner_arc CHECK (
                    ((CASE WHEN signal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN thesis_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN prediction_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN watchlist_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN research_case_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE owner_kind
                                        WHEN 2 THEN signal_id IS NOT NULL
                                        WHEN 3 THEN thesis_id IS NOT NULL
                                        WHEN 4 THEN prediction_id IS NOT NULL
                                        WHEN 5 THEN watchlist_id IS NOT NULL
                                        WHEN 7 THEN research_case_id IS NOT NULL
                        ELSE false
                    END
                );
                """);
            // Named twice, listed once. A watchlist holding the same person twice is a
            // duplicate row rather than a second interest, and a signal about the same
            // project twice says nothing new.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_intelligence_subjects_signal
                ON intelligence_subjects (signal_id, subject_kind, subject_id)
                WHERE signal_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_intelligence_subjects_thesis
                ON intelligence_subjects (thesis_id, subject_kind, subject_id)
                WHERE thesis_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_intelligence_subjects_prediction
                ON intelligence_subjects (prediction_id, subject_kind, subject_id)
                WHERE prediction_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_intelligence_subjects_watchlist
                ON intelligence_subjects (watchlist_id, subject_kind, subject_id)
                WHERE watchlist_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_intelligence_subjects_research_case
                ON intelligence_subjects (research_case_id, subject_kind, subject_id)
                WHERE research_case_id IS NOT NULL;
                """);
            // A research case gathers real records, in its own tenant.
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT fk_research_case_links_source_id
                    FOREIGN KEY (organization_id, source_id)
                    REFERENCES intelligence_sources (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT fk_research_case_links_signal_id
                    FOREIGN KEY (organization_id, signal_id)
                    REFERENCES signals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT fk_research_case_links_thesis_id
                    FOREIGN KEY (organization_id, thesis_id)
                    REFERENCES theses (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT fk_research_case_links_prediction_id
                    FOREIGN KEY (organization_id, prediction_id)
                    REFERENCES predictions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT fk_research_case_links_task_id
                    FOREIGN KEY (organization_id, task_id)
                    REFERENCES tasks (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE research_case_links
                ADD CONSTRAINT ck_research_case_links_arc CHECK (
                    ((CASE WHEN source_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN signal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN thesis_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN prediction_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN task_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE kind
                                        WHEN 1 THEN (source_id IS NOT NULL AND source_id = linked_id)
                                        WHEN 2 THEN (signal_id IS NOT NULL AND signal_id = linked_id)
                                        WHEN 3 THEN (thesis_id IS NOT NULL AND thesis_id = linked_id)
                                        WHEN 4 THEN (prediction_id IS NOT NULL AND prediction_id = linked_id)
                                        WHEN 5 THEN (task_id IS NOT NULL AND task_id = linked_id)
                        ELSE false
                    END
                );
                """);
            // Curated history belongs to exactly one object, in the same tenant.
            // 
            // These seven are DEFERRABLE INITIALLY DEFERRED, and alone among the arcs they
            // have to be. An event is written in the same unit of work as the thing it
            // happened to; EF orders inserts by the relationships it knows about, knows
            // about none of these, and wrote the history row before the thesis. Checking
            // at commit rather than at statement is exactly what deferred constraints are
            // for, and it keeps the key rather than trading it for insert ordering
            // (ADR-0011, ADR-0012).
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_source_id
                    FOREIGN KEY (organization_id, source_id)
                    REFERENCES intelligence_sources (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_signal_id
                    FOREIGN KEY (organization_id, signal_id)
                    REFERENCES signals (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_thesis_id
                    FOREIGN KEY (organization_id, thesis_id)
                    REFERENCES theses (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_prediction_id
                    FOREIGN KEY (organization_id, prediction_id)
                    REFERENCES predictions (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_watchlist_id
                    FOREIGN KEY (organization_id, watchlist_id)
                    REFERENCES watchlists (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_radar_entry_id
                    FOREIGN KEY (organization_id, radar_entry_id)
                    REFERENCES talent_radar_entries (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT fk_intelligence_events_research_case_id
                    FOREIGN KEY (organization_id, research_case_id)
                    REFERENCES research_cases (organization_id, id)
                    ON DELETE CASCADE
                    DEFERRABLE INITIALLY DEFERRED;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_events
                ADD CONSTRAINT ck_intelligence_events_owner_arc CHECK (
                    ((CASE WHEN source_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN signal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN thesis_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN prediction_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN watchlist_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN radar_entry_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN research_case_id IS NULL THEN 0 ELSE 1 END)) = 1
                    AND CASE owner_kind
                                        WHEN 1 THEN (source_id IS NOT NULL AND source_id = owner_id)
                                        WHEN 2 THEN (signal_id IS NOT NULL AND signal_id = owner_id)
                                        WHEN 3 THEN (thesis_id IS NOT NULL AND thesis_id = owner_id)
                                        WHEN 4 THEN (prediction_id IS NOT NULL AND prediction_id = owner_id)
                                        WHEN 5 THEN (watchlist_id IS NOT NULL AND watchlist_id = owner_id)
                                        WHEN 6 THEN (radar_entry_id IS NOT NULL AND radar_entry_id = owner_id)
                                        WHEN 7 THEN (research_case_id IS NOT NULL AND research_case_id = owner_id)
                        ELSE false
                    END
                );
                """);
            // Evidence points at evidence, in the same tenant. A signal cannot cite
            // another organization's source, and a thesis cannot rest on another
            // organization's signal (§66).
            migrationBuilder.Sql("""
                ALTER TABLE signal_evidence
                ADD CONSTRAINT fk_signal_evidence_source_id
                    FOREIGN KEY (organization_id, source_id)
                    REFERENCES intelligence_sources (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE thesis_evidence
                ADD CONSTRAINT fk_thesis_evidence_signal_id
                    FOREIGN KEY (organization_id, signal_id)
                    REFERENCES signals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE prediction_evidence
                ADD CONSTRAINT fk_prediction_evidence_source_id
                    FOREIGN KEY (organization_id, source_id)
                    REFERENCES intelligence_sources (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE prediction_evidence
                ADD CONSTRAINT fk_prediction_evidence_signal_id
                    FOREIGN KEY (organization_id, signal_id)
                    REFERENCES signals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // A prediction cites a source or a signal, never both and never neither.
            // A citation of nothing is not provenance.
            migrationBuilder.Sql("""
                ALTER TABLE prediction_evidence
                ADD CONSTRAINT ck_prediction_evidence_arc CHECK (
                    ((CASE WHEN source_id IS NULL THEN 0 ELSE 1 END)
                     + (CASE WHEN signal_id IS NULL THEN 0 ELSE 1 END)) = 1
                );
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_prediction_evidence_source
                ON prediction_evidence (prediction_id, source_id)
                WHERE source_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_prediction_evidence_signal
                ON prediction_evidence (prediction_id, signal_id)
                WHERE signal_id IS NOT NULL;
                """);
            // A source that claims AgencyOS holds the evidence must name what it holds.
            // The first two kinds point at M10 artifacts in the same tenant; the rest
            // are references to things AgencyOS does not have and never claims to
            // (ADR-0030).
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_sources
                ADD CONSTRAINT fk_intelligence_sources_document_version
                    FOREIGN KEY (organization_id, document_version_id)
                    REFERENCES document_versions (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_sources
                ADD CONSTRAINT fk_intelligence_sources_message
                    FOREIGN KEY (organization_id, message_id)
                    REFERENCES communication_messages (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_sources
                ADD CONSTRAINT ck_intelligence_sources_kind CHECK (
                    CASE kind
                        WHEN 1 THEN (document_version_id IS NOT NULL
                            AND message_id IS NULL AND url IS NULL)
                        WHEN 2 THEN (message_id IS NOT NULL
                            AND document_version_id IS NULL AND url IS NULL)
                        WHEN 3 THEN (url IS NOT NULL
                            AND document_version_id IS NULL AND message_id IS NULL)
                        ELSE (document_version_id IS NULL AND message_id IS NULL)
                    END
                );
                """);
            // A reliability rating is somebody's judgment, so it is never separable
            // from who made it and when. Unassessed is the honest default and carries
            // neither.
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_sources
                ADD CONSTRAINT ck_intelligence_sources_reliability CHECK (
                    (reliability = 0
                        AND reliability_assessed_at IS NULL
                        AND reliability_assessed_by IS NULL)
                    OR (reliability > 0
                        AND reliability_assessed_at IS NOT NULL
                        AND reliability_assessed_by IS NOT NULL)
                );
                """);
            // A probability is a decimal from zero to one. Stored as numeric and never
            // as a float, so 0.1 is still 0.1 when a Brier score is computed from it
            // (constitution §5, §11).
            migrationBuilder.Sql("""
                ALTER TABLE prediction_revisions
                ADD CONSTRAINT ck_prediction_revisions_probability CHECK (
                    probability >= 0 AND probability <= 1
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE prediction_revisions
                ADD CONSTRAINT ck_prediction_revisions_sequence CHECK (sequence >= 1);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE thesis_revisions
                ADD CONSTRAINT ck_thesis_revisions_sequence CHECK (sequence >= 1);
                """);
            // A resolution is an event: an outcome, a moment and a person, together or
            // not at all. Half a resolution would score a forecast nobody closed.
            migrationBuilder.Sql("""
                ALTER TABLE predictions
                ADD CONSTRAINT ck_predictions_resolution CHECK (
                    (outcome IS NULL AND resolved_at IS NULL AND resolved_by IS NULL)
                    OR (outcome IS NOT NULL AND resolved_at IS NOT NULL
                        AND resolved_by IS NOT NULL)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE predictions
                ADD CONSTRAINT ck_predictions_cancellation CHECK (
                    (cancelled_at IS NULL AND cancelled_reason IS NULL)
                    OR (cancelled_at IS NOT NULL AND cancelled_reason IS NOT NULL)
                );
                """);
            // Resolved or cancelled, never both. A cancelled question was never
            // answered, and a resolved one cannot be withdrawn afterwards to improve a
            // calibration figure.
            migrationBuilder.Sql("""
                ALTER TABLE predictions
                ADD CONSTRAINT ck_predictions_not_both CHECK (
                    NOT (outcome IS NOT NULL AND cancelled_at IS NOT NULL)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE theses
                ADD CONSTRAINT fk_theses_superseded_by
                    FOREIGN KEY (organization_id, superseded_by_thesis_id)
                    REFERENCES theses (organization_id, id)
                    ON DELETE RESTRICT;
                """);
            // Retired and superseded both say why and when. A thesis that quietly
            // stopped being held leaves nobody able to say what changed.
            migrationBuilder.Sql("""
                ALTER TABLE theses
                ADD CONSTRAINT ck_theses_closure CHECK (
                    (closed_at IS NULL AND closed_reason IS NULL AND status IN (1, 2))
                    OR (closed_at IS NOT NULL AND closed_reason IS NOT NULL
                        AND status IN (3, 4))
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE theses
                ADD CONSTRAINT ck_theses_supersession CHECK (
                    (superseded_by_thesis_id IS NULL AND status <> 4)
                    OR (superseded_by_thesis_id IS NOT NULL
                        AND status = 4
                        AND superseded_by_thesis_id <> id)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE talent_radar_entries
                ADD CONSTRAINT fk_talent_radar_entries_person_id
                    FOREIGN KEY (organization_id, person_id)
                    REFERENCES people (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE talent_radar_entries
                ADD CONSTRAINT fk_talent_radar_entries_prospect_id
                    FOREIGN KEY (organization_id, prospect_id)
                    REFERENCES prospects (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE talent_radar_entries
                ADD CONSTRAINT fk_talent_radar_entries_talent_profile_id
                    FOREIGN KEY (organization_id, talent_profile_id)
                    REFERENCES talent_profiles (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // A converted entry names the prospect it became. Without that the radar
            // and M4 would each believe they own the same pursuit.
            migrationBuilder.Sql("""
                ALTER TABLE talent_radar_entries
                ADD CONSTRAINT ck_talent_radar_conversion CHECK (
                    (converted_at IS NULL AND converted_by IS NULL AND prospect_id IS NULL)
                    OR (converted_at IS NOT NULL
                        AND converted_by IS NOT NULL
                        AND prospect_id IS NOT NULL
                        AND status = 4)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE talent_radar_entries
                ADD CONSTRAINT ck_talent_radar_dismissal CHECK (
                    (dismissed_at IS NULL AND dismissed_reason IS NULL)
                    OR (dismissed_at IS NOT NULL AND dismissed_reason IS NOT NULL
                        AND status = 5)
                );
                """);
            // A revision is what somebody stated at a moment. Rewriting one would
            // rewrite the record of what the agency believed and when, which is the
            // entire value of keeping revisions rather than a current-value column
            // (§10, ADR-0030).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_thesis_revisions_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF EXISTS (SELECT 1 FROM theses WHERE id = OLD.thesis_id) THEN
                            RAISE EXCEPTION
                                'A thesis revision records a position held at a moment. Revise '
                                'the thesis instead of deleting what it used to say.';
                        END IF;

                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION
                        'A thesis revision cannot be edited. Record revision N+1 instead of '
                        'rewriting revision N.';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_thesis_revisions_immutable
                BEFORE UPDATE OR DELETE ON thesis_revisions
                FOR EACH ROW EXECUTE FUNCTION agencyos_thesis_revisions_immutable();
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_prediction_revisions_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF EXISTS (SELECT 1 FROM predictions WHERE id = OLD.prediction_id) THEN
                            RAISE EXCEPTION
                                'A forecast is what somebody stated on a date. Deleting one '
                                'would improve a calibration score by erasing the evidence '
                                'against it.';
                        END IF;

                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION
                        'A forecast cannot be edited. State a new probability instead of '
                        'changing the one already on the record.';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_prediction_revisions_immutable
                BEFORE UPDATE OR DELETE ON prediction_revisions
                FOR EACH ROW EXECUTE FUNCTION agencyos_prediction_revisions_immutable();
                """);
            // The provenance rule, enforced where it cannot be bypassed. A claim with
            // no source is a rumour with a database row, and the whole of M11 exists to
            // refuse that (§1).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_signal_evidence_last_source()
                RETURNS trigger AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM signals WHERE id = OLD.signal_id) THEN
                        RETURN OLD;
                    END IF;

                    IF (SELECT count(*) FROM signal_evidence
                        WHERE signal_id = OLD.signal_id) <= 1 THEN
                        RAISE EXCEPTION
                            'A signal keeps at least one source. Retract the signal instead of '
                            'leaving the claim with no provenance.';
                    END IF;

                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_signal_evidence_last_source
                BEFORE DELETE ON signal_evidence
                FOR EACH ROW EXECUTE FUNCTION agencyos_signal_evidence_last_source();
                """);
            // ---- Search ----
            // 
            // What an analyst wrote, and nothing quoted from somewhere else. Excerpts
            // are deliberately not indexed: an excerpt is a quotation from an M10
            // artifact, and a search hit inside a privileged contract would report its
            // contents to whoever ran the search. Notes are excluded for the same
            // reason a source-sensitive note exists at all (ADR-0025, ADR-0030).
            migrationBuilder.Sql("""
                ALTER TABLE signals
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('simple', coalesce(claim, '')), 'B')
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_signals_search ON signals USING GIN (search_vector);");
            migrationBuilder.Sql("""
                ALTER TABLE theses
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('simple', coalesce(proposition, '')), 'B')
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_theses_search ON theses USING GIN (search_vector);");
            migrationBuilder.Sql("""
                ALTER TABLE predictions
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('simple', coalesce(statement, ''))
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_predictions_search ON predictions USING GIN (search_vector);");
            migrationBuilder.Sql("""
                ALTER TABLE intelligence_sources
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('simple', coalesce(publisher, '')), 'B') ||
                    setweight(to_tsvector('simple', coalesce(author, '')), 'B')
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_intelligence_sources_search ON intelligence_sources USING GIN (search_vector);");
            migrationBuilder.Sql("""
                ALTER TABLE research_cases
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('simple', coalesce(question, ''))
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_research_cases_search ON research_cases USING GIN (search_vector);");
            migrationBuilder.Sql("""
                ALTER TABLE watchlists
                ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('simple', coalesce(name, ''))
                ) STORED;
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_watchlists_search ON watchlists USING GIN (search_vector);");
            // ---- Work queues ----
            // 
            // Partial indexes over the states the intelligence desk actually opens. A
            // disputed signal and an entry waiting to be reviewed are small minorities
            // of their tables, and a full index would mostly hold rows nobody queries.
            migrationBuilder.Sql("""
                CREATE INDEX ix_signals_disputed
                ON signals (organization_id, observed_at DESC)
                WHERE verification = 3;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_talent_radar_ready
                ON talent_radar_entries (organization_id, priority DESC, last_reviewed_at)
                WHERE status = 3;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_research_cases_open
                ON research_cases (organization_id, opened_at)
                WHERE status = 1;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_watchlists_unreviewed
                ON watchlists (organization_id, last_reviewed_at NULLS FIRST)
                WHERE status = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers and their functions first, then every foreign key declared in
            // SQL. EF orders its own DropTable calls around the relationships it knows
            // about, and it knows about none of these, so without this the reverse path
            // stops halfway and leaves the schema half-dismantled.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_signal_evidence_last_source ON signal_evidence;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_prediction_revisions_immutable ON prediction_revisions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_thesis_revisions_immutable ON thesis_revisions;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_signal_evidence_last_source();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_prediction_revisions_immutable();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_thesis_revisions_immutable();");

            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_person_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_company_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_talent_profile_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_project_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_source_property_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_package_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_project_role_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_opportunity_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_deal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_subjects "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_subjects_contract_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_source_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_signal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_thesis_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_prediction_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_watchlist_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_radar_entry_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_events "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_events_research_case_id;");
            migrationBuilder.Sql(
                "ALTER TABLE research_case_links "
                    + "DROP CONSTRAINT IF EXISTS fk_research_case_links_source_id;");
            migrationBuilder.Sql(
                "ALTER TABLE research_case_links "
                    + "DROP CONSTRAINT IF EXISTS fk_research_case_links_signal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE research_case_links "
                    + "DROP CONSTRAINT IF EXISTS fk_research_case_links_thesis_id;");
            migrationBuilder.Sql(
                "ALTER TABLE research_case_links "
                    + "DROP CONSTRAINT IF EXISTS fk_research_case_links_prediction_id;");
            migrationBuilder.Sql(
                "ALTER TABLE research_case_links "
                    + "DROP CONSTRAINT IF EXISTS fk_research_case_links_task_id;");
            migrationBuilder.Sql(
                "ALTER TABLE signal_evidence "
                    + "DROP CONSTRAINT IF EXISTS fk_signal_evidence_source_id;");
            migrationBuilder.Sql(
                "ALTER TABLE thesis_evidence "
                    + "DROP CONSTRAINT IF EXISTS fk_thesis_evidence_signal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE prediction_evidence "
                    + "DROP CONSTRAINT IF EXISTS fk_prediction_evidence_source_id;");
            migrationBuilder.Sql(
                "ALTER TABLE prediction_evidence "
                    + "DROP CONSTRAINT IF EXISTS fk_prediction_evidence_signal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_sources "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_sources_document_version;");
            migrationBuilder.Sql(
                "ALTER TABLE intelligence_sources "
                    + "DROP CONSTRAINT IF EXISTS fk_intelligence_sources_message;");
            migrationBuilder.Sql(
                "ALTER TABLE theses "
                    + "DROP CONSTRAINT IF EXISTS fk_theses_superseded_by;");
            migrationBuilder.Sql(
                "ALTER TABLE talent_radar_entries "
                    + "DROP CONSTRAINT IF EXISTS fk_talent_radar_entries_person_id;");
            migrationBuilder.Sql(
                "ALTER TABLE talent_radar_entries "
                    + "DROP CONSTRAINT IF EXISTS fk_talent_radar_entries_prospect_id;");
            migrationBuilder.Sql(
                "ALTER TABLE talent_radar_entries "
                    + "DROP CONSTRAINT IF EXISTS fk_talent_radar_entries_talent_profile_id;");

            migrationBuilder.DropTable(
                name: "intelligence_events");

            migrationBuilder.DropTable(
                name: "intelligence_sources");

            migrationBuilder.DropTable(
                name: "intelligence_subjects");

            migrationBuilder.DropTable(
                name: "prediction_evidence");

            migrationBuilder.DropTable(
                name: "prediction_revisions");

            migrationBuilder.DropTable(
                name: "research_case_links");

            migrationBuilder.DropTable(
                name: "signal_evidence");

            migrationBuilder.DropTable(
                name: "talent_radar_entries");

            migrationBuilder.DropTable(
                name: "thesis_evidence");

            migrationBuilder.DropTable(
                name: "thesis_revisions");

            migrationBuilder.DropTable(
                name: "watchlists");

            migrationBuilder.DropTable(
                name: "predictions");

            migrationBuilder.DropTable(
                name: "research_cases");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropTable(
                name: "theses");
        }
    }
}
