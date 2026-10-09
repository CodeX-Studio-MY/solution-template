using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Core.Catalog.Migrations.SqlServer.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class PlanLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanLimits",
                schema: "catalog",
                columns: table => new
                {
                    Plan = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PerTenantPerMinute = table.Column<int>(type: "int", nullable: false),
                    PerPrincipalPerMinute = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanLimits", x => x.Plan);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanLimits",
                schema: "catalog");
        }
    }
}
