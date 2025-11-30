using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class SimplerChatRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_bot",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "role",
                table: "chat_members");

            migrationBuilder.AddColumn<Guid>(
                name: "invited_by_id",
                table: "chat_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_members_invited_by_id",
                table: "chat_members",
                column: "invited_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_members_chat_members_invited_by_id",
                table: "chat_members",
                column: "invited_by_id",
                principalTable: "chat_members",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_members_chat_members_invited_by_id",
                table: "chat_members");

            migrationBuilder.DropIndex(
                name: "ix_chat_members_invited_by_id",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "invited_by_id",
                table: "chat_members");

            migrationBuilder.AddColumn<bool>(
                name: "is_bot",
                table: "chat_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "role",
                table: "chat_members",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
