using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.PostgreSql.Migrations.PurchaseRequest
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchase_request");

            migrationBuilder.CreateTable(
                name: "SequenceCounters",
                schema: "purchase_request",
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
                schema: "purchase_request");
        }
    }
}
