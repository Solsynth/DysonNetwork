using DysonNetwork.Sphere.Chat;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class EnrichChatMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "break_until",
                table: "chat_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<ChatTimeoutCause>(
                name: "timeout_cause",
                table: "chat_members",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "timeout_until",
                table: "chat_members",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "break_until",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "timeout_cause",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "timeout_until",
                table: "chat_members");
        }
    }
}
