using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class OverhaulMls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "group_info",
                table: "mls_group_states",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "ratchet_tree",
                table: "mls_group_states",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group_info",
                table: "mls_group_states");

            migrationBuilder.DropColumn(
                name: "ratchet_tree",
                table: "mls_group_states");
        }
    }
}
