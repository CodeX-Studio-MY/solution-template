using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.CostCenterBudget.Migrations.SqlServer.Migrations.CostCenterBudget
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
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceCounters", x => x.Name);
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
