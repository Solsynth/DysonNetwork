using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChatRoomIdFromMlsGroupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mls_group_states_chat_room_id",
                table: "mls_group_states");

            migrationBuilder.DropIndex(
                name: "ix_mls_device_memberships_chat_room_id_account_id_device_id",
                table: "mls_device_memberships");

            migrationBuilder.DropColumn(
                name: "chat_room_id",
                table: "mls_group_states");

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "account_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id",
                table: "mls_device_memberships");

            migrationBuilder.AddColumn<Guid>(
                name: "chat_room_id",
                table: "mls_group_states",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_chat_room_id",
                table: "mls_group_states",
                column: "chat_room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_chat_room_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "chat_room_id", "account_id", "device_id" },
                unique: true);
        }
    }
}
