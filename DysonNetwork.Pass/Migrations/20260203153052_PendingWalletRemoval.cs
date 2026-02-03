using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class PendingWalletRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lotteries_accounts_account_id",
                table: "lotteries");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_fund_recipients_accounts_recipient_account_id",
                table: "wallet_fund_recipients");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_funds_accounts_creator_account_id",
                table: "wallet_funds");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_gifts_accounts_gifter_id",
                table: "wallet_gifts");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_gifts_accounts_recipient_id",
                table: "wallet_gifts");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_gifts_accounts_redeemer_id",
                table: "wallet_gifts");

            migrationBuilder.DropForeignKey(
                name: "fk_wallet_subscriptions_accounts_account_id",
                table: "wallet_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_wallets_accounts_account_id",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "ix_wallets_account_id",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "ix_wallet_gifts_redeemer_id",
                table: "wallet_gifts");

            migrationBuilder.DropIndex(
                name: "ix_wallet_funds_creator_account_id",
                table: "wallet_funds");

            migrationBuilder.DropIndex(
                name: "ix_wallet_fund_recipients_recipient_account_id",
                table: "wallet_fund_recipients");

            migrationBuilder.DropIndex(
                name: "ix_lotteries_account_id",
                table: "lotteries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_wallets_account_id",
                table: "wallets",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_redeemer_id",
                table: "wallet_gifts",
                column: "redeemer_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_funds_creator_account_id",
                table: "wallet_funds",
                column: "creator_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_recipient_account_id",
                table: "wallet_fund_recipients",
                column: "recipient_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_lotteries_account_id",
                table: "lotteries",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lotteries_accounts_account_id",
                table: "lotteries",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_fund_recipients_accounts_recipient_account_id",
                table: "wallet_fund_recipients",
                column: "recipient_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_funds_accounts_creator_account_id",
                table: "wallet_funds",
                column: "creator_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_gifts_accounts_gifter_id",
                table: "wallet_gifts",
                column: "gifter_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_gifts_accounts_recipient_id",
                table: "wallet_gifts",
                column: "recipient_id",
                principalTable: "accounts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_gifts_accounts_redeemer_id",
                table: "wallet_gifts",
                column: "redeemer_id",
                principalTable: "accounts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_wallet_subscriptions_accounts_account_id",
                table: "wallet_subscriptions",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wallets_accounts_account_id",
                table: "wallets",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
