using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RefactorChatLastRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_read_receipts");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "chat_messages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Instant>(
                name: "last_read_at",
                table: "chat_members",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_read_at",
                table: "chat_members");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "chat_messages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.CreateTable(
                name: "chat_read_receipts",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_read_receipts", x => new { x.message_id, x.sender_id });
                    table.ForeignKey(
                        name: "fk_chat_read_receipts_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_read_receipts_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_read_receipts_message_id_sender_id",
                table: "chat_read_receipts",
                columns: new[] { "message_id", "sender_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_read_receipts_sender_id",
                table: "chat_read_receipts",
                column: "sender_id");
        }
    }
}
