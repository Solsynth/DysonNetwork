using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RenameHlsPlaylistUrlToPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hls_playlist_url",
                table: "live_streams");

            migrationBuilder.AddColumn<string>(
                name: "hls_playlist_path",
                table: "live_streams",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hls_playlist_path",
                table: "live_streams");

            migrationBuilder.AddColumn<string>(
                name: "hls_playlist_url",
                table: "live_streams",
                type: "text",
                nullable: true);
        }
    }
}
