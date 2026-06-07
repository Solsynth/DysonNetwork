using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class EnrichWalletTranasction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "held_amount",
                table: "wallet_pockets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "contribution_amount",
                table: "wallet_funds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "contribution_type",
                table: "wallet_funds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "deadline_at",
                table: "wallet_funds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_raising",
                table: "wallet_funds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "target_amount",
                table: "wallet_funds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Instant>(
                name: "confirmed_at",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "expires_at",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "frozen_at",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_frozen",
                table: "payment_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "require_confirmation",
                table: "payment_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "payment_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "held_amount",
                table: "wallet_pockets");

            migrationBuilder.DropColumn(
                name: "contribution_amount",
                table: "wallet_funds");

            migrationBuilder.DropColumn(
                name: "contribution_type",
                table: "wallet_funds");

            migrationBuilder.DropColumn(
                name: "deadline_at",
                table: "wallet_funds");

            migrationBuilder.DropColumn(
                name: "is_raising",
                table: "wallet_funds");

            migrationBuilder.DropColumn(
                name: "target_amount",
                table: "wallet_funds");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "frozen_at",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "is_frozen",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "require_confirmation",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "payment_transactions");
        }
    }
}
