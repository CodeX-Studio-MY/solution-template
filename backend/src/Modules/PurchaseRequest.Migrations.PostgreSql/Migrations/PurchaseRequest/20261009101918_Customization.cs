using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.PostgreSql.Migrations.PurchaseRequest
{
    /// <inheritdoc />
    public partial class Customization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_fields_json",
                schema: "purchase_request",
                table: "Requests",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                schema: "purchase_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "character varying(60)", unicode: false, maxLength: 60, nullable: false),
                    key = table.Column<string>(type: "character varying(60)", unicode: false, maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    options_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_field_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_entity_key",
                schema: "purchase_request",
                table: "CustomFieldDefinitions",
                columns: new[] { "entity", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions",
                schema: "purchase_request");

            migrationBuilder.DropColumn(
                name: "custom_fields_json",
                schema: "purchase_request",
                table: "Requests");
        }
    }
}
