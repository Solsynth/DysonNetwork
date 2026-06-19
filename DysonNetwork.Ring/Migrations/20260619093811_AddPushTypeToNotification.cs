using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddPushTypeToNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "push_type",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "push_type",
                table: "notifications");
        }
    }
}
