using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Core.Catalog.Migrations.PostgreSql.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class TenantLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "delete_after",
                schema: "catalog",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "require_support_approval",
                schema: "catalog",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delete_after",
                schema: "catalog",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "require_support_approval",
                schema: "catalog",
                table: "Tenants");
        }
    }
}
