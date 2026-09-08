using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_tool_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    requested_of = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision = table.Column<int>(type: "integer", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_provider_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    maximum_sensitivity = table.Column<int>(type: "integer", nullable: false),
                    allows_write_proposals = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    task = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_template_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_template_version = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure = table.Column<int>(type: "integer", nullable: false),
                    failure_detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result = table.Column<string>(type: "character varying(60000)", maxLength: 60000, nullable: true),
                    model_invocation_count = table.Column<int>(type: "integer", nullable: false),
                    tool_call_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    research_case_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_runs", x => x.id);
                    table.UniqueConstraint("AK_ai_runs_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "ai_tool_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool_version = table.Column<int>(type: "integer", nullable: false),
                    effect = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    arguments = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    refusal = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_tool_requests", x => x.id);
                    table.UniqueConstraint("AK_ai_tool_requests_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "ai_run_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    step_kind = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_run_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_run_steps_ai_runs_organization_id_ai_run_id",
                        columns: x => new { x.organization_id, x.ai_run_id },
                        principalTable: "ai_runs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_approvals_pending",
                table: "ai_approvals",
                columns: new[] { "organization_id", "requested_of", "decision" },
                filter: "decision = 1");

            migrationBuilder.CreateIndex(
                name: "ux_ai_approvals_request",
                table: "ai_approvals",
                column: "ai_tool_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_provider_policies",
                table: "ai_provider_policies",
                columns: new[] { "organization_id", "provider_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_run_steps_organization_id_ai_run_id",
                table: "ai_run_steps",
                columns: new[] { "organization_id", "ai_run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_run_steps_sequence",
                table: "ai_run_steps",
                columns: new[] { "ai_run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_runs_status",
                table: "ai_runs",
                columns: new[] { "organization_id", "status", "started_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_runs_user",
                table: "ai_runs",
                columns: new[] { "organization_id", "user_id", "started_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_runs_waiting",
                table: "ai_runs",
                columns: new[] { "organization_id", "status" },
                filter: "status = 3");

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_requests_run",
                table: "ai_tool_requests",
                columns: new[] { "organization_id", "ai_run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_tool_requests_fingerprint",
                table: "ai_tool_requests",
                columns: new[] { "ai_run_id", "fingerprint" },
                unique: true);
            // ---- Tenant-safe arcs, coherence and immutability (M12) ----
            // 
            // None of this is expressible in the EF model: composite keys through alternate
            // keys, an exclusive-arc check, cross-column coherence, and a trigger that
            // refuses to rewrite what a run already did.
            // A run is about a real record in the same tenant. Five typed columns rather
            // than an untyped pair, on the M10 and M11 precedent (ADR-0011, ADR-0025).
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT fk_ai_runs_research_case_id
                    FOREIGN KEY (organization_id, research_case_id)
                    REFERENCES research_cases (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT fk_ai_runs_person_id
                    FOREIGN KEY (organization_id, person_id)
                    REFERENCES people (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT fk_ai_runs_company_id
                    FOREIGN KEY (organization_id, company_id)
                    REFERENCES companies (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT fk_ai_runs_deal_id
                    FOREIGN KEY (organization_id, deal_id)
                    REFERENCES deals (organization_id, id)
                    ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT fk_ai_runs_contract_id
                    FOREIGN KEY (organization_id, contract_id)
                    REFERENCES contracts (organization_id, id)
                    ON DELETE CASCADE;
                """);
            // Either a run names a subject and says what kind it is, with the typed column
            // and subject_id agreeing, or it names neither. Half an arc points at nothing,
            // and a subject_id with no typed column behind it has no foreign key.
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_subject_arc CHECK (
                    CASE subject_kind
                        WHEN 0 THEN (subject_id IS NULL AND ((CASE WHEN research_case_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN person_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN company_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN deal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN contract_id IS NULL THEN 0 ELSE 1 END)) = 0)
                        ELSE (
                            subject_id IS NOT NULL
                            AND ((CASE WHEN research_case_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN person_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN company_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN deal_id IS NULL THEN 0 ELSE 1 END)
                         + (CASE WHEN contract_id IS NULL THEN 0 ELSE 1 END)) = 1
                            AND CASE subject_kind
                                        WHEN 1 THEN (research_case_id IS NOT NULL AND research_case_id = subject_id)
                                        WHEN 2 THEN (person_id IS NOT NULL AND person_id = subject_id)
                                        WHEN 3 THEN (company_id IS NOT NULL AND company_id = subject_id)
                                        WHEN 4 THEN (deal_id IS NOT NULL AND deal_id = subject_id)
                                        WHEN 5 THEN (contract_id IS NOT NULL AND contract_id = subject_id)
                                ELSE false
                            END
                        )
                    END
                );
                """);
            // A run that has finished says when. Without this a cancelled run and a running
            // one are indistinguishable to anything reading completed_at.
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_terminal CHECK (
                    (status IN (1, 2, 3) AND completed_at IS NULL)
                    OR (status IN (4, 5, 6, 7) AND completed_at IS NOT NULL)
                );
                """);
            // A failed run says what kind of failure, and a run that did not fail claims
            // none. "It failed" with no category is the state §68 exists to prevent.
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_failure CHECK (
                    (status = 5 AND failure <> 0) OR (status <> 5 AND failure = 0)
                );
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_counts CHECK (
                    model_invocation_count >= 0 AND tool_call_count >= 0
                );
                """);
            // A decision names who made it and when, together or not at all. Expiry is the
            // one case with a moment and no person, because nobody made it — which is
            // exactly what expiry means and why it is a separate value (§13).
            migrationBuilder.Sql("""
                ALTER TABLE ai_approvals
                ADD CONSTRAINT ck_ai_approvals_decision CHECK (
                    (decision = 1 AND decided_by IS NULL AND decided_at IS NULL)
                    OR (decision IN (2, 3) AND decided_by IS NOT NULL AND decided_at IS NOT NULL)
                    OR (decision = 4 AND decided_by IS NULL AND decided_at IS NOT NULL)
                );
                """);
            // An approval that is already expired when it is written asks nothing.
            migrationBuilder.Sql("""
                ALTER TABLE ai_approvals
                ADD CONSTRAINT ck_ai_approvals_window CHECK (expires_at > requested_at);
                """);
            // ReadOnly or CanonicalWrite. ExternalEffect exists in the enumeration and is
            // refused here as well as in the aggregate: no external-effect tool is reachable
            // by a model in this build, and the database says so rather than trusting that
            // no code path ever will (§12, §15).
            migrationBuilder.Sql("""
                ALTER TABLE ai_tool_requests
                ADD CONSTRAINT ck_ai_tool_requests_effect CHECK (effect IN (1, 2));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE ai_tool_requests
                ADD CONSTRAINT ck_ai_tool_requests_version CHECK (tool_version >= 1);
                """);
            // Internal, Confidential or Protected. Restricted (4) is deliberately absent:
            // material an organization marked as never leaving has no reachable ceiling, so
            // no administrator, role or configuration flag can permit transmitting it. The
            // aggregate refuses it too; this is the layer no future code path goes around
            // (§5, §43).
            migrationBuilder.Sql("""
                ALTER TABLE ai_provider_policies
                ADD CONSTRAINT ck_ai_provider_policies_ceiling CHECK (maximum_sensitivity IN (1, 2, 3));
                """);
            // The history a person reads to tell what the model said from what AgencyOS did.
            // A later turn, or a later bug, must not be able to revise it (§17, §64).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_ai_run_steps_immutable()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF EXISTS (SELECT 1 FROM ai_runs WHERE id = OLD.ai_run_id) THEN
                            RAISE EXCEPTION
                                'A run step records what happened. Steps are appended, never '
                                'removed.';
                        END IF;

                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION
                        'A run step cannot be edited. What the model asked for and what AgencyOS '
                        'did are the record; rewriting one would make the history worthless as an '
                        'explanation.';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_ai_run_steps_immutable
                BEFORE UPDATE OR DELETE ON ai_run_steps
                FOR EACH ROW EXECUTE FUNCTION agencyos_ai_run_steps_immutable();
                """);
            // The one that matters most. A decided approval cannot be re-decided, and the
            // action it authorizes cannot be swapped underneath it — which is the attack the
            // fingerprint exists to stop, enforced where no application path can miss it
            // (§13, §14, §47).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_ai_approvals_monotonic()
                RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        RAISE EXCEPTION
                            'An approval decision is a record of what a person allowed. It is not '
                            'deleted.';
                    END IF;

                    IF (OLD.decision <> 1 AND NEW.decision <> OLD.decision) THEN
                        RAISE EXCEPTION
                            'That approval was already decided. A decision is made once.';
                    END IF;

                    IF (NEW.fingerprint IS DISTINCT FROM OLD.fingerprint
                        OR NEW.ai_tool_request_id IS DISTINCT FROM OLD.ai_tool_request_id
                        OR NEW.organization_id IS DISTINCT FROM OLD.organization_id) THEN
                        RAISE EXCEPTION
                            'An approval binds to one exact action. What it authorizes cannot be '
                            'changed after the fact.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_ai_approvals_monotonic
                BEFORE UPDATE OR DELETE ON ai_approvals
                FOR EACH ROW EXECUTE FUNCTION agencyos_ai_approvals_monotonic();
                """);
            // At most one canonical effect per approved request, and the arguments frozen
            // from the moment they are proposed. The runtime and the handler both enforce
            // this; the trigger is what makes it true of the schema (§48).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_ai_tool_requests_once()
                RETURNS trigger AS $$
                BEGIN
                    IF (OLD.status = 5 AND NEW.status <> 5) THEN
                        RAISE EXCEPTION
                            'That tool request already ran. A canonical effect happens once, and '
                            'moving the row back would allow a second.';
                    END IF;

                    IF (NEW.fingerprint IS DISTINCT FROM OLD.fingerprint
                        OR NEW.arguments IS DISTINCT FROM OLD.arguments
                        OR NEW.tool_name IS DISTINCT FROM OLD.tool_name
                        OR NEW.tool_version IS DISTINCT FROM OLD.tool_version) THEN
                        RAISE EXCEPTION
                            'What was proposed cannot change. Approving one action and executing '
                            'another is exactly what the fingerprint prevents.';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_ai_tool_requests_once
                BEFORE UPDATE ON ai_tool_requests
                FOR EACH ROW EXECUTE FUNCTION agencyos_ai_tool_requests_once();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers, their functions, then the foreign keys declared in SQL. EF orders
            // its own DropTable calls around relationships it knows about, and it knows
            // about none of these — M11 proved that the hard way.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_ai_tool_requests_once ON ai_tool_requests;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_ai_approvals_monotonic ON ai_approvals;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_ai_run_steps_immutable ON ai_run_steps;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_ai_tool_requests_once();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_ai_approvals_monotonic();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS agencyos_ai_run_steps_immutable();");

            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_runs_research_case_id;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_runs_person_id;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_runs_company_id;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_runs_deal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS fk_ai_runs_contract_id;");

            migrationBuilder.DropTable(
                name: "ai_approvals");

            migrationBuilder.DropTable(
                name: "ai_provider_policies");

            migrationBuilder.DropTable(
                name: "ai_run_steps");

            migrationBuilder.DropTable(
                name: "ai_tool_requests");

            migrationBuilder.DropTable(
                name: "ai_runs");
        }
    }
}
