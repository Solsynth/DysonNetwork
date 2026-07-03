using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AuthorizedAppScopesJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scopes",
                table: "authorized_apps",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scopes",
                table: "authorized_apps");
        }
    }
}
