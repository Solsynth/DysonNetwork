using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lotteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    region_two_number = table.Column<int>(type: "integer", nullable: false),
                    multiplier = table.Column<int>(type: "integer", nullable: false),
                    draw_status = table.Column<int>(type: "integer", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    matched_region_one_numbers = table.Column<string>(type: "jsonb", nullable: true),
                    matched_region_two_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotteries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lottery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    winning_region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    winning_region_two_number = table.Column<int>(type: "integer", nullable: false),
                    total_tickets = table.Column<int>(type: "integer", nullable: false),
                    total_prizes_awarded = table.Column<int>(type: "integer", nullable: false),
                    total_prize_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lottery_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    code = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    discount_rate = table.Column<double>(type: "double precision", nullable: true),
                    max_usage = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_of_splits = table.Column<int>(type: "integer", nullable: false),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    renewal_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_subscriptions_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "wallet_fund_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    is_received = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_fund_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_fund_recipients_wallet_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "wallet_funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    payer_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_transactions_wallets_payee_wallet_id",
                        column: x => x.payee_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_transactions_wallets_payer_wallet_id",
                        column: x => x.payer_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "wallet_pockets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_pockets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_pockets_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_wallet_gifts_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_gifts_wallet_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "wallet_subscriptions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    app_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    product_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_orders_payment_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_orders_wallets_payee_wallet_id",
                        column: x => x.payee_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_payee_wallet_id",
                table: "payment_orders",
                column: "payee_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_transaction_id",
                table: "payment_orders",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payee_wallet_id",
                table: "payment_transactions",
                column: "payee_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payer_wallet_id",
                table: "payment_transactions",
                column: "payer_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_fund_id",
                table: "wallet_fund_recipients",
                column: "fund_id");

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
                name: "ix_wallet_gifts_subscription_id",
                table: "wallet_gifts",
                column: "subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_pockets_wallet_id",
                table: "wallet_pockets",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id",
                table: "wallet_subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id_identifier",
                table: "wallet_subscriptions",
                columns: new[] { "account_id", "identifier" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id_is_active",
                table: "wallet_subscriptions",
                columns: new[] { "account_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_coupon_id",
                table: "wallet_subscriptions",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_identifier",
                table: "wallet_subscriptions",
                column: "identifier");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_status",
                table: "wallet_subscriptions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotteries");

            migrationBuilder.DropTable(
                name: "lottery_records");

            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "wallet_fund_recipients");

            migrationBuilder.DropTable(
                name: "wallet_gifts");

            migrationBuilder.DropTable(
                name: "wallet_pockets");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "wallet_funds");

            migrationBuilder.DropTable(
                name: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "wallets");

            migrationBuilder.DropTable(
                name: "wallet_coupons");
        }
    }
}
