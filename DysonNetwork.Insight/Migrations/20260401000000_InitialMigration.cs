using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "feeds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    verification_key = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    preview = table.Column<LinkEmbed>(type: "jsonb", nullable: true),
                    config = table.Column<WebFeedConfig>(type: "jsonb", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feeds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "interactive_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    behaviour = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interactive_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memory_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_hot = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    confidence = table.Column<float>(type: "real", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_accessed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mood_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    energy_level = table.Column<float>(type: "real", nullable: false),
                    positivity_level = table.Column<float>(type: "real", nullable: false),
                    sociability_level = table.Column<float>(type: "real", nullable: false),
                    curiosity_level = table.Column<float>(type: "real", nullable: false),
                    current_mood_description = table.Column<string>(type: "text", nullable: false),
                    recent_emotional_events = table.Column<string>(type: "text", nullable: true),
                    last_mood_update = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    interactions_since_update = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mood_states", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "thinking_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    total_token = table.Column<long>(type: "bigint", nullable: false),
                    paid_token = table.Column<long>(type: "bigint", nullable: false),
                    free_tokens = table.Column<long>(type: "bigint", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_initiated = table.Column<bool>(type: "boolean", nullable: false),
                    user_last_read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_message_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thinking_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unpaid_accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    marked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unpaid_accounts", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_summary = table.Column<string>(type: "text", nullable: true),
                    impression_summary = table.Column<string>(type: "text", nullable: true),
                    relationship_summary = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    favorability = table.Column<int>(type: "integer", nullable: false),
                    trust_level = table.Column<int>(type: "integer", nullable: false),
                    intimacy_level = table.Column<int>(type: "integer", nullable: false),
                    interaction_count = table.Column<int>(type: "integer", nullable: false),
                    last_interaction_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_profile_update_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feed_articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    author = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    preview = table.Column<LinkEmbed>(type: "jsonb", nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_feed_articles_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_feed_subscriptions_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thinking_thoughts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parts = table.Column<List<SnThinkingMessagePart>>(type: "jsonb", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    token_count = table.Column<long>(type: "bigint", nullable: false),
                    model_name = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    bot_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thinking_thoughts", x => x.id);
                    table.ForeignKey(
                        name: "fk_thinking_thoughts_thinking_sequences_sequence_id",
                        column: x => x.sequence_id,
                        principalTable: "thinking_sequences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feed_articles_feed_id",
                table: "feed_articles",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_subscriptions_feed_id",
                table: "feed_subscriptions",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_thinking_thoughts_sequence_id",
                table: "thinking_thoughts",
                column: "sequence_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_account_id",
                table: "user_profiles",
                column: "account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_articles");

            migrationBuilder.DropTable(
                name: "feed_subscriptions");

            migrationBuilder.DropTable(
                name: "interactive_history");

            migrationBuilder.DropTable(
                name: "memory_records");

            migrationBuilder.DropTable(
                name: "mood_states");

            migrationBuilder.DropTable(
                name: "scheduled_tasks");

            migrationBuilder.DropTable(
                name: "thinking_thoughts");

            migrationBuilder.DropTable(
                name: "unpaid_accounts");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "feeds");

            migrationBuilder.DropTable(
                name: "thinking_sequences");
        }
    }
}
