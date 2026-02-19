using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddHlsEgressFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hls_egress_id",
                table: "live_streams",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hls_playlist_url",
                table: "live_streams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "hls_started_at",
                table: "live_streams",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hls_egress_id",
                table: "live_streams");

            migrationBuilder.DropColumn(
                name: "hls_playlist_url",
                table: "live_streams");

            migrationBuilder.DropColumn(
                name: "hls_started_at",
                table: "live_streams");
        }
    }
}
