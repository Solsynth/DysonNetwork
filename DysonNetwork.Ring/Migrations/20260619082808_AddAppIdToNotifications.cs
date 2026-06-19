using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddAppIdToNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "app_id",
                table: "push_subscriptions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "app_id",
                table: "notifications",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "app_id",
                table: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "app_id",
                table: "notifications");
        }
    }
}
