using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthDevicePlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "platform",
                table: "auth_challenges");

            migrationBuilder.AddColumn<int>(
                name: "platform",
                table: "auth_clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "platform",
                table: "auth_clients");

            migrationBuilder.AddColumn<int>(
                name: "platform",
                table: "auth_challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
