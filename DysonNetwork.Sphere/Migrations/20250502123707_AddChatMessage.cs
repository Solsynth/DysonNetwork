using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_members",
                table: "chat_members");

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "chat_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "ak_chat_members_chat_room_id_account_id",
                table: "chat_members",
                columns: new[] { "chat_room_id", "account_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_members",
                table: "chat_members",
                column: "id");

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    members_metioned = table.Column<List<Guid>>(type: "jsonb", nullable: true),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    replied_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    forwarded_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<long>(type: "bigint", nullable: false),
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
                name: "chat_statuses",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_statuses", x => new { x.message_id, x.sender_id });
                    table.ForeignKey(
                        name: "fk_chat_statuses_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_statuses_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_reaction",
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
                    table.PrimaryKey("pk_message_reaction", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_reaction_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_reaction_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_message_id",
                table: "files",
                column: "message_id");

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
                name: "ix_chat_statuses_sender_id",
                table: "chat_statuses",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_reaction_message_id",
                table: "message_reaction",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_reaction_sender_id",
                table: "message_reaction",
                column: "sender_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_chat_messages_message_id",
                table: "files",
                column: "message_id",
                principalTable: "chat_messages",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_chat_messages_message_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "chat_statuses");

            migrationBuilder.DropTable(
                name: "message_reaction");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropIndex(
                name: "ix_files_message_id",
                table: "files");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_chat_members_chat_room_id_account_id",
                table: "chat_members");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_members",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "id",
                table: "chat_members");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_members",
                table: "chat_members",
                columns: new[] { "chat_room_id", "account_id" });
        }
    }
}
