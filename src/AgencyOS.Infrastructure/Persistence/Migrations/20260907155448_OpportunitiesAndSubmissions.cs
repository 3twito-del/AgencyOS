using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpportunitiesAndSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_tasks_organization_id_id",
                table: "tasks",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    strategy_notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.id);
                    table.UniqueConstraint("AK_opportunities_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_opportunities_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunities_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_opportunity_events_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_subjects", x => x.id);
                    table.ForeignKey(
                        name: "FK_opportunity_subjects_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_subjects_packages_organization_id_package_id",
                        columns: x => new { x.organization_id, x.package_id },
                        principalTable: "packages",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_subjects_project_roles_organization_id_project_~",
                        columns: x => new { x.organization_id, x.project_role_id },
                        principalTable: "project_roles",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_subjects_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_subjects_talent_profiles_organization_id_talent~",
                        columns: x => new { x.organization_id, x.talent_profile_id },
                        principalTable: "talent_profiles",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_action_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_targets", x => x.id);
                    table.UniqueConstraint("AK_opportunity_targets_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_opportunity_targets_companies_organization_id_company_id",
                        columns: x => new { x.organization_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunity_targets_opportunities_organization_id_opportuni~",
                        columns: x => new { x.organization_id, x.opportunity_id },
                        principalTable: "opportunities",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_targets_people_organization_id_contact_person_id",
                        columns: x => new { x.organization_id, x.contact_person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunity_targets_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_pitches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_pitches", x => x.id);
                    table.UniqueConstraint("AK_opportunity_pitches_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_opportunity_pitches_interactions_interaction_id",
                        column: x => x.interaction_id,
                        principalTable: "interactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_pitches_opportunity_targets_organization_id_opp~",
                        columns: x => new { x.organization_id, x.opportunity_target_id },
                        principalTable: "opportunity_targets",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_target_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_stage = table.Column<int>(type: "integer", nullable: true),
                    to_stage = table.Column<int>(type: "integer", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pitch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_target_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_opportunity_target_events_opportunity_targets_opportunity_t~",
                        column: x => x.opportunity_target_id,
                        principalTable: "opportunity_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_task_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_task_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_opportunity_task_links_opportunities_organization_id_opport~",
                        columns: x => new { x.organization_id, x.opportunity_id },
                        principalTable: "opportunities",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_task_links_opportunity_targets_organization_id_~",
                        columns: x => new { x.organization_id, x.opportunity_target_id },
                        principalTable: "opportunity_targets",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_opportunity_task_links_tasks_organization_id_task_item_id",
                        columns: x => new { x.organization_id, x.task_item_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    response_expected_by = table.Column<DateOnly>(type: "date", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.id);
                    table.UniqueConstraint("AK_submissions_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_submissions_opportunity_targets_organization_id_opportunity~",
                        columns: x => new { x.organization_id, x.opportunity_target_id },
                        principalTable: "opportunity_targets",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pitch_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pitch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_at_pitch = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type_at_pitch = table.Column<int>(type: "integer", nullable: false),
                    version_label_at_pitch = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pitch_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_pitch_materials_materials_organization_id_material_id",
                        columns: x => new { x.organization_id, x.material_id },
                        principalTable: "materials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pitch_materials_opportunity_pitches_pitch_id",
                        column: x => x.pitch_id,
                        principalTable: "opportunity_pitches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_at_submission = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type_at_submission = table.Column<int>(type: "integer", nullable: false),
                    version_label_at_submission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_submission_materials_materials_organization_id_material_id",
                        columns: x => new { x.organization_id, x.material_id },
                        principalTable: "materials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_materials_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_organization_owner",
                table: "opportunities",
                columns: new[] { "organization_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_organization_status",
                table: "opportunities",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_organization_updated",
                table: "opportunities",
                columns: new[] { "organization_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_owner_user_id",
                table: "opportunities",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_events_opportunity_recorded",
                table: "opportunity_events",
                columns: new[] { "opportunity_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_pitches_organization_id_opportunity_target_id",
                table: "opportunity_pitches",
                columns: new[] { "organization_id", "opportunity_target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_pitches_target_occurred",
                table: "opportunity_pitches",
                columns: new[] { "opportunity_target_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_opportunity_pitches_interaction",
                table: "opportunity_pitches",
                column: "interaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_subjects_opportunity_id",
                table: "opportunity_subjects",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_subjects_organization_id_package_id",
                table: "opportunity_subjects",
                columns: new[] { "organization_id", "package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_subjects_organization_id_project_id",
                table: "opportunity_subjects",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_subjects_organization_id_project_role_id",
                table: "opportunity_subjects",
                columns: new[] { "organization_id", "project_role_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_subjects_organization_id_talent_profile_id",
                table: "opportunity_subjects",
                columns: new[] { "organization_id", "talent_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_target_events_target_occurred",
                table: "opportunity_target_events",
                columns: new[] { "opportunity_target_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_targets_opportunity_stage",
                table: "opportunity_targets",
                columns: new[] { "opportunity_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_targets_organization_id_company_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_targets_organization_id_contact_person_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "contact_person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_targets_organization_id_opportunity_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "opportunity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_targets_organization_id_person_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_targets_organization_next_action",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "next_action_on" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_task_links_opportunity",
                table: "opportunity_task_links",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_task_links_organization_id_opportunity_id",
                table: "opportunity_task_links",
                columns: new[] { "organization_id", "opportunity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_task_links_organization_id_opportunity_target_id",
                table: "opportunity_task_links",
                columns: new[] { "organization_id", "opportunity_target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_task_links_organization_id_task_item_id",
                table: "opportunity_task_links",
                columns: new[] { "organization_id", "task_item_id" });

            migrationBuilder.CreateIndex(
                name: "ux_opportunity_task_links_task",
                table: "opportunity_task_links",
                column: "task_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pitch_materials_organization_id_material_id",
                table: "pitch_materials",
                columns: new[] { "organization_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pitch_materials_pair",
                table: "pitch_materials",
                columns: new[] { "pitch_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_materials_organization_id_material_id",
                table: "submission_materials",
                columns: new[] { "organization_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ux_submission_materials_pair",
                table: "submission_materials",
                columns: new[] { "submission_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_organization_id_opportunity_target_id",
                table: "submissions",
                columns: new[] { "organization_id", "opportunity_target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_organization_response_expected",
                table: "submissions",
                columns: new[] { "organization_id", "response_expected_by" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_target_sent",
                table: "submissions",
                columns: new[] { "opportunity_target_id", "sent_at" });

            // ---- Structural invariants ----
            //
            // Everything below is expressible only in SQL. Where a rule can be made
            // impossible in the database it is, rather than being left to the
            // application to remember (CLAUDE.md section 5).

            // A target is a company or a person, never both and never neither.
            migrationBuilder.Sql("""
                ALTER TABLE opportunity_targets ADD CONSTRAINT ck_opportunity_targets_one_endpoint
                CHECK (
                    (company_id IS NOT NULL AND person_id IS NULL)
                    OR (company_id IS NULL AND person_id IS NOT NULL)
                );
                """);

            // A contact belongs to a company target. Against a person target it
            // would be either the same person or somebody with no stated
            // relationship to them.
            migrationBuilder.Sql("""
                ALTER TABLE opportunity_targets ADD CONSTRAINT ck_opportunity_targets_contact_company
                CHECK (contact_person_id IS NULL OR company_id IS NOT NULL);
                """);

            // A target cannot close before the pursuit that created it began. The
            // dates come from different commands, so nothing else guarantees it.
            migrationBuilder.Sql("""
                ALTER TABLE opportunity_targets ADD CONSTRAINT ck_opportunity_targets_closed_stage
                CHECK ((closed_on IS NULL) = (stage IN (1, 2, 3, 4, 5, 6)));
                """);

            // An opportunity that ended has an end date, and one that has not
            // ended does not. Statuses 4 and 5 are Closed and Cancelled.
            migrationBuilder.Sql("""
                ALTER TABLE opportunities ADD CONSTRAINT ck_opportunities_closed_consistent
                CHECK (
                    (status IN (4, 5) AND closed_on IS NOT NULL)
                    OR (status NOT IN (4, 5) AND closed_on IS NULL AND outcome IS NULL)
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE opportunities ADD CONSTRAINT ck_opportunities_period
                CHECK (closed_on IS NULL OR closed_on >= opened_on);
                """);

            // An outcome belongs to a closed pursuit. Cancelling says it was called
            // off, which is itself the answer.
            migrationBuilder.Sql("""
                ALTER TABLE opportunities ADD CONSTRAINT ck_opportunities_outcome_when_closed
                CHECK ((outcome IS NOT NULL) = (status = 4));
                """);

            // A subject names exactly one thing. The typed foreign keys already stop
            // it pointing at another tenant; this stops it pointing at nothing, or
            // at two things at once.
            migrationBuilder.Sql("""
                ALTER TABLE opportunity_subjects ADD CONSTRAINT ck_opportunity_subjects_one_target
                CHECK (
                    (talent_profile_id IS NOT NULL)::int
                    + (project_id IS NOT NULL)::int
                    + (package_id IS NOT NULL)::int
                    + (project_role_id IS NOT NULL)::int = 1
                );
                """);

            // And the column that is set matches the declared kind, so the two
            // cannot disagree about what the subject is.
            migrationBuilder.Sql("""
                ALTER TABLE opportunity_subjects ADD CONSTRAINT ck_opportunity_subjects_kind_matches
                CHECK (
                    (kind = 1 AND talent_profile_id IS NOT NULL)
                    OR (kind = 2 AND project_id IS NOT NULL)
                    OR (kind = 3 AND package_id IS NOT NULL)
                    OR (kind = 4 AND project_role_id IS NOT NULL)
                );
                """);

            // A response cannot be expected before the submission went.
            migrationBuilder.Sql("""
                ALTER TABLE submissions ADD CONSTRAINT ck_submissions_response_after_sent
                CHECK (response_expected_by IS NULL OR response_expected_by >= sent_at::date);
                """);

            // One open target per party per pursuit. Two would mean two agents
            // working the same buyer without knowing about each other - which is
            // the situation this part of the system exists to prevent.
            //
            // Stages 1..6 are the ones a target can still move from.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_opportunity_targets_one_open_company
                ON opportunity_targets (opportunity_id, company_id)
                WHERE company_id IS NOT NULL AND stage IN (1, 2, 3, 4, 5, 6);
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_opportunity_targets_one_open_person
                ON opportunity_targets (opportunity_id, person_id)
                WHERE person_id IS NOT NULL AND stage IN (1, 2, 3, 4, 5, 6);
                """);

            // The same thing is not a subject of one pursuit twice.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_opportunity_subjects_one_per_target
                ON opportunity_subjects (opportunity_id, kind, COALESCE(
                    talent_profile_id, project_id, package_id, project_role_id));
                """);

            // ---- Search ----
            //
            // Opportunities join the M3 ranked search. 'simple' rather than
            // 'english' for the reason M2 gave: these are names and titles, and
            // stemming them loses the thing being searched for.
            //
            // strategy_notes is deliberately absent. A caller without
            // opportunities.strategy.read must not be able to confirm what a note
            // says by searching for a phrase and watching the pursuit surface -
            // which would defeat the redaction entirely (ADR-0020).
            migrationBuilder.Sql("""
                ALTER TABLE opportunities ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(name, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(description, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_opportunities_search_vector ON opportunities USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_opportunities_name_trgm ON opportunities USING gin (name gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_opportunities_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_opportunities_search_vector;");
            migrationBuilder.Sql("ALTER TABLE opportunities DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_opportunity_subjects_one_per_target;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_opportunity_targets_one_open_person;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_opportunity_targets_one_open_company;");

            migrationBuilder.Sql(
                "ALTER TABLE submissions DROP CONSTRAINT IF EXISTS ck_submissions_response_after_sent;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunity_subjects DROP CONSTRAINT IF EXISTS ck_opportunity_subjects_kind_matches;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunity_subjects DROP CONSTRAINT IF EXISTS ck_opportunity_subjects_one_target;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunities DROP CONSTRAINT IF EXISTS ck_opportunities_outcome_when_closed;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunities DROP CONSTRAINT IF EXISTS ck_opportunities_period;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunities DROP CONSTRAINT IF EXISTS ck_opportunities_closed_consistent;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunity_targets DROP CONSTRAINT IF EXISTS ck_opportunity_targets_closed_stage;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunity_targets DROP CONSTRAINT IF EXISTS ck_opportunity_targets_contact_company;");
            migrationBuilder.Sql(
                "ALTER TABLE opportunity_targets DROP CONSTRAINT IF EXISTS ck_opportunity_targets_one_endpoint;");

            migrationBuilder.DropTable(
                name: "opportunity_events");

            migrationBuilder.DropTable(
                name: "opportunity_subjects");

            migrationBuilder.DropTable(
                name: "opportunity_target_events");

            migrationBuilder.DropTable(
                name: "opportunity_task_links");

            migrationBuilder.DropTable(
                name: "pitch_materials");

            migrationBuilder.DropTable(
                name: "submission_materials");

            migrationBuilder.DropTable(
                name: "opportunity_pitches");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "opportunity_targets");

            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tasks_organization_id_id",
                table: "tasks");
        }
    }
}
