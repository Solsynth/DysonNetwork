using System;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_achievements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_achievements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_quest_progresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    progress_count = table.Column<int>(type: "integer", nullable: false),
                    repeat_iteration_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_quest_progresses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "achievement_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_seed_managed = table.Column<bool>(type: "boolean", nullable: false),
                    target_count = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<SnProgressTriggerDefinition>(type: "jsonb", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_achievement_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_event_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    definition_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    period_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_event_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_reward_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    definition_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    period_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    badge_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    experience_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    source_points_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    notification_sent_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_reward_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quest_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_seed_managed = table.Column<bool>(type: "boolean", nullable: false),
                    target_count = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<SnProgressTriggerDefinition>(type: "jsonb", nullable: false),
                    schedule = table.Column<SnQuestScheduleConfig>(type: "jsonb", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id",
                table: "account_achievements",
                columns: new[] { "account_id", "achievement_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses",
                columns: new[] { "account_id", "quest_definition_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_definitions_identifier",
                table: "achievement_definitions",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts",
                columns: new[] { "event_id", "definition_type", "definition_identifier", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_reward_grants_reward_token",
                table: "progress_reward_grants",
                column: "reward_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quest_definitions_identifier",
                table: "quest_definitions",
                column: "identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_achievements");

            migrationBuilder.DropTable(
                name: "account_quest_progresses");

            migrationBuilder.DropTable(
                name: "achievement_definitions");

            migrationBuilder.DropTable(
                name: "progress_event_receipts");

            migrationBuilder.DropTable(
                name: "progress_reward_grants");

            migrationBuilder.DropTable(
                name: "quest_definitions");
        }
    }
}
