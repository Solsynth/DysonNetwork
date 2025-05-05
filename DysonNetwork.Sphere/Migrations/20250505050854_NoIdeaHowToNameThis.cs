using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class NoIdeaHowToNameThis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_message_reaction_chat_members_sender_id",
                table: "message_reaction");

            migrationBuilder.DropForeignKey(
                name: "fk_message_reaction_chat_messages_message_id",
                table: "message_reaction");

            migrationBuilder.DropPrimaryKey(
                name: "pk_message_reaction",
                table: "message_reaction");

            migrationBuilder.RenameTable(
                name: "message_reaction",
                newName: "chat_reactions");

            migrationBuilder.RenameIndex(
                name: "ix_message_reaction_sender_id",
                table: "chat_reactions",
                newName: "ix_chat_reactions_sender_id");

            migrationBuilder.RenameIndex(
                name: "ix_message_reaction_message_id",
                table: "chat_reactions",
                newName: "ix_chat_reactions_message_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_reactions",
                table: "chat_reactions",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_reactions_chat_members_sender_id",
                table: "chat_reactions",
                column: "sender_id",
                principalTable: "chat_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_chat_reactions_chat_messages_message_id",
                table: "chat_reactions",
                column: "message_id",
                principalTable: "chat_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_reactions_chat_members_sender_id",
                table: "chat_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_chat_reactions_chat_messages_message_id",
                table: "chat_reactions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_reactions",
                table: "chat_reactions");

            migrationBuilder.RenameTable(
                name: "chat_reactions",
                newName: "message_reaction");

            migrationBuilder.RenameIndex(
                name: "ix_chat_reactions_sender_id",
                table: "message_reaction",
                newName: "ix_message_reaction_sender_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_reactions_message_id",
                table: "message_reaction",
                newName: "ix_message_reaction_message_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_message_reaction",
                table: "message_reaction",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_reaction_chat_members_sender_id",
                table: "message_reaction",
                column: "sender_id",
                principalTable: "chat_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_message_reaction_chat_messages_message_id",
                table: "message_reaction",
                column: "message_id",
                principalTable: "chat_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
