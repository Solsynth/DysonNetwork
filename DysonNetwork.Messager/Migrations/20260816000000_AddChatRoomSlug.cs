using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations;

public partial class AddChatRoomSlug : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "slug",
            table: "chat_rooms",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_chat_rooms_realm_id_slug",
            table: "chat_rooms",
            columns: new[] { "realm_id", "slug" },
            unique: true,
            filter: "realm_id IS NOT NULL AND slug IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_chat_rooms_account_id_slug",
            table: "chat_rooms",
            columns: new[] { "account_id", "slug" },
            unique: true,
            filter: "realm_id IS NULL AND slug IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_chat_rooms_account_id_slug",
            table: "chat_rooms");

        migrationBuilder.DropIndex(
            name: "ix_chat_rooms_realm_id_slug",
            table: "chat_rooms");

        migrationBuilder.DropColumn(
            name: "slug",
            table: "chat_rooms");
    }
}
