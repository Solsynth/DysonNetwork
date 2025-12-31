using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFollowingBooleanInFediverse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_followed_by",
                table: "fediverse_relationships");

            migrationBuilder.DropColumn(
                name: "is_following",
                table: "fediverse_relationships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_followed_by",
                table: "fediverse_relationships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_following",
                table: "fediverse_relationships",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
