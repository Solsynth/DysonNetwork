using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddPayerWalletToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payer_wallet_id",
                table: "payment_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_payer_wallet_id",
                table: "payment_orders",
                column: "payer_wallet_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_orders_wallets_payer_wallet_id",
                table: "payment_orders",
                column: "payer_wallet_id",
                principalTable: "wallets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_orders_wallets_payer_wallet_id",
                table: "payment_orders");

            migrationBuilder.DropIndex(
                name: "ix_payment_orders_payer_wallet_id",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "payer_wallet_id",
                table: "payment_orders");
        }
    }
}
