using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddBotChatConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_chat_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    commands = table.Column<List<SnBotCommand>>(type: "jsonb", nullable: false),
                    webhooks = table.Column<List<SnBotWebhook>>(type: "jsonb", nullable: false),
                    auto_approve_dm = table.Column<bool>(type: "boolean", nullable: false),
                    support_chat = table.Column<bool>(type: "boolean", nullable: false),
                    subscribed_events = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_chat_configs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_chat_configs");
        }
    }
}
