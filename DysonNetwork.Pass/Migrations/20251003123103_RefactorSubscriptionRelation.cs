using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class RefactorSubscriptionRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wallet_subscriptions_wallet_gifts_gift_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_wallet_subscriptions_gift_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropColumn(
                name: "gift_id",
                table: "wallet_subscriptions");

            migrationBuilder.AddColumn<Guid>(
                name: "subscription_id",
                table: "wallet_gifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_subscription_id",
                table: "wallet_gifts",
                column: "subscription_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_gifts_wallet_subscriptions_subscription_id",
                table: "wallet_gifts",
                column: "subscription_id",
                principalTable: "wallet_subscriptions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wallet_gifts_wallet_subscriptions_subscription_id",
                table: "wallet_gifts");

            migrationBuilder.DropIndex(
                name: "ix_wallet_gifts_subscription_id",
                table: "wallet_gifts");

            migrationBuilder.DropColumn(
                name: "subscription_id",
                table: "wallet_gifts");

            migrationBuilder.AddColumn<Guid>(
                name: "gift_id",
                table: "wallet_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_gift_id",
                table: "wallet_subscriptions",
                column: "gift_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_subscriptions_wallet_gifts_gift_id",
                table: "wallet_subscriptions",
                column: "gift_id",
                principalTable: "wallet_gifts",
                principalColumn: "id");
        }
    }
}
