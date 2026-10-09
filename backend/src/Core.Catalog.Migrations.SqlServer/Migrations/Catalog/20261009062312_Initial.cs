using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Acme.Core.Catalog.Migrations.SqlServer.Migrations.Catalog
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
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleKey = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.ModuleId);
                    table.CheckConstraint("CK_Modules_ModuleKey", "REGEXP_LIKE([ModuleKey], '^[a-z][a-z0-9-]{1,29}$')");
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Placement_DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Placement_Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Placement_SecretReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Placement_ServerPool = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanModules",
                schema: "catalog",
                columns: table => new
                {
                    Plan = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModules", x => new { x.Plan, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_PlanModules_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "catalog",
                        principalTable: "Modules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantMigrationStates",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastMigration = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMigrationStates", x => new { x.TenantId, x.Target });
                    table.ForeignKey(
                        name: "FK_TenantMigrationStates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantModules",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrontendVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantModules", x => new { x.TenantId, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_TenantModules_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "catalog",
                        principalTable: "Modules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantModules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Modules",
                columns: new[] { "ModuleId", "DisplayName", "ModuleKey" },
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
                columns: new[] { "ModuleId", "Plan" },
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
                name: "IX_Modules_ModuleKey",
                schema: "catalog",
                table: "Modules",
                column: "ModuleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanModules_ModuleId",
                schema: "catalog",
                table: "PlanModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModules_ModuleId",
                schema: "catalog",
                table: "TenantModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                schema: "catalog",
                table: "Tenants",
                column: "Slug",
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
