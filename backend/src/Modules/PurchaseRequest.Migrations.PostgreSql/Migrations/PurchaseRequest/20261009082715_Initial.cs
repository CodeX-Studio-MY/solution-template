using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Acme.PurchaseRequest.Migrations.PostgreSql.Migrations.PurchaseRequest
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
                    principal_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.principal_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: "purchase_request",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox", x => new { x.event_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "purchase_request",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    trace_parent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    published_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "purchase_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quote_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    auto_approved = table.Column<bool>(type: "boolean", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rejected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    goods_received_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "SequenceCounters",
                schema: "purchase_request",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequence_counters", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "purchase_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RequestApprovals",
                schema: "purchase_request",
                columns: table => new
                {
                    level = table.Column<int>(type: "integer", nullable: false),
                    approver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_approvals", x => new { x.request_id, x.level, x.approver_id });
                    table.ForeignKey(
                        name: "fk_request_approvals_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "purchase_request",
                        principalTable: "Requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestLines",
                schema: "purchase_request",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_lines", x => new { x.request_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_request_lines_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "purchase_request",
                        principalTable: "Requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_created_on",
                schema: "purchase_request",
                table: "IdempotencyRecords",
                column: "created_on");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_id",
                schema: "purchase_request",
                table: "Outbox",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_published_on",
                schema: "purchase_request",
                table: "Outbox",
                column: "published_on");

            migrationBuilder.CreateIndex(
                name: "ix_requests_number",
                schema: "purchase_request",
                table: "Requests",
                column: "number",
                unique: true,
                filter: "\"is_deleted\" = false");

            migrationBuilder.CreateIndex(
                name: "ix_requests_requester_id_created_on",
                schema: "purchase_request",
                table: "Requests",
                columns: new[] { "requester_id", "created_on" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_status_submitted_on",
                schema: "purchase_request",
                table: "Requests",
                columns: new[] { "status", "submitted_on" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_key",
                schema: "purchase_request",
                table: "StoredFiles",
                column: "key",
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
