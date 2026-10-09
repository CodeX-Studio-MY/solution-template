using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Core.Catalog.Migrations.PostgreSql.Migrations.Catalog
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
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    per_tenant_per_minute = table.Column<int>(type: "integer", nullable: false),
                    per_principal_per_minute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_limits", x => x.plan);
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
