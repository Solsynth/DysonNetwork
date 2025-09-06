using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "region",
                table: "accounts");
        }
    }
}
