using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "language",
                table: "posts");

            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "posts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "slug",
                table: "posts");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "posts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }
    }
}
