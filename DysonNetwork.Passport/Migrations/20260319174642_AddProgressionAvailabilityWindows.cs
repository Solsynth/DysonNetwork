using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressionAvailabilityWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "available_from",
                table: "quest_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "available_until",
                table: "quest_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_progress_enabled",
                table: "quest_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Instant>(
                name: "available_from",
                table: "achievement_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "available_until",
                table: "achievement_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_progress_enabled",
                table: "achievement_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "available_from",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "available_until",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "is_progress_enabled",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "available_from",
                table: "achievement_definitions");

            migrationBuilder.DropColumn(
                name: "available_until",
                table: "achievement_definitions");

            migrationBuilder.DropColumn(
                name: "is_progress_enabled",
                table: "achievement_definitions");
        }
    }
}
