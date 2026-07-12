using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryObservabilityRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_delivery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_delivery_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_delivery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    app_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    push_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_send_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    app_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    push_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_send_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_delivery_records_created_at_outcome",
                table: "email_delivery_records",
                columns: new[] { "created_at", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_records_created_at_topic_provider_out",
                table: "notification_delivery_records",
                columns: new[] { "created_at", "topic", "provider", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_send_records_created_at_topic",
                table: "notification_send_records",
                columns: new[] { "created_at", "topic" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_delivery_records");

            migrationBuilder.DropTable(
                name: "notification_delivery_records");

            migrationBuilder.DropTable(
                name: "notification_send_records");
        }
    }
}
