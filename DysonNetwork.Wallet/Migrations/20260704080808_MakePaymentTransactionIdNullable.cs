using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class MakePaymentTransactionIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_merchant_settlements_payment_transactions_payment_transacti",
                table: "merchant_settlements");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_transaction_id",
                table: "merchant_settlements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_merchant_settlements_payment_transactions_payment_transacti",
                table: "merchant_settlements",
                column: "payment_transaction_id",
                principalTable: "payment_transactions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_merchant_settlements_payment_transactions_payment_transacti",
                table: "merchant_settlements");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_transaction_id",
                table: "merchant_settlements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_merchant_settlements_payment_transactions_payment_transacti",
                table: "merchant_settlements",
                column: "payment_transaction_id",
                principalTable: "payment_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
