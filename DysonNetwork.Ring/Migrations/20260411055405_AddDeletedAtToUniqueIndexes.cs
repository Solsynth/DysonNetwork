using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_account_id_device_id",
                table: "push_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_notification_preferences_account_id_topic",
                table: "notification_preferences");

            migrationBuilder.DropIndex(
                name: "ix_notification_preferences_account_id_topic_deleted_at",
                table: "notification_preferences");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_account_id_device_id_deleted_at",
                table: "push_subscriptions",
                columns: new[] { "account_id", "device_id", "deleted_at" },
                unique: true,
                filter: "deleted_at IS NULL AND is_activated");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_account_id_topic_deleted_at",
                table: "notification_preferences",
                columns: new[] { "account_id", "topic", "deleted_at" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_account_id_device_id_deleted_at",
                table: "push_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_notification_preferences_account_id_topic_deleted_at",
                table: "notification_preferences");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_account_id_device_id",
                table: "push_subscriptions",
                columns: new[] { "account_id", "device_id" },
                unique: true,
                filter: "deleted_at IS NULL AND is_activated");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_account_id_topic",
                table: "notification_preferences",
                columns: new[] { "account_id", "topic" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_account_id_topic_deleted_at",
                table: "notification_preferences",
                columns: new[] { "account_id", "topic", "deleted_at" },
                unique: true);
        }
    }
}
