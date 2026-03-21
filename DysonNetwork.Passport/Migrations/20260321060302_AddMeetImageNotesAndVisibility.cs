using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetImageNotesAndVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "image",
                table: "meets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "meets",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "meets",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image",
                table: "meets");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "meets");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "meets");
        }
    }
}
