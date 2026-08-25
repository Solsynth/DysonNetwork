using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations;

public partial class AddChatRoomReadReceiptsSetting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_read_receipts_public",
            table: "chat_rooms",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "read_receipts_enabled",
            table: "chat_rooms");
    }
}
