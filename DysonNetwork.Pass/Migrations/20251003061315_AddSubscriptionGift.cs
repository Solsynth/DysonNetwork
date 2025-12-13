using System;
using System.Collections.Generic;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionGift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_gifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gifter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gift_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    subscription_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    redeemed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    redeemer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    is_open_gift = table.Column<bool>(type: "boolean", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_gifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_gifter_id",
                        column: x => x.gifter_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_redeemer_id",
                        column: x => x.redeemer_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_gifts_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                });

            migrationBuilder.AddColumn<Guid>(
                name: "gift_id",
                table: "wallet_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_coupon_id",
                table: "wallet_gifts",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_gift_code",
                table: "wallet_gifts",
                column: "gift_code");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_gifter_id",
                table: "wallet_gifts",
                column: "gifter_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_recipient_id",
                table: "wallet_gifts",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_redeemer_id",
                table: "wallet_gifts",
                column: "redeemer_id");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wallet_subscriptions_wallet_gifts_gift_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "wallet_gifts");

            migrationBuilder.DropIndex(
                name: "ix_wallet_subscriptions_gift_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropColumn(
                name: "gift_id",
                table: "wallet_subscriptions");
        }
    }
}