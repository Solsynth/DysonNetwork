using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCategoryWithTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_catalog_items_account_id_category_deleted_at",
                table: "presence_catalog_items");

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

            // Backfill tags + tag_slugs from the legacy category enum (0=unknown .. 9=other)
            migrationBuilder.Sql(
                """
                UPDATE presence_catalog_items
                SET tags = jsonb_build_array(
                        jsonb_build_object('slug', CASE category
                            WHEN 0 THEN 'unknown' WHEN 1 THEN 'gaming' WHEN 2 THEN 'coding'
                            WHEN 3 THEN 'music' WHEN 4 THEN 'productivity' WHEN 5 THEN 'entertainment'
                            WHEN 6 THEN 'social' WHEN 7 THEN 'fitness' WHEN 8 THEN 'reading'
                            ELSE 'other' END,
                        'name', CASE category
                            WHEN 0 THEN 'Unknown' WHEN 1 THEN 'Gaming' WHEN 2 THEN 'Coding'
                            WHEN 3 THEN 'Music' WHEN 4 THEN 'Productivity' WHEN 5 THEN 'Entertainment'
                            WHEN 6 THEN 'Social' WHEN 7 THEN 'Fitness' WHEN 8 THEN 'Reading'
                            ELSE 'Other' END)
                    ),
                    tag_slugs = jsonb_build_array(CASE category
                        WHEN 0 THEN 'unknown' WHEN 1 THEN 'gaming' WHEN 2 THEN 'coding'
                        WHEN 3 THEN 'music' WHEN 4 THEN 'productivity' WHEN 5 THEN 'entertainment'
                        WHEN 6 THEN 'social' WHEN 7 THEN 'fitness' WHEN 8 THEN 'reading'
                        ELSE 'other' END)
                WHERE category != 0;
                """);

            migrationBuilder.Sql(
                """
                UPDATE presence_activities
                SET tags = jsonb_build_array(
                        jsonb_build_object('slug', CASE category
                            WHEN 0 THEN 'unknown' WHEN 1 THEN 'gaming' WHEN 2 THEN 'coding'
                            WHEN 3 THEN 'music' WHEN 4 THEN 'productivity' WHEN 5 THEN 'entertainment'
                            WHEN 6 THEN 'social' WHEN 7 THEN 'fitness' WHEN 8 THEN 'reading'
                            ELSE 'other' END,
                        'name', CASE category
                            WHEN 0 THEN 'Unknown' WHEN 1 THEN 'Gaming' WHEN 2 THEN 'Coding'
                            WHEN 3 THEN 'Music' WHEN 4 THEN 'Productivity' WHEN 5 THEN 'Entertainment'
                            WHEN 6 THEN 'Social' WHEN 7 THEN 'Fitness' WHEN 8 THEN 'Reading'
                            ELSE 'Other' END)
                    ),
                    tag_slugs = jsonb_build_array(CASE category
                        WHEN 0 THEN 'unknown' WHEN 1 THEN 'gaming' WHEN 2 THEN 'coding'
                        WHEN 3 THEN 'music' WHEN 4 THEN 'productivity' WHEN 5 THEN 'entertainment'
                        WHEN 6 THEN 'social' WHEN 7 THEN 'fitness' WHEN 8 THEN 'reading'
                        ELSE 'other' END)
                WHERE category != 0;
                """);

            migrationBuilder.DropColumn(
                name: "category",
                table: "presence_catalog_items");

            migrationBuilder.DropColumn(
                name: "category",
                table: "presence_activities");

            migrationBuilder.CreateIndex(
                name: "ix_presence_catalog_items_account_id_deleted_at",
                table: "presence_catalog_items",
                columns: new[] { "account_id", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_catalog_items_account_id_deleted_at",
                table: "presence_catalog_items");

            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "presence_catalog_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "presence_activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE presence_catalog_items
                SET category = CASE tags->0->>'slug'
                    WHEN 'gaming' THEN 1 WHEN 'coding' THEN 2 WHEN 'music' THEN 3
                    WHEN 'productivity' THEN 4 WHEN 'entertainment' THEN 5 WHEN 'social' THEN 6
                    WHEN 'fitness' THEN 7 WHEN 'reading' THEN 8 WHEN 'other' THEN 9
                    ELSE 0 END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE presence_activities
                SET category = CASE tags->0->>'slug'
                    WHEN 'gaming' THEN 1 WHEN 'coding' THEN 2 WHEN 'music' THEN 3
                    WHEN 'productivity' THEN 4 WHEN 'entertainment' THEN 5 WHEN 'social' THEN 6
                    WHEN 'fitness' THEN 7 WHEN 'reading' THEN 8 WHEN 'other' THEN 9
                    ELSE 0 END;
                """);

            migrationBuilder.DropColumn(
                name: "tags",
                table: "presence_catalog_items");

            migrationBuilder.DropColumn(
                name: "tag_slugs",
                table: "presence_catalog_items");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "tag_slugs",
                table: "presence_activities");

            migrationBuilder.CreateIndex(
                name: "ix_presence_catalog_items_account_id_category_deleted_at",
                table: "presence_catalog_items",
                columns: new[] { "account_id", "category", "deleted_at" });
        }
    }
}
