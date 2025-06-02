using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class FixPushNotificationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id",
                table: "notification_push_subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id_acco",
                table: "notification_push_subscriptions",
                columns: new[] { "device_token", "device_id", "account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id_acco",
                table: "notification_push_subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id",
                table: "notification_push_subscriptions",
                columns: new[] { "device_token", "device_id" },
                unique: true);
        }
    }
}
