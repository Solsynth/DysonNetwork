using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePostSubscriptionsWithWatchPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_subscriptions");

            migrationBuilder.CreateTable(
                name: "post_watch_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    notify_reactions = table.Column<bool>(type: "boolean", nullable: false),
                    notify_replies = table.Column<bool>(type: "boolean", nullable: false),
                    notify_chains = table.Column<bool>(type: "boolean", nullable: false),
                    notify_forwards = table.Column<bool>(type: "boolean", nullable: false),
                    notify_edits = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_watch_preferences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_watch_preferences_account_id_source_deleted_at",
                table: "post_watch_preferences",
                columns: new[] { "account_id", "source", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_watch_preferences");

            migrationBuilder.CreateTable(
                name: "post_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    notify_edits = table.Column<bool>(type: "boolean", nullable: false),
                    notify_forwards = table.Column<bool>(type: "boolean", nullable: false),
                    notify_reactions = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_subscriptions_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_subscriptions_account_id_post_id_deleted_at",
                table: "post_subscriptions",
                columns: new[] { "account_id", "post_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_subscriptions_post_id",
                table: "post_subscriptions",
                column: "post_id");
        }
    }
}
