using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSendingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_sending_plan_advances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_number = table.Column<int>(type: "integer", nullable: false),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    attempted_count = table.Column<int>(type: "integer", nullable: false),
                    sent_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    pending_count_after = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_sending_plan_advances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_sending_plan_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name_snapshot = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_interval_number = table.Column<int>(type: "integer", nullable: true),
                    last_resolved_email = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    processed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_sending_plan_recipients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_sending_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sending_plan_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    html_body = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    broadcast_to_all = table.Column<bool>(type: "boolean", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    max_emails_per_interval = table.Column<int>(type: "integer", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_emails_per_day = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    advanced_intervals_count = table.Column<int>(type: "integer", nullable: false),
                    planned_start_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    next_interval_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_advanced_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    paused_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_sending_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_sending_plan_advances_plan_id_interval_number_deleted",
                table: "email_sending_plan_advances",
                columns: new[] { "plan_id", "interval_number", "deleted_at" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_sending_plan_recipients_plan_id_account_id_deleted_at",
                table: "email_sending_plan_recipients",
                columns: new[] { "plan_id", "account_id", "deleted_at" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_sending_plans_sending_plan_key_deleted_at",
                table: "email_sending_plans",
                columns: new[] { "sending_plan_key", "deleted_at" },
                unique: true,
                filter: "deleted_at IS NULL AND sending_plan_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_sending_plan_advances");

            migrationBuilder.DropTable(
                name: "email_sending_plan_recipients");

            migrationBuilder.DropTable(
                name: "email_sending_plans");
        }
    }
}
