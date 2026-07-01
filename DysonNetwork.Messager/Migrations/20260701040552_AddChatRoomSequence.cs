using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRoomSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "room_sequence",
                table: "chat_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "chat_room_counters",
                columns: table => new
                {
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_room_counters", x => x.chat_room_id);
                });

            migrationBuilder.Sql(
                """
                WITH sequenced AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (PARTITION BY chat_room_id ORDER BY created_at, id) AS room_sequence
                    FROM chat_messages
                )
                UPDATE chat_messages AS message
                SET room_sequence = sequenced.room_sequence
                FROM sequenced
                WHERE message.id = sequenced.id;

                INSERT INTO chat_room_counters (chat_room_id, last_sequence)
                SELECT chat_room_id, MAX(room_sequence)
                FROM chat_messages
                GROUP BY chat_room_id;

                ALTER TABLE chat_messages
                ALTER COLUMN room_sequence DROP DEFAULT;
                """
            );

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id_room_sequence",
                table: "chat_messages",
                columns: new[] { "chat_room_id", "room_sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_room_counters");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_chat_room_id_room_sequence",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "room_sequence",
                table: "chat_messages");
        }
    }
}
