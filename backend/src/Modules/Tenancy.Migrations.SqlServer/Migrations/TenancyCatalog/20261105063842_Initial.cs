using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saaskit.Tenancy.Migrations.SqlServer.Migrations.TenancyCatalog
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "tenancy",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.AuditLogId);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingRuns",
                schema: "tenancy",
                columns: table => new
                {
                    OnboardingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AdminName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServerPool = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NextStep = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingRuns", x => x.OnboardingRunId);
                });

            migrationBuilder.CreateTable(
                name: "Operators",
                schema: "tenancy",
                columns: table => new
                {
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TotpSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.OperatorId);
                });

            migrationBuilder.CreateTable(
                name: "Principals",
                schema: "tenancy",
                columns: table => new
                {
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Principals", x => x.PrincipalId);
                });

            migrationBuilder.CreateTable(
                name: "SupportAccessGrants",
                schema: "tenancy",
                columns: table => new
                {
                    SupportAccessGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccessGrants", x => x.SupportAccessGrantId);
                });

            migrationBuilder.CreateTable(
                name: "TenantPrincipalAccessAudit",
                schema: "tenancy",
                columns: table => new
                {
                    TenantPrincipalAccessAuditId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RemoteAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    TraceId = table.Column<string>(type: "char(32)", unicode: false, fixedLength: true, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPrincipalAccessAudit", x => x.TenantPrincipalAccessAuditId);
                });

            migrationBuilder.CreateTable(
                name: "TenantPrincipalLogins",
                schema: "tenancy",
                columns: table => new
                {
                    TenantPrincipalLoginId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrincipalType = table.Column<byte>(type: "tinyint", nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Method = table.Column<byte>(type: "tinyint", nullable: false),
                    FailureReason = table.Column<byte>(type: "tinyint", nullable: true),
                    AuthorizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemoteAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true),
                    TraceId = table.Column<string>(type: "char(32)", unicode: false, fixedLength: true, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPrincipalLogins", x => x.TenantPrincipalLoginId);
                });

            migrationBuilder.CreateTable(
                name: "PrincipalApplications",
                schema: "tenancy",
                columns: table => new
                {
                    PrincipalApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsExternal = table.Column<bool>(type: "bit", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrincipalApplications", x => x.PrincipalApplicationId);
                    table.ForeignKey(
                        name: "FK_PrincipalApplications_Principals_PrincipalApplicationId",
                        column: x => x.PrincipalApplicationId,
                        principalSchema: "tenancy",
                        principalTable: "Principals",
                        principalColumn: "PrincipalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrincipalDevices",
                schema: "tenancy",
                columns: table => new
                {
                    PrincipalDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceType = table.Column<byte>(type: "tinyint", nullable: false),
                    Identifier = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrincipalDevices", x => x.PrincipalDeviceId);
                    table.ForeignKey(
                        name: "FK_PrincipalDevices_Principals_PrincipalDeviceId",
                        column: x => x.PrincipalDeviceId,
                        principalSchema: "tenancy",
                        principalTable: "Principals",
                        principalColumn: "PrincipalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrincipalUsers",
                schema: "tenancy",
                columns: table => new
                {
                    PrincipalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrincipalUsers", x => x.PrincipalUserId);
                    table.ForeignKey(
                        name: "FK_PrincipalUsers_Principals_PrincipalUserId",
                        column: x => x.PrincipalUserId,
                        principalSchema: "tenancy",
                        principalTable: "Principals",
                        principalColumn: "PrincipalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantPrincipalAccess",
                schema: "tenancy",
                columns: table => new
                {
                    TenantPrincipalAccessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    GrantedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPrincipalAccess", x => x.TenantPrincipalAccessId);
                    table.ForeignKey(
                        name: "FK_TenantPrincipalAccess_Principals_PrincipalId",
                        column: x => x.PrincipalId,
                        principalSchema: "tenancy",
                        principalTable: "Principals",
                        principalColumn: "PrincipalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrincipalUserProviders",
                schema: "tenancy",
                columns: table => new
                {
                    PrincipalUserProviderId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrincipalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<byte>(type: "tinyint", nullable: false),
                    ProviderSubject = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    ProviderEmail = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: true),
                    LinkedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrincipalUserProviders", x => x.PrincipalUserProviderId);
                    table.ForeignKey(
                        name: "FK_PrincipalUserProviders_PrincipalUsers_PrincipalUserId",
                        column: x => x.PrincipalUserId,
                        principalSchema: "tenancy",
                        principalTable: "PrincipalUsers",
                        principalColumn: "PrincipalUserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TenantId_OccurredOn",
                schema: "tenancy",
                table: "AuditLog",
                columns: new[] { "TenantId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingRuns_Slug",
                schema: "tenancy",
                table: "OnboardingRuns",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_NormalizedEmail",
                schema: "tenancy",
                table: "Operators",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalDevices_DeviceType_Identifier",
                schema: "tenancy",
                table: "PrincipalDevices",
                columns: new[] { "DeviceType", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Principals_Type_DisplayName",
                schema: "tenancy",
                table: "Principals",
                columns: new[] { "Type", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalUserProviders_PrincipalUserId",
                schema: "tenancy",
                table: "PrincipalUserProviders",
                column: "PrincipalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalUserProviders_Provider_ProviderSubject",
                schema: "tenancy",
                table: "PrincipalUserProviders",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalUsers_Email",
                schema: "tenancy",
                table: "PrincipalUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_OperatorId",
                schema: "tenancy",
                table: "SupportAccessGrants",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_TenantId_Status",
                schema: "tenancy",
                table: "SupportAccessGrants",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalAccess_PrincipalId",
                schema: "tenancy",
                table: "TenantPrincipalAccess",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalAccess_TenantId_PrincipalId",
                schema: "tenancy",
                table: "TenantPrincipalAccess",
                columns: new[] { "TenantId", "PrincipalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalAccessAudit_PrincipalId_OccurredOn",
                schema: "tenancy",
                table: "TenantPrincipalAccessAudit",
                columns: new[] { "PrincipalId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalAccessAudit_TenantId_OccurredOn",
                schema: "tenancy",
                table: "TenantPrincipalAccessAudit",
                columns: new[] { "TenantId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalLogins_PrincipalId_OccurredOn",
                schema: "tenancy",
                table: "TenantPrincipalLogins",
                columns: new[] { "PrincipalId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPrincipalLogins_TenantId_OccurredOn",
                schema: "tenancy",
                table: "TenantPrincipalLogins",
                columns: new[] { "TenantId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "OnboardingRuns",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Operators",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PrincipalApplications",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PrincipalDevices",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PrincipalUserProviders",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SupportAccessGrants",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantPrincipalAccess",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantPrincipalAccessAudit",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantPrincipalLogins",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PrincipalUsers",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Principals",
                schema: "tenancy");
        }
    }
}
