using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessagePins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_message_pins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pinned_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_message_pins", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_message_pins_chat_members_pinned_by_member_id",
                        column: x => x.pinned_by_member_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_message_pins_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_message_pins_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_pins_chat_room_id_expires_at",
                table: "chat_message_pins",
                columns: new[] { "chat_room_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_pins_chat_room_id_message_id",
                table: "chat_message_pins",
                columns: new[] { "chat_room_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_pins_message_id",
                table: "chat_message_pins",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_pins_pinned_by_member_id",
                table: "chat_message_pins",
                column: "pinned_by_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_message_pins");
        }
    }
}
