using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMemberUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "chat_members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "username",
                table: "chat_members");
        }
    }
}
