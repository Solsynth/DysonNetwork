using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddShadowbanAndAutomodRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "shadowban_reason",
                table: "publishers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "shadowbanned_at",
                table: "publishers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shadowban_reason",
                table: "posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "shadowbanned_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "automod_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    default_action = table.Column<int>(type: "integer", nullable: false),
                    pattern = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_regex = table.Column<bool>(type: "boolean", nullable: false),
                    derank_weight = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automod_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automod_rules_name",
                table: "automod_rules",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automod_rules");

            migrationBuilder.DropColumn(
                name: "shadowban_reason",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "shadowbanned_at",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "shadowban_reason",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "shadowbanned_at",
                table: "posts");
        }
    }
}
