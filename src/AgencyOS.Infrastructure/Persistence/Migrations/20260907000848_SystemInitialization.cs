using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgencyOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemInitialization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_initialization",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    initialized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    initial_organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initial_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_initialization", x => x.id);
                    table.CheckConstraint("ck_system_initialization_singleton", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_initialization");
        }
    }
}
