using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptionActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_account_id_device_id_deleted_at",
                table: "push_subscriptions");

            migrationBuilder.AddColumn<bool>(
                name: "is_activated",
                table: "push_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_account_id_device_id",
                table: "push_subscriptions",
                columns: new[] { "account_id", "device_id" },
                unique: true,
                filter: "deleted_at IS NULL AND is_activated");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_account_id_device_id_provider_deleted_at",
                table: "push_subscriptions",
                columns: new[] { "account_id", "device_id", "provider", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_account_id_device_id",
                table: "push_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_account_id_device_id_provider_deleted_at",
                table: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "is_activated",
                table: "push_subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_account_id_device_id_deleted_at",
                table: "push_subscriptions",
                columns: new[] { "account_id", "device_id", "deleted_at" },
                unique: true);
        }
    }
}
