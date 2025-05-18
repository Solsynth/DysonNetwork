using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeDataStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_statuses");

            migrationBuilder.CreateTable(
                name: "chat_read_receipts",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_read_receipts", x => new { x.message_id, x.sender_id });
                    table.ForeignKey(
                        name: "fk_chat_read_receipts_chat_members_sender_id",
                        column: x => x.sender_id,
                        principalTable: "chat_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_read_receipts_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_read_receipts_message_id_sender_id",
                table: "chat_read_receipts",
                columns: new[] { "message_id", "sender_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_read_receipts_sender_id",
                table: "chat_read_receipts",
                column: "sender_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_read_receipts");

            migrationBuilder.CreateTable(
                name: "chat_statuses",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_chat_statuses_message_id_sender_id",
                table: "chat_statuses",
                columns: new[] { "message_id", "sender_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_statuses_sender_id",
                table: "chat_statuses",
                column: "sender_id");
        }
    }
}
