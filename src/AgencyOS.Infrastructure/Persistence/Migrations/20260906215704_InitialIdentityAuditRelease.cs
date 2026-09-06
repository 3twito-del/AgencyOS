using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAuditRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    semantic_delta = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    client_platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    client_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_build_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    api_contract_version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "release_policies",
                columns: table => new
                {
                    platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ring = table.Column<int>(type: "integer", nullable: false),
                    latest_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    minimum_supported_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    behind_policy = table.Column<int>(type: "integer", nullable: false),
                    api_contract_minimum = table.Column<int>(type: "integer", nullable: false),
                    api_contract_maximum = table.Column<int>(type: "integer", nullable: false),
                    security_epoch = table.Column<int>(type: "integer", nullable: false),
                    kill_switch = table.Column<bool>(type: "boolean", nullable: false),
                    mandatory_after_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rollback_target = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_versions = table.Column<string[]>(type: "text[]", nullable: false),
                    artifact_sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    artifact_signature = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_policies", x => new { x.platform, x.ring });
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_memberships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_correlation_id",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                table: "audit_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_organization_user_status",
                table: "memberships",
                columns: new[] { "organization_id", "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_status",
                table: "memberships",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_organizations_name",
                table: "organizations",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_subject",
                table: "users",
                column: "external_subject",
                unique: true);

            // ----------------------------------------------------------------
            // Append-only enforcement for the audit trail.
            //
            // The domain type exposes no mutator, and a save interceptor rejects
            // modified or deleted audit entries, but both of those live inside
            // the application. This trigger also binds anything that reaches the
            // database directly: a maintenance script, a later migration, an
            // operator with a psql session.
            //
            // docs/07_SECURITY_AND_AUDIT.md requires audit records to be
            // immutable. Immutability that holds only while code behaves is not
            // immutability.
            // ----------------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION agencyos_audit_events_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'audit_events is append-only; % is not permitted', TG_OP
                        USING ERRCODE = '0A000';
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_events_append_only
                BEFORE UPDATE OR DELETE ON audit_events
                FOR EACH ROW
                EXECUTE FUNCTION agencyos_audit_events_append_only();
                """);

            // TRUNCATE bypasses row-level triggers, so it is refused separately.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_events_no_truncate
                BEFORE TRUNCATE ON audit_events
                FOR EACH STATEMENT
                EXECUTE FUNCTION agencyos_audit_events_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_no_truncate ON audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_append_only ON audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_audit_events_append_only();");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "release_policies");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
