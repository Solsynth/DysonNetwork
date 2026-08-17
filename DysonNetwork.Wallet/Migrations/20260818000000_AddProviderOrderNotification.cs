using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations;

public partial class AddProviderOrderNotification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Instant?>(
            name: "notification_sent_at",
            table: "inbound_orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "notification_sent_at",
            table: "inbound_orders");
    }
}
