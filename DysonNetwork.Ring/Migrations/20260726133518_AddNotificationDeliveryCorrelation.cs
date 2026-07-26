using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Ring.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "notification_id",
                table: "notification_delivery_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subscription_id",
                table: "notification_delivery_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_records_notification_id_subscription_",
                table: "notification_delivery_records",
                columns: new[] { "notification_id", "subscription_id", "provider", "outcome" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_delivery_records_notification_id_subscription_",
                table: "notification_delivery_records");

            migrationBuilder.DropColumn(
                name: "notification_id",
                table: "notification_delivery_records");

            migrationBuilder.DropColumn(
                name: "subscription_id",
                table: "notification_delivery_records");
        }
    }
}
