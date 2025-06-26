using System;
using System.Collections.Generic;
using DysonNetwork.Sphere.Connection.WebReader;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddWebArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_feeds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    preview = table.Column<LinkEmbed>(type: "jsonb", nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_feeds", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_feeds_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_articles",
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
                    table.PrimaryKey("pk_web_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_articles_web_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "web_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_web_articles_feed_id",
                table: "web_articles",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_articles_url",
                table: "web_articles",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_web_feeds_publisher_id",
                table: "web_feeds",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_feeds_url",
                table: "web_feeds",
                column: "url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_articles");

            migrationBuilder.DropTable(
                name: "web_feeds");
        }
    }
}
