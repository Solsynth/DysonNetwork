using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddFediverseCommunityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_community",
                table: "sn_fediverse_actor",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "sn_fediverse_actor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "sn_fediverse_relationship",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_community",
                table: "sn_fediverse_actor");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "sn_fediverse_actor");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "sn_fediverse_relationship");
        }
    }
}
