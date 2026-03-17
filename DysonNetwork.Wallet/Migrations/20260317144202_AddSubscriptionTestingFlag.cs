using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTestingFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_testing",
                table: "wallet_subscription_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_testing",
                table: "wallet_subscription_definitions");
        }
    }
}
