using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.CostCenterBudget.Migrations.PostgreSql.Migrations.CostCenterBudget
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cost_center_budget");

            migrationBuilder.CreateTable(
                name: "SequenceCounters",
                schema: "cost_center_budget",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequence_counters", x => x.name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SequenceCounters",
                schema: "cost_center_budget");
        }
    }
}
