using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Saaskit.Core.Catalog.Migrations.SqlServer.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class ModuleFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Modules",
                columns: new[] { "ModuleId", "DisplayName", "ModuleKey" },
                values: new object[] { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), "Feedback", "feedback" });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "PlanModuleAccess",
                columns: new[] { "ModuleId", "PlanId" },
                values: new object[,]
                {
                    { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)1 },
                    { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)2 },
                    { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "PlanModuleAccess",
                keyColumns: new[] { "ModuleId", "PlanId" },
                keyValues: new object[] { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)1 });

            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "PlanModuleAccess",
                keyColumns: new[] { "ModuleId", "PlanId" },
                keyValues: new object[] { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)2 });

            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "PlanModuleAccess",
                keyColumns: new[] { "ModuleId", "PlanId" },
                keyValues: new object[] { new Guid("00a8325e-8331-5f23-afd9-068111961bff"), (short)3 });

            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "Modules",
                keyColumn: "ModuleId",
                keyValue: new Guid("00a8325e-8331-5f23-afd9-068111961bff"));
        }
    }
}
