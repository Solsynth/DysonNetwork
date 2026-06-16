using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_transfer_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remark = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    freeze = table.Column<bool>(type: "boolean", nullable: false),
                    require_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    fulfilled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_transfer_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_transfer_requests_payment_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_transfer_requests_wallets_payee_wallet_id",
                        column: x => x.payee_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transfer_requests_payee_wallet_id",
                table: "wallet_transfer_requests",
                column: "payee_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transfer_requests_transaction_id",
                table: "wallet_transfer_requests",
                column: "transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_transfer_requests");
        }
    }
}
