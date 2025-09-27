using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddRealmPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_posts_realm_id",
                table: "posts",
                column: "realm_id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_realms_realm_id",
                table: "posts",
                column: "realm_id",
                principalTable: "realms",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_realms_realm_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_realm_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "posts");
        }
    }
}
