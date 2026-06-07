using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceholderUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id_sender_id",
                table: "chat_messages",
                columns: new[] { "chat_room_id", "sender_id" },
                unique: true,
                filter: "type = 'placeholder' AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_messages_chat_room_id_sender_id",
                table: "chat_messages");
        }
    }
}
