using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class NewAgentMemoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_web_articles_web_feeds_feed_id",
                table: "web_articles");

            migrationBuilder.DropForeignKey(
                name: "fk_web_feed_subscriptions_web_feeds_feed_id",
                table: "web_feed_subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_web_feeds",
                table: "web_feeds");

            migrationBuilder.DropPrimaryKey(
                name: "pk_web_feed_subscriptions",
                table: "web_feed_subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_web_articles",
                table: "web_articles");

            migrationBuilder.RenameTable(
                name: "web_feeds",
                newName: "feeds");

            migrationBuilder.RenameTable(
                name: "web_feed_subscriptions",
                newName: "feed_subscriptions");

            migrationBuilder.RenameTable(
                name: "web_articles",
                newName: "feed_articles");

            migrationBuilder.RenameIndex(
                name: "ix_web_feed_subscriptions_feed_id",
                table: "feed_subscriptions",
                newName: "ix_feed_subscriptions_feed_id");

            migrationBuilder.RenameIndex(
                name: "ix_web_articles_feed_id",
                table: "feed_articles",
                newName: "ix_feed_articles_feed_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_feeds",
                table: "feeds",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_feed_subscriptions",
                table: "feed_subscriptions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_feed_articles",
                table: "feed_articles",
                column: "id");

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

            migrationBuilder.AddForeignKey(
                name: "fk_feed_articles_feeds_feed_id",
                table: "feed_articles",
                column: "feed_id",
                principalTable: "feeds",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_feed_subscriptions_feeds_feed_id",
                table: "feed_subscriptions",
                column: "feed_id",
                principalTable: "feeds",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_feed_articles_feeds_feed_id",
                table: "feed_articles");

            migrationBuilder.DropForeignKey(
                name: "fk_feed_subscriptions_feeds_feed_id",
                table: "feed_subscriptions");

            migrationBuilder.DropTable(
                name: "memory_records");

            migrationBuilder.DropPrimaryKey(
                name: "pk_feeds",
                table: "feeds");

            migrationBuilder.DropPrimaryKey(
                name: "pk_feed_subscriptions",
                table: "feed_subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_feed_articles",
                table: "feed_articles");

            migrationBuilder.RenameTable(
                name: "feeds",
                newName: "web_feeds");

            migrationBuilder.RenameTable(
                name: "feed_subscriptions",
                newName: "web_feed_subscriptions");

            migrationBuilder.RenameTable(
                name: "feed_articles",
                newName: "web_articles");

            migrationBuilder.RenameIndex(
                name: "ix_feed_subscriptions_feed_id",
                table: "web_feed_subscriptions",
                newName: "ix_web_feed_subscriptions_feed_id");

            migrationBuilder.RenameIndex(
                name: "ix_feed_articles_feed_id",
                table: "web_articles",
                newName: "ix_web_articles_feed_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_web_feeds",
                table: "web_feeds",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_web_feed_subscriptions",
                table: "web_feed_subscriptions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_web_articles",
                table: "web_articles",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_web_articles_web_feeds_feed_id",
                table: "web_articles",
                column: "feed_id",
                principalTable: "web_feeds",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_web_feed_subscriptions_web_feeds_feed_id",
                table: "web_feed_subscriptions",
                column: "feed_id",
                principalTable: "web_feeds",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
