using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TalentAndRepresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    role = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credits", x => x.id);
                    table.UniqueConstraint("AK_credits_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_credits_companies_organization_id_company_id",
                        columns: x => new { x.organization_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credits_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    version_label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    external_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.UniqueConstraint("AK_materials_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_materials_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prospects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    strategy_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    identified_on = table.Column<DateOnly>(type: "date", nullable: false),
                    next_follow_up_on = table.Column<DateOnly>(type: "date", nullable: true),
                    converted_to_representation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prospects", x => x.id);
                    table.UniqueConstraint("AK_prospects_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_prospects_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prospects_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "representations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_exclusive = table.Column<bool>(type: "boolean", nullable: true),
                    territory = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representations", x => x.id);
                    table.UniqueConstraint("AK_representations_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_representations_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.ForeignKey(
                        name: "FK_representations_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "talent_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    career_stage = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    positioning_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    base_market = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    languages = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_talent_profiles", x => x.id);
                    table.UniqueConstraint("AK_talent_profiles_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_talent_profiles_people_organization_id_person_id",
                        columns: x => new { x.organization_id, x.person_id },
                        principalTable: "people",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prospect_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prospect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage = table.Column<int>(type: "integer", nullable: true),
                    to_stage = table.Column<int>(type: "integer", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prospect_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_prospect_events_prospects_prospect_id",
                        column: x => x.prospect_id,
                        principalTable: "prospects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "representation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_representation_events_representations_representation_id",
                        column: x => x.representation_id,
                        principalTable: "representations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "representation_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representation_scopes", x => x.id);
                    table.CheckConstraint("ck_representation_scopes_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.ForeignKey(
                        name: "FK_representation_scopes_representations_representation_id",
                        column: x => x.representation_id,
                        principalTable: "representations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "representation_team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representation_team_members", x => x.id);
                    table.CheckConstraint("ck_representation_team_members_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.ForeignKey(
                        name: "FK_representation_team_members_representations_representation_~",
                        column: x => x.representation_id,
                        principalTable: "representations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_representation_team_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "talent_disciplines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    talent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discipline = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_talent_disciplines", x => x.id);
                    table.CheckConstraint("ck_talent_disciplines_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.ForeignKey(
                        name: "FK_talent_disciplines_talent_profiles_talent_profile_id",
                        column: x => x.talent_profile_id,
                        principalTable: "talent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credits_organization_id_company_id",
                table: "credits",
                columns: new[] { "organization_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credits_organization_person",
                table: "credits",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_materials_organization_person",
                table: "materials",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_prospect_events_prospect_occurred",
                table: "prospect_events",
                columns: new[] { "prospect_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "IX_prospects_organization_id_person_id",
                table: "prospects",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_prospects_organization_stage",
                table: "prospects",
                columns: new[] { "organization_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_prospects_owner_follow_up",
                table: "prospects",
                columns: new[] { "organization_id", "owner_user_id", "next_follow_up_on" });

            migrationBuilder.CreateIndex(
                name: "IX_prospects_owner_user_id",
                table: "prospects",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_representation_events_representation_occurred",
                table: "representation_events",
                columns: new[] { "representation_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_representation_scopes_representation",
                table: "representation_scopes",
                column: "representation_id");

            migrationBuilder.CreateIndex(
                name: "IX_representation_team_members_user_id",
                table: "representation_team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_representation_team_organization_user",
                table: "representation_team_members",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_representation_team_representation",
                table: "representation_team_members",
                column: "representation_id");

            migrationBuilder.CreateIndex(
                name: "IX_representations_organization_id_person_id",
                table: "representations",
                columns: new[] { "organization_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_representations_organization_status",
                table: "representations",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_talent_disciplines_profile",
                table: "talent_disciplines",
                column: "talent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_talent_profiles_organization_person",
                table: "talent_profiles",
                columns: new[] { "organization_id", "person_id" },
                unique: true);

            // ---- Temporal correctness, expressed as constraints ----
            //
            // Partial unique indexes rather than application checks alone. Each of
            // these is an invariant the domain also enforces; stating them here as
            // well means a bug, a bulk fix or a future code path cannot produce data
            // the domain says is impossible. They are the reason a retried
            // conversion cannot leave the agency believing it represents somebody
            // twice.

            // At most one live pursuit of a person per tenant. Two would mean two
            // agents courting the same person without knowing about each other.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_prospects_one_open_per_person
                ON prospects (organization_id, person_id)
                WHERE stage IN (1, 2, 3);
                """);

            // At most one representation per person that can still change. This is
            // the last line of defence against a duplicate active representation.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_representations_one_live_per_person
                ON representations (organization_id, person_id)
                WHERE status IN (1, 2, 3);
                """);

            // One open scope per area: representing somebody for Television twice
            // over the same period is not a richer record, it is a duplicate.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_representation_scopes_one_open_per_area
                ON representation_scopes (representation_id, area)
                WHERE ends_on IS NULL;
                """);

            // Exactly one lead at a time. "Who owns this relationship" must have one
            // answer, and the lead is derived from this table rather than stored on
            // the representation, so this index is what makes that derivation total.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_representation_team_one_open_lead
                ON representation_team_members (representation_id)
                WHERE role = 1 AND ends_on IS NULL;
                """);

            // One open assignment per person per representation. Somebody cannot
            // simultaneously be the coordinator and the assistant.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_representation_team_one_open_per_user
                ON representation_team_members (representation_id, user_id)
                WHERE ends_on IS NULL;
                """);

            // One open claim per discipline, for the same reason as scopes.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_talent_disciplines_one_open_per_discipline
                ON talent_disciplines (talent_profile_id, discipline)
                WHERE ends_on IS NULL;
                """);

            // ---- Search ----
            //
            // Credits and materials carry titles worth finding, so they join the M3
            // ranked search on the same generated-column and GIN pattern. 'simple'
            // rather than 'english' for the same reason as M2: these are proper
            // names and titles, and stemming them loses the thing being searched for.

            migrationBuilder.Sql("""
                ALTER TABLE credits ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(role, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(notes, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE materials ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(version_label, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(notes, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_credits_search_vector ON credits USING gin (search_vector);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_materials_search_vector ON materials USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_credits_title_trgm ON credits USING gin (title gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_materials_title_trgm ON materials USING gin (title gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_materials_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_credits_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_materials_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_credits_search_vector;");
            migrationBuilder.Sql("ALTER TABLE materials DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.Sql("ALTER TABLE credits DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_talent_disciplines_one_open_per_discipline;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_representation_team_one_open_per_user;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_representation_team_one_open_lead;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_representation_scopes_one_open_per_area;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_representations_one_live_per_person;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_prospects_one_open_per_person;");


            migrationBuilder.DropTable(
                name: "credits");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "prospect_events");

            migrationBuilder.DropTable(
                name: "representation_events");

            migrationBuilder.DropTable(
                name: "representation_scopes");

            migrationBuilder.DropTable(
                name: "representation_team_members");

            migrationBuilder.DropTable(
                name: "talent_disciplines");

            migrationBuilder.DropTable(
                name: "prospects");

            migrationBuilder.DropTable(
                name: "representations");

            migrationBuilder.DropTable(
                name: "talent_profiles");
        }
    }
}
