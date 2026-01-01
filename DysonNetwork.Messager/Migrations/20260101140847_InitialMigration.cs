using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_rooms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nick = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notify = table.Column<int>(type: "integer", nullable: false),
                    last_read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    leave_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    invited_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    break_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    timeout_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    timeout_cause = table.Column<ChatTimeoutCause>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_members", x => x.id);
                    table.UniqueConstraint("ak_chat_members_chat_room_id_account_id", x => new { x.chat_room_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_chat_members_chat_members_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "chat_members",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_chat_members_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    members_mentioned = table.Column<string>(type: "jsonb", nullable: true),
                    nonce = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    attachments = table.Column<List<SnCloudFileReferenceObject>>(type: "jsonb", nullable: false),
                    replied_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    forwarded_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_messages_forwarded_message_id",
                        column: x => x.forwarded_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_messages_replied_message_id",
                        column: x => x.replied_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_realtime_call",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    upstream = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_realtime_call", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_realtime_call_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_realtime_call_chat_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_reactions_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_reactions_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_members_invited_by_id",
                table: "chat_members",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id",
                table: "chat_messages",
                column: "chat_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_forwarded_message_id",
                table: "chat_messages",
                column: "forwarded_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_replied_message_id",
                table: "chat_messages",
                column: "replied_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sender_id",
                table: "chat_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reactions_message_id",
                table: "chat_reactions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reactions_sender_id",
                table: "chat_reactions",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_realtime_call_room_id",
                table: "chat_realtime_call",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_realtime_call_sender_id",
                table: "chat_realtime_call",
                column: "sender_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_reactions");

            migrationBuilder.DropTable(
                name: "chat_realtime_call");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_members");

            migrationBuilder.DropTable(
                name: "chat_rooms");
        }
    }
}
