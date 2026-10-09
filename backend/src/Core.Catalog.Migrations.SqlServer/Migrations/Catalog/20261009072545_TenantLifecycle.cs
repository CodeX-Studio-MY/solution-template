using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Core.Catalog.Migrations.SqlServer.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class TenantLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteAfter",
                schema: "catalog",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireSupportApproval",
                schema: "catalog",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleteAfter",
                schema: "catalog",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RequireSupportApproval",
                schema: "catalog",
                table: "Tenants");
        }
    }
}
