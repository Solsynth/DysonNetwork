using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddThreadIdToChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "thread_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "thread_root_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_thread_root_id",
                table: "chat_messages",
                column: "thread_root_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_messages_chat_messages_thread_root_id",
                table: "chat_messages",
                column: "thread_root_id",
                principalTable: "chat_messages",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_messages_chat_messages_thread_root_id",
                table: "chat_messages");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_thread_root_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "thread_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "thread_root_id",
                table: "chat_messages");
        }
    }
}
