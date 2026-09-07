using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectsAndPackaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    working_title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    logline = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    synopsis = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    primary_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    lead_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                    table.UniqueConstraint("AK_projects_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_projects_companies_organization_id_primary_company_id",
                        columns: x => new { x.organization_id, x.primary_company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_users_lead_user_id",
                        column: x => x.lead_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    attributed_creator = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    creator_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provenance = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_properties", x => x.id);
                    table.UniqueConstraint("AK_source_properties_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_source_properties_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_properties_people_organization_id_creator_person_id",
                        columns: x => new { x.organization_id, x.creator_person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    thesis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    strategy_notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    lead_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.id);
                    table.UniqueConstraint("AK_packages_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_packages_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_packages_users_lead_user_id",
                        column: x => x.lead_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_company_participations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_company_participations", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_company_participations_companies_organization_id_co~",
                        columns: x => new { x.organization_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_company_participations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: true),
                    from_stage = table.Column<int>(type: "integer", nullable: true),
                    to_stage = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_events_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_material_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_material_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_material_links_materials_organization_id_material_id",
                        columns: x => new { x.organization_id, x.material_id },
                        principalTable: "materials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_material_links_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_exclusive = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_roles", x => x.id);
                    table.UniqueConstraint("AK_project_roles_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_project_roles_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_source_properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_source_properties", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_source_properties_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_source_properties_source_properties_organization_id~",
                        columns: x => new { x.organization_id, x.source_property_id },
                        principalTable: "source_properties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_elements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_elements", x => x.id);
                    table.ForeignKey(
                        name: "FK_package_elements_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_package_events_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    role_is_exclusive = table.Column<bool>(type: "boolean", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.id);
                    table.UniqueConstraint("AK_attachments_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_attachments_companies_organization_id_company_id",
                        columns: x => new { x.organization_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attachments_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attachments_project_roles_organization_id_project_role_id",
                        columns: x => new { x.organization_id, x.project_role_id },
                        principalTable: "project_roles",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attachments_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attachment_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachment_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_attachment_events_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credits_organization_project",
                table: "credits",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_events_attachment_recorded",
                table: "attachment_events",
                columns: new[] { "attachment_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_organization_id_company_id",
                table: "attachments",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_organization_id_project_role_id",
                table: "attachments",
                columns: new[] { "organization_id", "project_role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_organization_person",
                table: "attachments",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_project_status",
                table: "attachments",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_package_elements_target",
                table: "package_elements",
                columns: new[] { "package_id", "kind", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_package_events_package_recorded",
                table: "package_events",
                columns: new[] { "package_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_lead_user_id",
                table: "packages",
                column: "lead_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_packages_organization_id_project_id",
                table: "packages",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_packages_organization_status",
                table: "packages",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_companies_organization_company",
                table: "project_company_participations",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_project_company_participations_project_id",
                table: "project_company_participations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_events_project_recorded",
                table: "project_events",
                columns: new[] { "project_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_project_material_links_organization_id_material_id",
                table: "project_material_links",
                columns: new[] { "organization_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ux_project_material_links_pair",
                table: "project_material_links",
                columns: new[] { "project_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_roles_project_status",
                table: "project_roles",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_project_source_properties_organization_id_source_property_id",
                table: "project_source_properties",
                columns: new[] { "organization_id", "source_property_id" });

            migrationBuilder.CreateIndex(
                name: "ux_project_source_properties_pair",
                table: "project_source_properties",
                columns: new[] { "project_id", "source_property_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_lead_user_id",
                table: "projects",
                column: "lead_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_projects_organization_id_primary_company_id",
                table: "projects",
                columns: new[] { "organization_id", "primary_company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_organization_status",
                table: "projects",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_organization_updated",
                table: "projects",
                columns: new[] { "organization_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_source_properties_organization_id_creator_person_id",
                table: "source_properties",
                columns: new[] { "organization_id", "creator_person_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_credits_projects_organization_id_project_id",
                table: "credits",
                columns: new[] { "organization_id", "project_id" },
                principalTable: "projects",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // ---- Structural invariants ----
            //
            // Everything below is expressible only in SQL. Where a rule can be made
            // impossible in the database it is, rather than being left to the
            // application to remember (CLAUDE.md section 5).

            // An attachment names a person or a company, never both and never
            // neither. AttachmentParty expresses this in the domain; this closes the
            // door for anything reaching the table by another route.
            migrationBuilder.Sql("""
                ALTER TABLE attachments ADD CONSTRAINT ck_attachments_one_party
                CHECK (
                    (person_id IS NOT NULL AND company_id IS NULL)
                    OR (person_id IS NULL AND company_id IS NOT NULL)
                );
                """);

            // Periods run forwards. A record that ended before it began is not a
            // slightly wrong record, it is a meaningless one.
            migrationBuilder.Sql("""
                ALTER TABLE attachments ADD CONSTRAINT ck_attachments_period
                CHECK (ends_on IS NULL OR ends_on >= starts_on);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE project_company_participations
                ADD CONSTRAINT ck_project_company_participations_period
                CHECK (ends_on IS NULL OR ends_on >= starts_on);
                """);

            // One party at a time in an exclusive role. Two directors both believing
            // they are attached is the failure two concurrent requests would produce,
            // and it must be impossible rather than unlikely.
            //
            // Statuses 2 and 3 are Attached and Conditional: the ones that occupy a
            // role. InDiscussion deliberately does not, because several people can
            // genuinely be in talks for one job.
            //
            // The predicate reads role_is_exclusive from the attachment row rather
            // than joining to project_roles, because PostgreSQL forbids a subquery
            // in an index predicate. The project aggregate keeps that copy in step
            // and refuses to make a role exclusive while several parties hold it.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_attachments_one_holder_per_exclusive_role
                ON attachments (project_role_id)
                WHERE status IN (2, 3) AND role_is_exclusive;
                """);

            // One open involvement per company per capacity. A studio recorded twice
            // over the same period is a duplicate, not a richer record.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_project_companies_one_open_per_capacity
                ON project_company_participations (project_id, company_id, capacity)
                WHERE ends_on IS NULL;
                """);

            // ---- Search ----
            //
            // Projects, source properties and packages join the M3 ranked search on
            // the same generated-column and GIN pattern. 'simple' rather than
            // 'english' for the reason M2 gave: these are titles and proper names,
            // and stemming them loses the thing being searched for.

            migrationBuilder.Sql("""
                ALTER TABLE projects ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(working_title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(logline, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(synopsis, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE source_properties ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(attributed_creator, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(notes, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE packages ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(name, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(thesis, '')), 'B')
                ) STORED;
                """);

            // Strategy notes are deliberately absent from the package search vector.
            // A caller without packages.strategy.read must not be able to confirm
            // what a note says by searching for a phrase and seeing the package
            // surface - which would defeat the redaction entirely (ADR-0019).

            migrationBuilder.Sql(
                "CREATE INDEX ix_projects_search_vector ON projects USING gin (search_vector);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_source_properties_search_vector ON source_properties USING gin (search_vector);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_packages_search_vector ON packages USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_projects_title_trgm ON projects USING gin (title gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_source_properties_title_trgm ON source_properties USING gin (title gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_packages_name_trgm ON packages USING gin (name gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_packages_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_source_properties_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_projects_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_packages_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_source_properties_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_projects_search_vector;");
            migrationBuilder.Sql("ALTER TABLE packages DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.Sql("ALTER TABLE source_properties DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.Sql("ALTER TABLE projects DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_project_companies_one_open_per_capacity;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_attachments_one_holder_per_exclusive_role;");

            migrationBuilder.Sql(
                "ALTER TABLE project_company_participations "
                    + "DROP CONSTRAINT IF EXISTS ck_project_company_participations_period;");
            migrationBuilder.Sql(
                "ALTER TABLE attachments DROP CONSTRAINT IF EXISTS ck_attachments_period;");
            migrationBuilder.Sql(
                "ALTER TABLE attachments DROP CONSTRAINT IF EXISTS ck_attachments_one_party;");

            migrationBuilder.DropForeignKey(
                name: "FK_credits_projects_organization_id_project_id",
                table: "credits");

            migrationBuilder.DropTable(
                name: "attachment_events");

            migrationBuilder.DropTable(
                name: "package_elements");

            migrationBuilder.DropTable(
                name: "package_events");

            migrationBuilder.DropTable(
                name: "project_company_participations");

            migrationBuilder.DropTable(
                name: "project_events");

            migrationBuilder.DropTable(
                name: "project_material_links");

            migrationBuilder.DropTable(
                name: "project_source_properties");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropTable(
                name: "source_properties");

            migrationBuilder.DropTable(
                name: "project_roles");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "ix_credits_organization_project",
                table: "credits");
        }
    }
}
