using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class EnrichPresenceActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "large_image",
                table: "presence_activities",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "small_image",
                table: "presence_activities",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitle_url",
                table: "presence_activities",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title_url",
                table: "presence_activities",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "large_image",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "small_image",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "subtitle_url",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "title_url",
                table: "presence_activities");
        }
    }
}
