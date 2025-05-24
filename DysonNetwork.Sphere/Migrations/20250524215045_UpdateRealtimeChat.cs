using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRealtimeChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_push_subscriptions_device_id",
                table: "notification_push_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_notification_push_subscriptions_device_token",
                table: "notification_push_subscriptions");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "chat_realtime_call",
                newName: "session_id");

            migrationBuilder.AddColumn<string>(
                name: "provider_name",
                table: "chat_realtime_call",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "upstream",
                table: "chat_realtime_call",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id",
                table: "notification_push_subscriptions",
                columns: new[] { "device_token", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_push_subscriptions_device_token_device_id",
                table: "notification_push_subscriptions");

            migrationBuilder.DropColumn(
                name: "provider_name",
                table: "chat_realtime_call");

            migrationBuilder.DropColumn(
                name: "upstream",
                table: "chat_realtime_call");

            migrationBuilder.RenameColumn(
                name: "session_id",
                table: "chat_realtime_call",
                newName: "title");

            migrationBuilder.CreateIndex(
                name: "ix_notification_push_subscriptions_device_id",
                table: "notification_push_subscriptions",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_push_subscriptions_device_token",
                table: "notification_push_subscriptions",
                column: "device_token",
                unique: true);
        }
    }
}
