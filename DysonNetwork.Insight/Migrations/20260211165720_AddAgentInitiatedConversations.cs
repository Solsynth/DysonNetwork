using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentInitiatedConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "agent_initiated",
                table: "thinking_sequences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "last_message_at",
                table: "thinking_sequences",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

            migrationBuilder.AddColumn<Instant>(
                name: "user_last_read_at",
                table: "thinking_sequences",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_initiated",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "last_message_at",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "user_last_read_at",
                table: "thinking_sequences");
        }
    }
}
