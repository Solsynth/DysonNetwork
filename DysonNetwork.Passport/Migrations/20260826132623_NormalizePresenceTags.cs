using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePresenceTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "presence_activity_tags",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_activity_tags", x => new { x.activity_id, x.slug });
                    table.ForeignKey(
                        name: "fk_presence_activity_tags_presence_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "presence_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "presence_catalog_tags",
                columns: table => new
                {
                    catalog_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_catalog_tags", x => new { x.catalog_id, x.slug });
                    table.ForeignKey(
                        name: "fk_presence_catalog_tags_presence_catalog_items_catalog_id",
                        column: x => x.catalog_id,
                        principalTable: "presence_catalog_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill from the jsonb tags columns (before dropping them)
            migrationBuilder.Sql(
                """
                INSERT INTO presence_activity_tags (activity_id, slug, name)
                SELECT a.id, t->>'slug', t->>'name'
                FROM presence_activities a
                CROSS JOIN LATERAL jsonb_array_elements(a.tags) AS t
                WHERE jsonb_typeof(a.tags) = 'array';
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO presence_catalog_tags (catalog_id, slug, name)
                SELECT c.id, t->>'slug', t->>'name'
                FROM presence_catalog_items c
                CROSS JOIN LATERAL jsonb_array_elements(c.tags) AS t
                WHERE jsonb_typeof(c.tags) = 'array';
                """);

            migrationBuilder.DropColumn(
                name: "tag_slugs",
                table: "presence_catalog_items");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "presence_catalog_items");

            migrationBuilder.DropColumn(
                name: "tag_slugs",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "presence_activities");

            migrationBuilder.CreateIndex(
                name: "ix_presence_activity_tags_slug",
                table: "presence_activity_tags",
                column: "slug");

            migrationBuilder.CreateIndex(
                name: "ix_presence_catalog_tags_slug",
                table: "presence_catalog_tags",
                column: "slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_activity_tags_slug",
                table: "presence_activity_tags");

            migrationBuilder.DropIndex(
                name: "ix_presence_catalog_tags_slug",
                table: "presence_catalog_tags");

            migrationBuilder.AddColumn<string>(
                name: "tag_slugs",
                table: "presence_catalog_items",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<List<SnPresenceTag>>(
                name: "tags",
                table: "presence_catalog_items",
                type: "jsonb",
                nullable: false,
                defaultValue: new List<SnPresenceTag>());

            migrationBuilder.AddColumn<string>(
                name: "tag_slugs",
                table: "presence_activities",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<List<SnPresenceTag>>(
                name: "tags",
                table: "presence_activities",
                type: "jsonb",
                nullable: false,
                defaultValue: new List<SnPresenceTag>());

            migrationBuilder.Sql(
                """
                UPDATE presence_activities a
                SET tags = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('slug', t.slug, 'name', t.name))
                    FROM presence_activity_tags t WHERE t.activity_id = a.id
                ), '[]'::jsonb),
                tag_slugs = COALESCE((
                    SELECT jsonb_agg(t.slug) FROM presence_activity_tags t WHERE t.activity_id = a.id
                ), '[]'::jsonb);
                """);

            migrationBuilder.Sql(
                """
                UPDATE presence_catalog_items c
                SET tags = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('slug', t.slug, 'name', t.name))
                    FROM presence_catalog_tags t WHERE t.catalog_id = c.id
                ), '[]'::jsonb),
                tag_slugs = COALESCE((
                    SELECT jsonb_agg(t.slug) FROM presence_catalog_tags t WHERE t.catalog_id = c.id
                ), '[]'::jsonb);
                """);

            migrationBuilder.DropTable(
                name: "presence_catalog_tags");

            migrationBuilder.DropTable(
                name: "presence_activity_tags");
        }
    }
}
