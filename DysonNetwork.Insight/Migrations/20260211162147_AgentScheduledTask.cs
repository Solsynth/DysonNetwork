using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AgentScheduledTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    recurrence_interval = table.Column<Duration>(type: "interval", nullable: true),
                    recurrence_end_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    context = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    execution_count = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_executed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_tasks", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_tasks");
        }
    }
}
