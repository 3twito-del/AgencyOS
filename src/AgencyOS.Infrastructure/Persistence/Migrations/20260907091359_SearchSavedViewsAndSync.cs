using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchSavedViewsAndSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "professional_relationships",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "people",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "change_log",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_log", x => new { x.organization_id, x.sequence });
                    table.ForeignKey(
                        name: "FK_change_log_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "change_sequence",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_sequence", x => x.organization_id);
                    table.ForeignKey(
                        name: "FK_change_sequence_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.organization_id, x.key });
                    table.ForeignKey(
                        name: "FK_idempotency_keys_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_views", x => x.id);
                    table.ForeignKey(
                        name: "FK_saved_views_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_organization_owner",
                table: "saved_views",
                columns: new[] { "organization_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_saved_views_owner_name",
                table: "saved_views",
                columns: new[] { "organization_id", "owner_user_id", "name" },
                unique: true);

            // ---- Search: extension, prefix-query helper, indexed vectors ----

            // pg_trgm is a trusted extension, so a database owner can install it
            // without superuser. It backs the fuzzy arm of ranking: without it a
            // misspelled name returns nothing rather than the right person.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Builds a prefix tsquery from raw user text.
            //
            // Escaping lives here, in one reviewed place, rather than in whichever
            // C# string happens to build a query next. Input is split on anything
            // that is not alphanumeric, so every token is [a-z0-9]+ by
            // construction and cannot carry tsquery syntax: a search for
            // "smith & !jones" is three words, not an operator expression. The
            // function is IMMUTABLE and STRICT so the planner can use it freely
            // and a null query short-circuits to null rather than to everything.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION agencyos_prefix_tsquery(input text)
                RETURNS tsquery
                LANGUAGE sql
                IMMUTABLE
                STRICT
                PARALLEL SAFE
                AS $$
                    SELECT to_tsquery('simple', string_agg(quote_literal(token) || ':*', ' & '))
                    FROM unnest(regexp_split_to_array(lower(btrim(input)), '[^[:alnum:]]+')) AS t(token)
                    WHERE token <> ''
                $$;
                """);

            // Generated columns rather than triggers: the vector cannot drift from
            // the row, because it is not a second copy that something has to
            // remember to update. 'simple' rather than 'english' is deliberate -
            // these are proper names, and stemming "Reid" or "Downing" is how a
            // search engine loses the person you are looking for.
            //
            // Weights carry intent: A is the name shown in the list, B the other
            // names a person is known by, C supporting fields.
            //
            // Addresses are indexed twice: once whole, once split on punctuation.
            // 'simple' tokenizes "sarah@example.com" as one lexeme, and a prefix
            // query for that address becomes three prefix terms ANDed together,
            // which the single lexeme cannot satisfy - so searching a person by
            // their own email address would find nothing. Indexing the split form
            // as well makes both the whole address and any word in it match.
            migrationBuilder.Sql("""
                ALTER TABLE people ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(display_name, '')), 'A')
                    || setweight(to_tsvector('simple',
                        coalesce(first_name, '') || ' ' || coalesce(last_name, '') || ' '
                        || coalesce(preferred_name, '') || ' ' || coalesce(middle_name, '')), 'B')
                    || setweight(to_tsvector('simple',
                        coalesce(title, '') || ' ' || coalesce(email, '') || ' '
                        || regexp_replace(coalesce(email, ''), '[^[:alnum:]]+', ' ', 'g')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE companies ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(name, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(legal_name, '')), 'B')
                    || setweight(to_tsvector('simple',
                        coalesce(website, '') || ' '
                        || regexp_replace(coalesce(website, ''), '[^[:alnum:]]+', ' ', 'g')), 'C')
                ) STORED;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tasks ADD COLUMN search_vector tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(title, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(notes, '')), 'C')
                ) STORED;
                """);

            // One GIN index per table for full text, one for trigram similarity.
            // The tenant predicate is applied as a filter rather than being folded
            // into the index: at one agency's volume that is the right trade, and
            // btree_gin exists if a profile ever says otherwise.
            migrationBuilder.Sql(
                "CREATE INDEX ix_people_search_vector ON people USING gin (search_vector);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_companies_search_vector ON companies USING gin (search_vector);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_tasks_search_vector ON tasks USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_people_display_name_trgm ON people USING gin (display_name gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_companies_name_trgm ON companies USING gin (name gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_tasks_title_trgm ON tasks USING gin (title gin_trgm_ops);");

            // ---- Synchronization: seed the change feed for existing records ----

            // A client syncing from position zero reads the feed like any other
            // client, so every record that already exists needs an entry. Without
            // this, an existing tenant's first sync would return nothing and the
            // cache would look correct and be empty.
            //
            // Order within a tenant is by last change then identifier, which is
            // deterministic and re-runnable. It is not real commit order - that
            // history was never recorded - and it does not need to be: the client
            // ends up at the same state either way.
            migrationBuilder.Sql("""
                WITH seeded AS (
                    SELECT
                        organization_id,
                        entity_type,
                        entity_id,
                        occurred_at,
                        row_number() OVER (
                            PARTITION BY organization_id
                            ORDER BY occurred_at, entity_type, entity_id
                        ) AS seq
                    FROM (
                        SELECT organization_id, 'Person' AS entity_type,
                               id::text AS entity_id, updated_at AS occurred_at
                        FROM people
                        UNION ALL
                        SELECT organization_id, 'Company', id::text, updated_at FROM companies
                        UNION ALL
                        SELECT organization_id, 'TaskItem', id::text, updated_at FROM tasks
                    ) existing
                )
                INSERT INTO change_log (organization_id, sequence, entity_type, entity_id, kind, occurred_at)
                SELECT organization_id, seq, entity_type, entity_id, 1, occurred_at
                FROM seeded;
                """);

            migrationBuilder.Sql("""
                INSERT INTO change_sequence (organization_id, last_value)
                SELECT organization_id, MAX(sequence) FROM change_log GROUP BY organization_id
                ON CONFLICT (organization_id) DO UPDATE SET last_value = EXCLUDED.last_value;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tasks_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_companies_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_people_display_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tasks_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_companies_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_people_search_vector;");

            migrationBuilder.Sql("ALTER TABLE tasks DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.Sql("ALTER TABLE companies DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.Sql("ALTER TABLE people DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS agencyos_prefix_tsquery(text);");

            // pg_trgm is deliberately left installed. Dropping an extension that
            // something else may since have come to depend on is a worse outcome
            // than leaving an unused one behind.

            migrationBuilder.DropTable(
                name: "change_log");

            migrationBuilder.DropTable(
                name: "change_sequence");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "saved_views");

            migrationBuilder.DropColumn(
                name: "version",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "version",
                table: "professional_relationships");

            migrationBuilder.DropColumn(
                name: "version",
                table: "people");

            migrationBuilder.DropColumn(
                name: "version",
                table: "companies");
        }
    }
}
