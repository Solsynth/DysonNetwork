using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class EnrichFediverseInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "active_users",
                table: "fediverse_instances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_account_username",
                table: "fediverse_instances",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "fediverse_instances",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon_url",
                table: "fediverse_instances",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "metadata_fetched_at",
                table: "fediverse_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_url",
                table: "fediverse_instances",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active_users",
                table: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "contact_account_username",
                table: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "icon_url",
                table: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "metadata_fetched_at",
                table: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "thumbnail_url",
                table: "fediverse_instances");
        }
    }
}
