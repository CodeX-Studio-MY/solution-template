using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Acme.Core.Catalog.Migrations.PostgreSql.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Modules",
                schema: "catalog",
                columns: table => new
                {
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_key = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modules", x => x.module_id);
                    table.CheckConstraint("CK_Modules_ModuleKey", "\"module_key\" ~ '^[a-z][a-z0-9-]{1,29}$'");
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    placement_database_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    placement_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    placement_secret_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    placement_server_pool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "PlanModules",
                schema: "catalog",
                columns: table => new
                {
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_modules", x => new { x.plan, x.module_id });
                    table.ForeignKey(
                        name: "fk_plan_modules_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "Modules",
                        principalColumn: "module_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantMigrationStates",
                schema: "catalog",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_migration = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_migration_states", x => new { x.tenant_id, x.target });
                    table.ForeignKey(
                        name: "fk_tenant_migration_states_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantModules",
                schema: "catalog",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frontend_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_modules", x => new { x.tenant_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_tenant_modules_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "Modules",
                        principalColumn: "module_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tenant_modules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Modules",
                columns: new[] { "module_id", "display_name", "module_key" },
                values: new object[,]
                {
                    { new Guid("0aa61768-e78c-597b-aee7-da49110e60f3"), "Cost Center Budget", "cost-center-budget" },
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), "Tenant Settings", "tenant-settings" },
                    { new Guid("68ec494c-c0b5-5b0c-8a17-c58460bc814c"), "Purchase Requests", "purchase-request" },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), "Access Management", "access-management" },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), "Billing", "billing" }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "PlanModules",
                columns: new[] { "module_id", "plan" },
                values: new object[,]
                {
                    { new Guid("0aa61768-e78c-597b-aee7-da49110e60f3"), "plus" },
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), "plus" },
                    { new Guid("68ec494c-c0b5-5b0c-8a17-c58460bc814c"), "plus" },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), "plus" },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), "plus" },
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), "standard" },
                    { new Guid("68ec494c-c0b5-5b0c-8a17-c58460bc814c"), "standard" },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), "standard" },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), "standard" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_modules_module_key",
                schema: "catalog",
                table: "Modules",
                column: "module_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_modules_module_id",
                schema: "catalog",
                table: "PlanModules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_modules_module_id",
                schema: "catalog",
                table: "TenantModules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "catalog",
                table: "Tenants",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanModules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantMigrationStates",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantModules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Modules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "catalog");
        }
    }
}
