using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Saaskit.Core.Catalog.Migrations.SqlServer.Migrations.Catalog
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
                name: "Plans",
                schema: "catalog",
                columns: table => new
                {
                    PlanId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanKey = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.PlanId);
                    table.CheckConstraint("CK_Plans_PlanKey", "REGEXP_LIKE([PlanKey], '^[a-z][a-z0-9-]{1,29}$')");
                });

            migrationBuilder.CreateTable(
                name: "PlanLimits",
                schema: "catalog",
                columns: table => new
                {
                    PlanLimitId = table.Column<short>(type: "smallint", nullable: false),
                    PerTenantPerMinute = table.Column<int>(type: "int", nullable: false),
                    PerPrincipalPerMinute = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanLimits", x => x.PlanLimitId);
                    table.ForeignKey(
                        name: "FK_PlanLimits_Plans_PlanLimitId",
                        column: x => x.PlanLimitId,
                        principalSchema: "catalog",
                        principalTable: "Plans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanModuleAccess",
                schema: "catalog",
                columns: table => new
                {
                    PlanId = table.Column<short>(type: "smallint", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModuleAccess", x => new { x.PlanId, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_PlanModuleAccess_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "catalog",
                        principalTable: "Modules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanModuleAccess_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "catalog",
                        principalTable: "Plans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlanId = table.Column<short>(type: "smallint", nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeleteAfter = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequireSupportApproval = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_Tenants", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_Tenants_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "catalog",
                        principalTable: "Plans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantFeatures",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureKey = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantFeatures", x => new { x.TenantId, x.FeatureKey });
                    table.ForeignKey(
                        name: "FK_TenantFeatures_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
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
                        principalColumn: "TenantId",
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
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleKey = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", maxLength: 250000, nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => new { x.TenantId, x.ModuleKey });
                    table.ForeignKey(
                        name: "FK_TenantSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Modules",
                columns: new[] { "ModuleId", "DisplayName", "ModuleKey" },
                values: new object[,]
                {
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), "Tenant Settings", "tenant-settings" },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), "Access Management", "access-management" },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), "Billing", "billing" }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Plans",
                columns: new[] { "PlanId", "CreatedBy", "CreatedOn", "Currency", "Description", "DisplayName", "IsActive", "MonthlyPrice", "PlanKey", "SortOrder", "UpdatedBy", "UpdatedOn" },
                values: new object[,]
                {
                    { (short)1, "system", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "MYR", null, "Lite", true, null, "lite", (byte)1, null, null },
                    { (short)2, "system", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "MYR", null, "Advanced", true, null, "advanced", (byte)2, null, null },
                    { (short)3, "system", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "MYR", null, "Complete", true, null, "complete", (byte)3, null, null }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "PlanModuleAccess",
                columns: new[] { "ModuleId", "PlanId" },
                values: new object[,]
                {
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), (short)1 },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), (short)1 },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), (short)1 },
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), (short)2 },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), (short)2 },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), (short)2 },
                    { new Guid("4b8bdd8f-2cd0-5914-9d29-e17f948aca9b"), (short)3 },
                    { new Guid("9a8337a5-3cd5-5963-9b61-886a163e630f"), (short)3 },
                    { new Guid("d39d3a7b-c5dc-560a-b527-ce06d0158063"), (short)3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ModuleKey",
                schema: "catalog",
                table: "Modules",
                column: "ModuleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanModuleAccess_ModuleId",
                schema: "catalog",
                table: "PlanModuleAccess",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_PlanKey",
                schema: "catalog",
                table: "Plans",
                column: "PlanKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantModules_ModuleId",
                schema: "catalog",
                table: "TenantModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_PlanId",
                schema: "catalog",
                table: "Tenants",
                column: "PlanId");

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
                name: "PlanLimits",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PlanModuleAccess",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantFeatures",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantMigrationStates",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantModules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantSettings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Modules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "catalog");
        }
    }
}
