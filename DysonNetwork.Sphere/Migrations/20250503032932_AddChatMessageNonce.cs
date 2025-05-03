using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessageNonce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "members_metioned",
                table: "chat_messages",
                newName: "members_mentioned");

            migrationBuilder.AddColumn<string>(
                name: "nonce",
                table: "chat_messages",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nonce",
                table: "chat_messages");

            migrationBuilder.RenameColumn(
                name: "members_mentioned",
                table: "chat_messages",
                newName: "members_metioned");
        }
    }
}
