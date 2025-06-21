using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class WalletOrderAppDX : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_orders_wallets_payee_wallet_id",
                table: "payment_orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "payee_wallet_id",
                table: "payment_orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "app_identifier",
                table: "payment_orders",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "meta",
                table: "payment_orders",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_orders_wallets_payee_wallet_id",
                table: "payment_orders",
                column: "payee_wallet_id",
                principalTable: "wallets",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_orders_wallets_payee_wallet_id",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "app_identifier",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "meta",
                table: "payment_orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "payee_wallet_id",
                table: "payment_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_orders_wallets_payee_wallet_id",
                table: "payment_orders",
                column: "payee_wallet_id",
                principalTable: "wallets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
