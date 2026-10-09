using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.SqlServer.Migrations.PurchaseRequest
{
    /// <inheritdoc />
    public partial class Customization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomFieldsJson",
                schema: "purchase_request",
                table: "Requests",
                type: "json",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                schema: "purchase_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Entity = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Key = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinitions_Entity_Key",
                schema: "purchase_request",
                table: "CustomFieldDefinitions",
                columns: new[] { "Entity", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions",
                schema: "purchase_request");

            migrationBuilder.DropColumn(
                name: "CustomFieldsJson",
                schema: "purchase_request",
                table: "Requests");
        }
    }
}
