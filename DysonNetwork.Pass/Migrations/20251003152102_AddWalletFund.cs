using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletFund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_funds", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_funds_accounts_creator_account_id",
                        column: x => x.creator_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_fund_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    is_received = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_fund_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_fund_recipients_accounts_recipient_account_id",
                        column: x => x.recipient_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_fund_recipients_wallet_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "wallet_funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_fund_id",
                table: "wallet_fund_recipients",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_recipient_account_id",
                table: "wallet_fund_recipients",
                column: "recipient_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_funds_creator_account_id",
                table: "wallet_funds",
                column: "creator_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_fund_recipients");

            migrationBuilder.DropTable(
                name: "wallet_funds");
        }
    }
}
