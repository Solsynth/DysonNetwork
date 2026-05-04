using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallets_public_id",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "ix_wallets_realm_id",
                table: "wallets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
