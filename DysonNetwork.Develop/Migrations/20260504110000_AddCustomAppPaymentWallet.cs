using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    [DbContext(typeof(AppDatabase))]
    [Migration("20260504110000_AddCustomAppPaymentWallet")]
    public partial class AddCustomAppPaymentWallet : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payment_wallet_id",
                table: "custom_apps",
                type: "uuid",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_wallet_id",
                table: "custom_apps");
        }
    }
}
