using System;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSubscriptionCatalogSettingsAddDisplayConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_subscription_catalog_settings");

            migrationBuilder.AddColumn<SubscriptionDisplayConfig>(
                name: "display_config",
                table: "wallet_subscription_definitions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_config",
                table: "wallet_subscription_definitions");

            migrationBuilder.CreateTable(
                name: "wallet_subscription_catalog_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    gift_policy_defaults = table.Column<SubscriptionGiftPolicy>(type: "jsonb", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscription_catalog_settings", x => x.id);
                });
        }
    }
}
