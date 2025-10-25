using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOutdatedFileIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "picture_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "background_id",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "picture_id",
                table: "chat_rooms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "background_id",
                table: "publishers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "picture_id",
                table: "publishers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "background_id",
                table: "chat_rooms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "picture_id",
                table: "chat_rooms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }
    }
}
