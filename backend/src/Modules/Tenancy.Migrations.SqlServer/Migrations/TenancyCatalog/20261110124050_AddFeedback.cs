using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saaskit.Tenancy.Migrations.SqlServer.Migrations.TenancyCatalog
{
    /// <inheritdoc />
    public partial class AddFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Feedback",
                schema: "tenancy",
                columns: table => new
                {
                    FeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PagePath = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedback", x => x.FeedbackId);
                    table.CheckConstraint("CK_Feedback_Rating", "[Rating] IS NULL OR [Rating] BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_Status_CreatedOn",
                schema: "tenancy",
                table: "Feedback",
                columns: new[] { "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_TenantId_CreatedOn",
                schema: "tenancy",
                table: "Feedback",
                columns: new[] { "TenantId", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Feedback",
                schema: "tenancy");
        }
    }
}
