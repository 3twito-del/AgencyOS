using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalInference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "execution_device",
                table: "ai_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "residency",
                table: "ai_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ai_context_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    residency = table.Column<int>(type: "integer", nullable: false),
                    context_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_context_leases", x => x.id);
                    table.UniqueConstraint("AK_ai_context_leases_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_context_leases_expiry",
                table: "ai_context_leases",
                columns: new[] { "state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_context_leases_run",
                table: "ai_context_leases",
                columns: new[] { "organization_id", "ai_run_id", "state" });
            // ---- Tenant integrity, coherence and one-time use (M13) ----
            // 
            // None of this is expressible in the EF model: a composite key through an
            // alternate key, cross-column coherence, and a trigger that refuses to
            // un-consume a lease.
            // A lease belongs to a run in the same tenant. The composite key is what makes
            // that a database fact rather than something the handler remembers to check
            // (ADR-0011).
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT fk_ai_context_leases_run
                    FOREIGN KEY (organization_id, ai_run_id)
                    REFERENCES ai_runs (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // The user whose authority the context was assembled under. RESTRICT rather
            // than CASCADE: deleting a person should not silently erase the record of what
            // was disclosed on their behalf.
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT fk_ai_context_leases_user
                    FOREIGN KEY (user_id)
                    REFERENCES users (id)
                    ON DELETE RESTRICT;
                """);
            // OrganizationControlled or DeviceLocal. ExternalCloud (1) is deliberately
            // absent: cloud inference runs on the server own connection and needs no lease,
            // so one issued for it would be a second and weaker path to the same context.
            // The aggregate refuses it too; this is the layer no future code path goes
            // around (ADR-0035).
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT ck_ai_context_leases_residency CHECK (residency IN (2, 3));
                """);
            // A lease that is already expired when it is written authorizes nothing.
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT ck_ai_context_leases_window CHECK (expires_at > issued_at);
                """);
            // A lease that has been used or withdrawn says when. Without this a consumed
            // lease and an outstanding one are indistinguishable to anything reading
            // resolved_at.
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT ck_ai_context_leases_state CHECK (
                    (state = 1 AND resolved_at IS NULL)
                    OR (state IN (2, 3) AND resolved_at IS NOT NULL)
                );
                """);
            // Either a lease names a subject and says what kind it is, or it names neither.
            // Half an arc points at nothing, and the subject is part of what the
            // fingerprint refuses to let the client substitute.
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT ck_ai_context_leases_subject CHECK (
                    (subject_kind = 0 AND subject_id IS NULL)
                    OR (subject_kind <> 0 AND subject_id IS NOT NULL)
                );
                """);
            // A SHA-256 hex digest, exactly. A short value here would be a truncated
            // fingerprint, which compares equal to more things than it should.
            migrationBuilder.Sql("""
                ALTER TABLE ai_context_leases
                ADD CONSTRAINT ck_ai_context_leases_fingerprint CHECK (length(context_fingerprint) = 64);
                """);
            // ExternalCloud, OrganizationControlled or DeviceLocal. Unlike the lease, a run
            // may be any of the three, because a run records where inference happened.
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_residency CHECK (residency IN (1, 2, 3));
                """);
            // M12 wrote this constraint over seven statuses. M13 adds an eighth -
            // AwaitingLocalExecution - which is non-terminal for the same reason
            // AwaitingApproval is: the run is waiting for something outside this process.
            // Dropped and rewritten rather than amended, because a CHECK cannot be altered.
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs DROP CONSTRAINT ck_ai_runs_terminal;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_terminal CHECK (
                    (status IN (1, 2, 3, 8) AND completed_at IS NULL)
                    OR (status IN (4, 5, 6, 7) AND completed_at IS NOT NULL)
                );
                """);
            // The one that matters. A resolved lease cannot be re-opened, and none of what
            // it binds to can be swapped underneath it - which is the substitution the
            // fingerprint exists to stop, enforced where no application path can miss it
            // (ADR-0035).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_ai_context_leases_once()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        RAISE EXCEPTION
                            'A lease records that context was disclosed to a device. It is not '
                            'deleted.';
                    END IF;

                    IF (OLD.state <> 1 AND NEW.state <> OLD.state) THEN
                        RAISE EXCEPTION
                            'That lease was already resolved. A lease authorizes one execution, '
                            'and moving it back would allow a second.';
                    END IF;

                    IF (NEW.context_fingerprint IS DISTINCT FROM OLD.context_fingerprint
                        OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                        OR NEW.user_id IS DISTINCT FROM OLD.user_id
                        OR NEW.ai_run_id IS DISTINCT FROM OLD.ai_run_id
                        OR NEW.subject_kind IS DISTINCT FROM OLD.subject_kind
                        OR NEW.subject_id IS DISTINCT FROM OLD.subject_id
                        OR NEW.residency IS DISTINCT FROM OLD.residency) THEN
                        RAISE EXCEPTION
                            'A lease binds to one tenant, one user, one run, one subject, one '
                            'residency and one exact context. What it authorizes cannot be '
                            'changed after the fact.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_ai_context_leases_once
                BEFORE UPDATE OR DELETE ON ai_context_leases
                FOR EACH ROW EXECUTE FUNCTION agencyos_ai_context_leases_once();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trigger, function, then the constraints and foreign keys declared in SQL.
            // EF orders its own DropTable calls around relationships it knows about, and
            // it knows about none of these - M11 proved that the hard way.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_ai_context_leases_once ON ai_context_leases;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_ai_context_leases_once();");

            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_context_leases_run;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_context_leases_user;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_context_leases_residency;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_context_leases_window;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_context_leases_state;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_context_leases_subject;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_context_leases "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_context_leases_fingerprint;");

            // The run constraints go back to what M12 wrote, so rolling back to M12
            // leaves a schema identical to the one M12 produced rather than merely a
            // working one.
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs DROP CONSTRAINT IF EXISTS ck_ai_runs_residency;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs DROP CONSTRAINT IF EXISTS ck_ai_runs_terminal;");
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_terminal CHECK (
                    (status IN (1, 2, 3) AND completed_at IS NULL)
                    OR (status IN (4, 5, 6, 7) AND completed_at IS NOT NULL)
                );
                """);

            migrationBuilder.DropTable(
                name: "ai_context_leases");

            migrationBuilder.DropColumn(
                name: "execution_device",
                table: "ai_runs");

            migrationBuilder.DropColumn(
                name: "residency",
                table: "ai_runs");
        }
    }
}
