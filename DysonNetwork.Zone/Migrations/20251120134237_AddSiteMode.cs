using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Zone.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preset",
                table: "publication_pages");

            migrationBuilder.AddColumn<int>(
                name: "mode",
                table: "publication_sites",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "publication_pages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mode",
                table: "publication_sites");

            migrationBuilder.DropColumn(
                name: "type",
                table: "publication_pages");

            migrationBuilder.AddColumn<string>(
                name: "preset",
                table: "publication_pages",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");
        }
    }
}
