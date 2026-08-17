using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "inbound_order_id",
                table: "wallet_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inbound_order_id",
                table: "payment_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbound_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    external_id = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    provider_reference_id = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    product_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    account_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    duration = table.Column<Duration>(type: "interval", nullable: false),
                    is_testing = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_inbound_order_id",
                table: "wallet_subscriptions",
                column: "inbound_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_inbound_order_id",
                table: "payment_orders",
                column: "inbound_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_provider_external_id",
                table: "inbound_orders",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_orders_inbound_orders_inbound_order_id",
                table: "payment_orders",
                column: "inbound_order_id",
                principalTable: "inbound_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_subscriptions_inbound_orders_inbound_order_id",
                table: "wallet_subscriptions",
                column: "inbound_order_id",
                principalTable: "inbound_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_orders_inbound_orders_inbound_order_id",
                table: "payment_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_subscriptions_inbound_orders_inbound_order_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "inbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_wallet_subscriptions_inbound_order_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_payment_orders_inbound_order_id",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "inbound_order_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropColumn(
                name: "inbound_order_id",
                table: "payment_orders");
        }
    }
}
