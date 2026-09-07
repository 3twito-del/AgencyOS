using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealsAndOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_opportunity_targets_organization_id_opportunity_id",
                table: "opportunity_targets");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_opportunity_targets_organization_id_opportunity_id_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "opportunity_id", "id" });

            migrationBuilder.CreateTable(
                name: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    strategy_notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deals", x => x.id);
                    table.UniqueConstraint("AK_deals_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_deals_opportunities_organization_id_opportunity_id",
                        columns: x => new { x.organization_id, x.opportunity_id },
                        principalTable: "opportunities",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_opportunity_targets_organization_id_opportunity_id_op~",
                        columns: x => new { x.organization_id, x.opportunity_id, x.opportunity_target_id },
                        principalTable: "opportunity_targets",
                        principalColumns: new[] { "organization_id", "opportunity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deal_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_deal_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_deal_events_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    responds_to_offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    communicated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offers", x => x.id);
                    table.UniqueConstraint("AK_offers_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "FK_offers_deals_organization_id_deal_id",
                        columns: x => new { x.organization_id, x.deal_id },
                        principalTable: "deals",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_offers_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deal_task_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deal_task_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_deal_task_links_deals_organization_id_deal_id",
                        columns: x => new { x.organization_id, x.deal_id },
                        principalTable: "deals",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deal_task_links_offers_organization_id_offer_id",
                        columns: x => new { x.organization_id, x.offer_id },
                        principalTable: "offers",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deal_task_links_tasks_organization_id_task_item_id",
                        columns: x => new { x.organization_id, x.task_item_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offer_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    transition = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_offer_events_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offer_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_terms", x => x.id);
                    table.ForeignKey(
                        name: "FK_offer_terms_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deal_events_deal_recorded",
                table: "deal_events",
                columns: new[] { "deal_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deal_task_links_deal",
                table: "deal_task_links",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "IX_deal_task_links_organization_id_deal_id",
                table: "deal_task_links",
                columns: new[] { "organization_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_deal_task_links_organization_id_offer_id",
                table: "deal_task_links",
                columns: new[] { "organization_id", "offer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_deal_task_links_organization_id_task_item_id",
                table: "deal_task_links",
                columns: new[] { "organization_id", "task_item_id" });

            migrationBuilder.CreateIndex(
                name: "ux_deal_task_links_task",
                table: "deal_task_links",
                column: "task_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deals_opportunity_target",
                table: "deals",
                column: "opportunity_target_id");

            migrationBuilder.CreateIndex(
                name: "IX_deals_organization_id_opportunity_id_opportunity_target_id",
                table: "deals",
                columns: new[] { "organization_id", "opportunity_id", "opportunity_target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_organization_owner",
                table: "deals",
                columns: new[] { "organization_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_organization_status",
                table: "deals",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_organization_updated",
                table: "deals",
                columns: new[] { "organization_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_deals_owner_user_id",
                table: "deals",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_offer_events_offer_recorded",
                table: "offer_events",
                columns: new[] { "offer_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_offer_terms_offer_code",
                table: "offer_terms",
                columns: new[] { "offer_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offers_organization_expires",
                table: "offers",
                columns: new[] { "organization_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_offers_organization_id_deal_id",
                table: "offers",
                columns: new[] { "organization_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_organization_status",
                table: "offers",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_offers_recorded_by_user_id",
                table: "offers",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_offers_deal_sequence",
                table: "offers",
                columns: new[] { "deal_id", "sequence" },
                unique: true);

            // ---- Structural invariants -------------------------------------
            //
            // The rules live in the domain and in the F# kernel; these are what
            // holds when two transactions race, or when a future code path
            // forgets. Nothing here encodes business logic that is not also
            // stated, readably, above it (ADR-0021).

            // A negotiation that has not ended carries no end date, and one that
            // has ended carries one. Statuses 1-3 are Draft, Negotiating and
            // TermsAgreed; 4 and 5 are NoDeal and Cancelled.
            migrationBuilder.Sql("""
                ALTER TABLE deals ADD CONSTRAINT ck_deals_closed_consistent
                CHECK ((closed_on IS NULL) = (status IN (1, 2, 3)));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE deals ADD CONSTRAINT ck_deals_period
                CHECK (closed_on IS NULL OR closed_on >= opened_on);
                """);

            // An offer never answers itself.
            migrationBuilder.Sql("""
                ALTER TABLE offers ADD CONSTRAINT ck_offers_no_self_response
                CHECK (responds_to_offer_id IS NULL OR responds_to_offer_id <> id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE offers ADD CONSTRAINT ck_offers_sequence_positive
                CHECK (sequence >= 1);
                """);

            // A stated lapse cannot precede the moment the offer was made.
            migrationBuilder.Sql("""
                ALTER TABLE offers ADD CONSTRAINT ck_offers_expiry_after_communication
                CHECK (
                    expires_at IS NULL
                    OR communicated_at IS NULL
                    OR expires_at >= communicated_at);
                """);

            // An offer answers one in the same negotiation. Expressed as a foreign
            // key over the triple rather than as a check, because a check cannot
            // see another row: this is the structural version of the rule the
            // chain validator states.
            migrationBuilder.Sql("""
                ALTER TABLE offers ADD CONSTRAINT ak_offers_organization_deal_id
                UNIQUE (organization_id, deal_id, id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE offers ADD CONSTRAINT fk_offers_responds_to_same_deal
                FOREIGN KEY (organization_id, deal_id, responds_to_offer_id)
                REFERENCES offers (organization_id, deal_id, id);
                """);

            // ---- Term value shape ------------------------------------------
            //
            // Exactly one value shape per kind, and money always carries its
            // currency. The domain refuses these too, through the F# parser; this
            // is what makes a row that bypassed the domain impossible rather than
            // merely unlikely.
            migrationBuilder.Sql("""
                ALTER TABLE offer_terms ADD CONSTRAINT ck_offer_terms_value_shape
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

            // Money is never negative: a term records what somebody is to be paid,
            // and a deduction is a different term.
            migrationBuilder.Sql("""
                ALTER TABLE offer_terms ADD CONSTRAINT ck_offer_terms_money_non_negative
                CHECK (amount_value IS NULL OR amount_value >= 0);
                """);

            // The generous kind-level bound. The catalog narrows participation
            // terms to 100 in the domain, where the per-term rule belongs.
            migrationBuilder.Sql("""
                ALTER TABLE offer_terms ADD CONSTRAINT ck_offer_terms_percentage_range
                CHECK (
                    value_kind <> 2
                    OR (numeric_value >= 0 AND numeric_value <= 1000));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE offer_terms ADD CONSTRAINT ck_offer_terms_currency_shape
                CHECK (currency_code IS NULL OR currency_code ~ '^[A-Z]{3}$');
                """);

            // ---- One thread, one answer ------------------------------------
            //
            // The two invariants the milestone exists to protect. A partial unique
            // index is what actually settles a race between two people recording
            // counters, or accepting and countering, at the same moment.
            //
            // Status 2 is Open and 3 is Accepted.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_offers_one_open_per_deal
                ON offers (deal_id)
                WHERE status = 2;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_offers_one_accepted_per_deal
                ON offers (deal_id)
                WHERE status = 3;
                """);

            // One live negotiation per target per kind. Keyed on the kind because
            // one buyer can genuinely be negotiating a project sale and a
            // producing deal at once; two of the same kind means two colleagues
            // working the same thing without knowing about each other.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_deals_one_live_per_target_kind
                ON deals (opportunity_target_id, kind)
                WHERE status IN (1, 2, 3);
                """);

            // ---- Historical immutability -----------------------------------
            //
            // A different concept from append-only audit, and worth stating
            // plainly: the audit log records who did what, and this stops a
            // recorded offer's commercial snapshot from being rewritten at all.
            //
            // These triggers are deliberately dumb. They encode no business rule
            // beyond "a recorded offer is frozen", which the domain enforces
            // first; they exist so a future code path that forgets fails loudly
            // instead of quietly changing what the agency is recorded as having
            // proposed (ADR-0021).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_offer_terms_immutable()
                RETURNS trigger AS $$
                DECLARE
                    parent_status integer;
                BEGIN
                    SELECT status INTO parent_status
                    FROM offers
                    WHERE id = COALESCE(NEW.offer_id, OLD.offer_id);

                    -- Null means the offer itself is being deleted in this
                    -- transaction, so the cascade is allowed through.
                    IF parent_status IS NOT NULL AND parent_status <> 1 THEN
                        RAISE EXCEPTION
                            'offer % has been recorded; its terms are immutable',
                            COALESCE(NEW.offer_id, OLD.offer_id)
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_offer_terms_immutable
                BEFORE UPDATE OR DELETE ON offer_terms
                FOR EACH ROW EXECUTE FUNCTION agencyos_offer_terms_immutable();
                """);

            // The offer's own commercial record freezes with its terms. Status,
            // version and updated_at stay writable: those are the lifecycle, and
            // moving through it is the whole point.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_offers_frozen()
                RETURNS trigger AS $$
                BEGIN
                    IF OLD.status <> 1 AND (
                        NEW.deal_id IS DISTINCT FROM OLD.deal_id
                        OR NEW.direction IS DISTINCT FROM OLD.direction
                        OR NEW.responds_to_offer_id IS DISTINCT FROM OLD.responds_to_offer_id
                        OR NEW.sequence IS DISTINCT FROM OLD.sequence
                        OR NEW.communicated_at IS DISTINCT FROM OLD.communicated_at
                        OR NEW.summary IS DISTINCT FROM OLD.summary
                        OR NEW.notes IS DISTINCT FROM OLD.notes
                        OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
                    ) THEN
                        RAISE EXCEPTION
                            'offer % has been recorded; its commercial record is immutable',
                            OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_offers_frozen
                BEFORE UPDATE ON offers
                FOR EACH ROW EXECUTE FUNCTION agencyos_offers_frozen();
                """);

            // ---- Search -----------------------------------------------------
            //
            // Deals join the M3 ranked search. 'simple' rather than 'english' for
            // the reason M2 gave: these are names and references, and stemming
            // them loses the thing being searched for.
            //
            // strategy_notes is deliberately absent, exactly as opportunity
            // strategy is. Term values are absent too: a caller without
            // deals.economics.read must not be able to confirm a compensation
            // figure by searching for it and watching a deal surface (ADR-0021).
            migrationBuilder.Sql("""
                ALTER TABLE deals ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(name, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(reference, '')), 'B')
                    || setweight(to_tsvector('simple', coalesce(summary, '')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_deals_search_vector ON deals USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_deals_name_trgm ON deals USING gin (name gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_deals_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_deals_search_vector;");
            migrationBuilder.Sql("ALTER TABLE deals DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_offers_frozen ON offers;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_offers_frozen();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_offer_terms_immutable ON offer_terms;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_offer_terms_immutable();");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_deals_one_live_per_target_kind;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_offers_one_accepted_per_deal;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_offers_one_open_per_deal;");

            migrationBuilder.Sql(
                "ALTER TABLE offer_terms DROP CONSTRAINT IF EXISTS ck_offer_terms_currency_shape;");
            migrationBuilder.Sql(
                "ALTER TABLE offer_terms DROP CONSTRAINT IF EXISTS ck_offer_terms_percentage_range;");
            migrationBuilder.Sql(
                "ALTER TABLE offer_terms DROP CONSTRAINT IF EXISTS ck_offer_terms_money_non_negative;");
            migrationBuilder.Sql(
                "ALTER TABLE offer_terms DROP CONSTRAINT IF EXISTS ck_offer_terms_value_shape;");

            migrationBuilder.Sql(
                "ALTER TABLE offers DROP CONSTRAINT IF EXISTS fk_offers_responds_to_same_deal;");
            migrationBuilder.Sql(
                "ALTER TABLE offers DROP CONSTRAINT IF EXISTS ak_offers_organization_deal_id;");
            migrationBuilder.Sql(
                "ALTER TABLE offers DROP CONSTRAINT IF EXISTS ck_offers_expiry_after_communication;");
            migrationBuilder.Sql(
                "ALTER TABLE offers DROP CONSTRAINT IF EXISTS ck_offers_sequence_positive;");
            migrationBuilder.Sql(
                "ALTER TABLE offers DROP CONSTRAINT IF EXISTS ck_offers_no_self_response;");

            migrationBuilder.Sql("ALTER TABLE deals DROP CONSTRAINT IF EXISTS ck_deals_period;");
            migrationBuilder.Sql(
                "ALTER TABLE deals DROP CONSTRAINT IF EXISTS ck_deals_closed_consistent;");

            migrationBuilder.DropTable(
                name: "deal_events");

            migrationBuilder.DropTable(
                name: "deal_task_links");

            migrationBuilder.DropTable(
                name: "offer_events");

            migrationBuilder.DropTable(
                name: "offer_terms");

            migrationBuilder.DropTable(
                name: "offers");

            migrationBuilder.DropTable(
                name: "deals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_opportunity_targets_organization_id_opportunity_id_id",
                table: "opportunity_targets");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_targets_organization_id_opportunity_id",
                table: "opportunity_targets",
                columns: new[] { "organization_id", "opportunity_id" });
        }
    }
}
