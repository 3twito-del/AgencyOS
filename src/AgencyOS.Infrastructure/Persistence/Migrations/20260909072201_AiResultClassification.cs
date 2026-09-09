using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiResultClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "result_sensitivity",
                table: "ai_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Internal, Confidential or Protected. Restricted is absent because a
            // result cannot be classified above what its inputs were, and the data
            // policy never admits Restricted material into a context at any
            // residency -- so a Restricted result would mean the classification had
            // already been breached upstream (ADR-0031, ADR-0035).
            migrationBuilder.Sql("""
                ALTER TABLE ai_runs
                ADD CONSTRAINT ck_ai_runs_result_sensitivity
                    CHECK (result_sensitivity IN (1, 2, 3));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ai_runs "
                    + "DROP CONSTRAINT IF EXISTS ck_ai_runs_result_sensitivity;");

            migrationBuilder.DropColumn(
                name: "result_sensitivity",
                table: "ai_runs");
        }
    }
}
