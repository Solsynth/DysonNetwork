using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddPresenceSessionsAndCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "catalog_id",
                table: "presence_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "presence_activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "ended_at",
                table: "presence_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "started_at",
                table: "presence_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "presence_activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "presence_catalog_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    catalog_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    reference_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    name = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    subtitle = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    caption = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    large_image = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    small_image = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    title_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    subtitle_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    queryable_terms = table.Column<string>(type: "jsonb", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    total_seconds = table.Column<long>(type: "bigint", nullable: false),
                    session_count = table.Column<int>(type: "integer", nullable: false),
                    last_active_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_catalog_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_presence_activities_account_id_started_at_ended_at_deleted_",
                table: "presence_activities",
                columns: new[] { "account_id", "started_at", "ended_at", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_catalog_items_account_id_category_deleted_at",
                table: "presence_catalog_items",
                columns: new[] { "account_id", "category", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_catalog_items_account_id_provider_catalog_key_dele",
                table: "presence_catalog_items",
                columns: new[] { "account_id", "provider", "catalog_key", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "presence_catalog_items");

            migrationBuilder.DropIndex(
                name: "ix_presence_activities_account_id_started_at_ended_at_deleted_",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "catalog_id",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "category",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "ended_at",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "presence_activities");
        }
    }
}
