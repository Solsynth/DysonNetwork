using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "chat_group_id",
                table: "chat_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_groups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_members_chat_group_id",
                table: "chat_members",
                column: "chat_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_groups_account_id_name",
                table: "chat_groups",
                columns: new[] { "account_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "fk_chat_members_chat_groups_chat_group_id",
                table: "chat_members",
                column: "chat_group_id",
                principalTable: "chat_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_members_chat_groups_chat_group_id",
                table: "chat_members");

            migrationBuilder.DropTable(
                name: "chat_groups");

            migrationBuilder.DropIndex(
                name: "ix_chat_members_chat_group_id",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "chat_group_id",
                table: "chat_members");
        }
    }
}
