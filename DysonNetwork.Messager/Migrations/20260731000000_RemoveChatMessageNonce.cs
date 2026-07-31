using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations;

public partial class RemoveChatMessageNonce : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE chat_messages
            SET client_message_id = nonce
            WHERE client_message_id IS NULL
              AND nonce IS NOT NULL;
            """);

        migrationBuilder.DropColumn(
            name: "nonce",
            table: "chat_messages");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "nonce",
            table: "chat_messages",
            type: "character varying(36)",
            maxLength: 36,
            nullable: false,
            defaultValue: "");
    }
}
