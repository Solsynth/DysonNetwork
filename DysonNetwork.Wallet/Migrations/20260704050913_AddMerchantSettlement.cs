using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    award_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    settled_by = table.Column<int>(type: "integer", nullable: true),
                    settled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    settlement_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchant_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_merchant_settlements_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_merchant_settlements_payment_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "payment_orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_merchant_settlements_payment_transactions_payment_transacti",
                        column: x => x.payment_transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_merchant_settlements_payment_transactions_settlement_transa",
                        column: x => x.settlement_transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_merchant_settlements_merchant_id",
                table: "merchant_settlements",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchant_settlements_order_id",
                table: "merchant_settlements",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchant_settlements_payment_transaction_id",
                table: "merchant_settlements",
                column: "payment_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchant_settlements_settlement_transaction_id",
                table: "merchant_settlements",
                column: "settlement_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_settlements");

            migrationBuilder.DropTable(
                name: "merchants");
        }
    }
}
