using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddStickerPackIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_id",
                table: "stickers");

            migrationBuilder.AlterColumn<SnCloudFileReferenceObject>(
                name: "image",
                table: "stickers",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(SnCloudFileReferenceObject),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "icon",
                table: "sticker_packs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon",
                table: "sticker_packs");

            migrationBuilder.AlterColumn<SnCloudFileReferenceObject>(
                name: "image",
                table: "stickers",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(SnCloudFileReferenceObject),
                oldType: "jsonb");

            migrationBuilder.AddColumn<string>(
                name: "image_id",
                table: "stickers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }
    }
}
