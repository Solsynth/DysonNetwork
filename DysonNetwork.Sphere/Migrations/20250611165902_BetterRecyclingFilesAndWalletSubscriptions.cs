using System;
using DysonNetwork.Sphere.Wallet;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class BetterRecyclingFilesAndWalletSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_marked_recycle",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "account_profiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<SubscriptionReferenceObject>(
                name: "stellar_membership",
                table: "account_profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                table: "account_profiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

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
                    payment_details = table.Column<PaymentDetails>(type: "jsonb", nullable: false),
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
                        name: "fk_wallet_subscriptions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_subscriptions_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id",
                table: "wallet_subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_coupon_id",
                table: "wallet_subscriptions",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_identifier",
                table: "wallet_subscriptions",
                column: "identifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "wallet_coupons");

            migrationBuilder.DropColumn(
                name: "is_marked_recycle",
                table: "files");

            migrationBuilder.DropColumn(
                name: "location",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "stellar_membership",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "time_zone",
                table: "account_profiles");
        }
    }
}
