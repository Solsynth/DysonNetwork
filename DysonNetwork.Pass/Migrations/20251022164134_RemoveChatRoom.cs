using System;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChatRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sn_chat_member");

            migrationBuilder.DropTable(
                name: "sn_chat_room");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sn_chat_room",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    picture_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sn_realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_chat_room", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_chat_room_realms_sn_realm_id",
                        column: x => x.sn_realm_id,
                        principalTable: "realms",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sn_chat_member",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    break_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_bot = table.Column<bool>(type: "boolean", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    leave_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    nick = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notify = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    timeout_cause = table.Column<ChatTimeoutCause>(type: "jsonb", nullable: true),
                    timeout_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_chat_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_chat_member_sn_chat_room_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "sn_chat_room",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sn_chat_member_chat_room_id",
                table: "sn_chat_member",
                column: "chat_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_chat_room_sn_realm_id",
                table: "sn_chat_room",
                column: "sn_realm_id");
        }
    }
}
