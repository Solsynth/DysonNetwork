using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class SimplifiedPermissionNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_permission_nodes_key_area_actor",
                table: "permission_nodes");

            migrationBuilder.DropColumn(
                name: "area",
                table: "permission_nodes");

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "permission_nodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_actor",
                table: "permission_nodes",
                columns: new[] { "key", "actor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_permission_nodes_key_actor",
                table: "permission_nodes");

            migrationBuilder.DropColumn(
                name: "type",
                table: "permission_nodes");

            migrationBuilder.AddColumn<string>(
                name: "area",
                table: "permission_nodes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_area_actor",
                table: "permission_nodes",
                columns: new[] { "key", "area", "actor" });
        }
    }
}
