using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class PublisherWithOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "realm_id",
                table: "publishers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishers_realm_id",
                table: "publishers",
                column: "realm_id");

            migrationBuilder.AddForeignKey(
                name: "fk_publishers_realms_realm_id",
                table: "publishers",
                column: "realm_id",
                principalTable: "realms",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_publishers_realms_realm_id",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_publishers_realm_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "publishers");
        }
    }
}
