using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardWidgetDisplayMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "board_widgets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "board_widgets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "board_widgets");

            migrationBuilder.DropColumn(
                name: "name",
                table: "board_widgets");
        }
    }
}
