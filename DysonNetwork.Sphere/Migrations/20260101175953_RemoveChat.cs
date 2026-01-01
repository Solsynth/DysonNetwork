using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_reactions");

            migrationBuilder.DropTable(
                name: "chat_realtime_call");

            migrationBuilder.DropTable(
                name: "web_articles");

            migrationBuilder.DropTable(
                name: "web_feed_subscriptions");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "web_feeds");

            migrationBuilder.DropTable(
                name: "chat_members");

            migrationBuilder.DropTable(
                name: "chat_rooms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_rooms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "web_feeds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config = table.Column<WebFeedConfig>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_feeds", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_feeds_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    break_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    leave_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    nick = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notify = table.Column<int>(type: "integer", nullable: false),
                    timeout_cause = table.Column<ChatTimeoutCause>(type: "jsonb", nullable: true),
                    timeout_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                name: "web_articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_articles_web_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "web_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_feed_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_feed_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_feed_subscriptions_web_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "web_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forwarded_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replied_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachments = table.Column<List<SnCloudFileReferenceObject>>(type: "jsonb", nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    members_mentioned = table.Column<string>(type: "jsonb", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    nonce = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    provider_name = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    upstream = table.Column<string>(type: "jsonb", nullable: true)
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
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    symbol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_web_articles_feed_id",
                table: "web_articles",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_articles_url",
                table: "web_articles",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_web_feed_subscriptions_feed_id",
                table: "web_feed_subscriptions",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_feeds_publisher_id",
                table: "web_feeds",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_feeds_url",
                table: "web_feeds",
                column: "url",
                unique: true);
        }
    }
}
