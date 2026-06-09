using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoApproveGroupChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_approve_group_chat",
                table: "bot_chat_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_approve_group_chat",
                table: "bot_chat_configs");
        }
    }
}
