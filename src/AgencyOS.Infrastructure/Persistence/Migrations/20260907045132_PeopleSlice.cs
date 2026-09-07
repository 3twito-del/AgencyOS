using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeopleSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    website = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.id);
                    table.UniqueConstraint("AK_companies_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_companies_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interaction_type = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    detailed_notes = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interactions", x => x.id);
                    table.UniqueConstraint("AK_interactions_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_interactions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    preferred_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    primary_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.id);
                    table.UniqueConstraint("AK_people_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_people_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "professional_relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_type = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    strength = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    ended_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_relationships", x => x.id);
                    table.ForeignKey(
                        name: "FK_professional_relationships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    related_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_interaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_tasks_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interaction_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    interaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_interaction_participants_interactions_interaction_id",
                        column: x => x.interaction_id,
                        principalTable: "interactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_organization_name",
                table: "companies",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_organization_status",
                table: "companies",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_interaction_participants_organization_company",
                table: "interaction_participants",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interaction_participants_organization_person",
                table: "interaction_participants",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ux_interaction_participants_party",
                table: "interaction_participants",
                columns: new[] { "interaction_id", "person_id", "company_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interactions_organization_occurred_at",
                table: "interactions",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_display_name",
                table: "people",
                columns: new[] { "organization_id", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_status",
                table: "people",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_organization_from_company",
                table: "professional_relationships",
                columns: new[] { "organization_id", "from_company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_organization_from_person",
                table: "professional_relationships",
                columns: new[] { "organization_id", "from_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_organization_status",
                table: "professional_relationships",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_organization_to_company",
                table: "professional_relationships",
                columns: new[] { "organization_id", "to_company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_organization_to_person",
                table: "professional_relationships",
                columns: new[] { "organization_id", "to_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_related_company",
                table: "tasks",
                columns: new[] { "organization_id", "related_company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_related_person",
                table: "tasks",
                columns: new[] { "organization_id", "related_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_state_due_at",
                table: "tasks",
                columns: new[] { "organization_id", "state", "due_at" });

            // ----------------------------------------------------------------
            // Exclusive-arc and tenant-containment constraints (ADR-0011).
            //
            // EF cannot express an optional foreign key whose parts are a required
            // tenant column and a nullable party column, so these are installed as
            // explicit SQL. The schema ends up with stronger guarantees than the
            // model can describe.
            //
            // Composite foreign keys use the default MATCH SIMPLE, so a row whose
            // party column is NULL is simply unconstrained on that side - which is
            // precisely what an exclusive arc needs.
            // ----------------------------------------------------------------

            // Exactly one endpoint kind on the From side of a relationship.
            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT ck_relationships_from_exactly_one_endpoint CHECK (num_nonnulls(from_person_id, from_company_id) = 1);");

            // Exactly one endpoint kind on the To side of a relationship.
            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT ck_relationships_to_exactly_one_endpoint CHECK (num_nonnulls(to_person_id, to_company_id) = 1);");

            // A relationship cannot end before it began.
            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT ck_relationships_ended_after_started CHECK (ended_at IS NULL OR started_at IS NULL OR ended_at >= started_at);");

            // No M2 relationship type permits a party to relate to itself. A type that did would require relaxing this deliberately, which is the point.
            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT ck_relationships_no_self_reference CHECK ((from_person_id IS NULL OR to_person_id IS NULL OR from_person_id <> to_person_id) AND (from_company_id IS NULL OR to_company_id IS NULL OR from_company_id <> to_company_id));");

            // Exactly one party kind per interaction participant.
            migrationBuilder.Sql(
                "ALTER TABLE interaction_participants ADD CONSTRAINT ck_interaction_participants_exactly_one_party CHECK (num_nonnulls(person_id, company_id) = 1);");

            // A task concerns at most one party.
            migrationBuilder.Sql(
                "ALTER TABLE tasks ADD CONSTRAINT ck_tasks_at_most_one_subject CHECK (num_nonnulls(related_person_id, related_company_id) <= 1);");

            // State 2 is Completed. A completed task always records when it was completed; an open task never claims a completion instant.
            migrationBuilder.Sql(
                "ALTER TABLE tasks ADD CONSTRAINT ck_tasks_completion_consistent CHECK ((state = 2 AND completed_at IS NOT NULL) OR (state <> 2 AND completed_at IS NULL));");

            // Every reference carries the tenant, so a row in one tenant cannot point
            // at a record in another. Cross-tenant data becomes impossible to write
            // rather than merely unlikely.

            migrationBuilder.Sql(
                "ALTER TABLE people ADD CONSTRAINT fk_people_primary_company_same_tenant FOREIGN KEY (organization_id, primary_company_id) REFERENCES companies (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT fk_relationships_from_person_same_tenant FOREIGN KEY (organization_id, from_person_id) REFERENCES people (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT fk_relationships_from_company_same_tenant FOREIGN KEY (organization_id, from_company_id) REFERENCES companies (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT fk_relationships_to_person_same_tenant FOREIGN KEY (organization_id, to_person_id) REFERENCES people (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE professional_relationships ADD CONSTRAINT fk_relationships_to_company_same_tenant FOREIGN KEY (organization_id, to_company_id) REFERENCES companies (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE interaction_participants ADD CONSTRAINT fk_interaction_participants_interaction_same_tenant FOREIGN KEY (organization_id, interaction_id) REFERENCES interactions (organization_id, id) ON DELETE CASCADE;");

            migrationBuilder.Sql(
                "ALTER TABLE interaction_participants ADD CONSTRAINT fk_interaction_participants_person_same_tenant FOREIGN KEY (organization_id, person_id) REFERENCES people (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE interaction_participants ADD CONSTRAINT fk_interaction_participants_company_same_tenant FOREIGN KEY (organization_id, company_id) REFERENCES companies (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE tasks ADD CONSTRAINT fk_tasks_related_person_same_tenant FOREIGN KEY (organization_id, related_person_id) REFERENCES people (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE tasks ADD CONSTRAINT fk_tasks_related_company_same_tenant FOREIGN KEY (organization_id, related_company_id) REFERENCES companies (organization_id, id);");

            migrationBuilder.Sql(
                "ALTER TABLE tasks ADD CONSTRAINT fk_tasks_source_interaction_same_tenant FOREIGN KEY (organization_id, source_interaction_id) REFERENCES interactions (organization_id, id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE tasks DROP CONSTRAINT IF EXISTS fk_tasks_source_interaction_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE tasks DROP CONSTRAINT IF EXISTS fk_tasks_related_company_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE tasks DROP CONSTRAINT IF EXISTS fk_tasks_related_person_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE interaction_participants DROP CONSTRAINT IF EXISTS fk_interaction_participants_company_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE interaction_participants DROP CONSTRAINT IF EXISTS fk_interaction_participants_person_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE interaction_participants DROP CONSTRAINT IF EXISTS fk_interaction_participants_interaction_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS fk_relationships_to_company_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS fk_relationships_to_person_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS fk_relationships_from_company_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS fk_relationships_from_person_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE people DROP CONSTRAINT IF EXISTS fk_people_primary_company_same_tenant;");
            migrationBuilder.Sql("ALTER TABLE tasks DROP CONSTRAINT IF EXISTS ck_tasks_completion_consistent;");
            migrationBuilder.Sql("ALTER TABLE tasks DROP CONSTRAINT IF EXISTS ck_tasks_at_most_one_subject;");
            migrationBuilder.Sql("ALTER TABLE interaction_participants DROP CONSTRAINT IF EXISTS ck_interaction_participants_exactly_one_party;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS ck_relationships_no_self_reference;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS ck_relationships_ended_after_started;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS ck_relationships_to_exactly_one_endpoint;");
            migrationBuilder.Sql("ALTER TABLE professional_relationships DROP CONSTRAINT IF EXISTS ck_relationships_from_exactly_one_endpoint;");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "interaction_participants");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "professional_relationships");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "interactions");
        }
    }
}
