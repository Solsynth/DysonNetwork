using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Messager.Migrations;

[Migration("20260814000000_AddChatMemberConfirmedAt")]
public partial class AddChatMemberConfirmedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Instant?>(
            name: "confirmed_at",
            table: "chat_members",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "confirmed_at",
            table: "chat_members");
    }
}
