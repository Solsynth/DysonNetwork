using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "wallet_subscriptions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "group_identifier",
                table: "wallet_subscriptions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "perk_level",
                table: "wallet_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "wallet_subscription_catalog_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gift_policy_defaults = table.Column<SubscriptionGiftPolicy>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscription_catalog_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_subscription_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    group_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    display_name = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    perk_level = table.Column<int>(type: "integer", nullable: false),
                    minimum_account_level = table.Column<int>(type: "integer", nullable: true),
                    experience_multiplier = table.Column<decimal>(type: "numeric", nullable: true),
                    golden_point_reward = table.Column<int>(type: "integer", nullable: true),
                    payment_policy = table.Column<SubscriptionPaymentPolicy>(type: "jsonb", nullable: false),
                    gift_policy = table.Column<SubscriptionGiftPolicy>(type: "jsonb", nullable: true),
                    provider_mappings = table.Column<Dictionary<string, List<string>>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscription_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscription_definitions_identifier",
                table: "wallet_subscription_definitions",
                column: "identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_subscription_catalog_settings");

            migrationBuilder.DropTable(
                name: "wallet_subscription_definitions");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "wallet_subscriptions");

            migrationBuilder.DropColumn(
                name: "group_identifier",
                table: "wallet_subscriptions");

            migrationBuilder.DropColumn(
                name: "perk_level",
                table: "wallet_subscriptions");
        }
    }
}
