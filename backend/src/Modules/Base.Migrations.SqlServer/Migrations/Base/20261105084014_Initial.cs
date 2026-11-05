using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saaskit.Base.Migrations.SqlServer.Migrations.Base
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "base");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "base",
                columns: table => new
                {
                    PrincipalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.PrincipalId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: "base",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => new { x.EventId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "base",
                columns: table => new
                {
                    OutboxId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TraceParent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PublishedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "SequenceCounters",
                schema: "base",
                columns: table => new
                {
                    SequenceCounterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceCounters", x => x.SequenceCounterId);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "base",
                columns: table => new
                {
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.StoredFileId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CreatedOn",
                schema: "base",
                table: "IdempotencyRecords",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_EventId",
                schema: "base",
                table: "Outbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_PublishedOn",
                schema: "base",
                table: "Outbox",
                column: "PublishedOn");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Key",
                schema: "base",
                table: "StoredFiles",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "base");

            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "base");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "base");

            migrationBuilder.DropTable(
                name: "SequenceCounters",
                schema: "base");

            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "base");
        }
    }
}
