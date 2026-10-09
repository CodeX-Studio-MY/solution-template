using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.SqlServer.Migrations.PurchaseRequest
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
                schema: "purchase_request");
        }
    }
}
