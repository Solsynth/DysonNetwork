using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallet_subscription_definitions_identifier",
                table: "wallet_subscription_definitions");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscription_definitions_identifier_deleted_at",
                table: "wallet_subscription_definitions",
                columns: new[] { "identifier", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallet_subscription_definitions_identifier_deleted_at",
                table: "wallet_subscription_definitions");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscription_definitions_identifier",
                table: "wallet_subscription_definitions",
                column: "identifier",
                unique: true);
        }
    }
}
