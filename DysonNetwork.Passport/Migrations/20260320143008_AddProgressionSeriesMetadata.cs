using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressionSeriesMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "series_identifier",
                table: "quest_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "series_order",
                table: "quest_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_title",
                table: "quest_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_identifier",
                table: "achievement_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "series_order",
                table: "achievement_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_title",
                table: "achievement_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "series_identifier",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "series_order",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "series_title",
                table: "quest_definitions");

            migrationBuilder.DropColumn(
                name: "series_identifier",
                table: "achievement_definitions");

            migrationBuilder.DropColumn(
                name: "series_order",
                table: "achievement_definitions");

            migrationBuilder.DropColumn(
                name: "series_title",
                table: "achievement_definitions");
        }
    }
}
