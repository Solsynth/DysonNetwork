using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class EnrichCloudPoolConfigure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_anonymous",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "allow_encryption",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "no_metadata",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "no_optimization",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "require_privilege",
                table: "pools",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_anonymous",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "allow_encryption",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "no_metadata",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "no_optimization",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "require_privilege",
                table: "pools");
        }
    }
}
