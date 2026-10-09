using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.SqlServer.Migrations.PurchaseRequest
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchase_request");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "purchase_request",
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
                schema: "purchase_request",
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
                schema: "purchase_request",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
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
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "purchase_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    QuoteFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoApproved = table.Column<bool>(type: "bit", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GoodsReceivedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SequenceCounters",
                schema: "purchase_request",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceCounters", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "purchase_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestApprovals",
                schema: "purchase_request",
                columns: table => new
                {
                    Level = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestApprovals", x => new { x.RequestId, x.Level, x.ApproverId });
                    table.ForeignKey(
                        name: "FK_RequestApprovals_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "purchase_request",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestLines",
                schema: "purchase_request",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Item = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLines", x => new { x.RequestId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_RequestLines_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "purchase_request",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CreatedOn",
                schema: "purchase_request",
                table: "IdempotencyRecords",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_EventId",
                schema: "purchase_request",
                table: "Outbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_PublishedOn",
                schema: "purchase_request",
                table: "Outbox",
                column: "PublishedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Number",
                schema: "purchase_request",
                table: "Requests",
                column: "Number",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_RequesterId_CreatedOn",
                schema: "purchase_request",
                table: "Requests",
                columns: new[] { "RequesterId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_SubmittedOn",
                schema: "purchase_request",
                table: "Requests",
                columns: new[] { "Status", "SubmittedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Key",
                schema: "purchase_request",
                table: "StoredFiles",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "RequestApprovals",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "RequestLines",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "SequenceCounters",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "purchase_request");

            migrationBuilder.DropTable(
                name: "Requests",
                schema: "purchase_request");
        }
    }
}
