using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatE2eeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_messages_chat_room_id",
                table: "chat_messages");

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "e2ee_policy",
                table: "chat_rooms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "encryption_mode",
                table: "chat_rooms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "ciphertext",
                table: "chat_messages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_message_id",
                table: "chat_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "encryption_epoch",
                table: "chat_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "encryption_header",
                table: "chat_messages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "encryption_message_type",
                table: "chat_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "encryption_scheme",
                table: "chat_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "encryption_signature",
                table: "chat_messages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_encrypted",
                table: "chat_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id_is_encrypted_created_at",
                table: "chat_messages",
                columns: new[] { "chat_room_id", "is_encrypted", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id_sender_id_client_message_id",
                table: "chat_messages",
                columns: new[] { "chat_room_id", "sender_id", "client_message_id" },
                filter: "client_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_messages_chat_room_id_is_encrypted_created_at",
                table: "chat_messages");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_chat_room_id_sender_id_client_message_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "e2ee_policy",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "encryption_mode",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "ciphertext",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "client_message_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "encryption_epoch",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "encryption_header",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "encryption_message_type",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "encryption_scheme",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "encryption_signature",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "is_encrypted",
                table: "chat_messages");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id",
                table: "chat_messages",
                column: "chat_room_id");
        }
    }
}
