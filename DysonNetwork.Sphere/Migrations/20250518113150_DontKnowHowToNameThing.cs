using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class DontKnowHowToNameThing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_stickers_slug",
                table: "stickers",
                column: "slug");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_packs_prefix",
                table: "sticker_packs",
                column: "prefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stickers_slug",
                table: "stickers");

            migrationBuilder.DropIndex(
                name: "ix_sticker_packs_prefix",
                table: "sticker_packs");
        }
    }
}
