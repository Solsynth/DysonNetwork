using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    public partial class AddMultiWalletSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "wallets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "wallets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "wallets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "Wallet");

            migrationBuilder.AddColumn<string>(
                name: "public_id",
                table: "wallets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "wallets",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE wallets SET is_primary = true WHERE account_id IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_public_id",
                table: "wallets",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallets_realm_id",
                table: "wallets",
                column: "realm_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallets_public_id",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "ix_wallets_realm_id",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "name",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "public_id",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "wallets");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "wallets",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
