using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostCollectionBackgroundIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "background",
                table: "post_collections",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "icon",
                table: "post_collections",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background",
                table: "post_collections");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "post_collections");
        }
    }
}
