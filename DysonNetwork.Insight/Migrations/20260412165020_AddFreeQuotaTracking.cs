using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeQuotaTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "daily_free_tokens_used",
                table: "thinking_sequences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Instant>(
                name: "last_free_quota_reset_at",
                table: "thinking_sequences",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "daily_free_tokens_used",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "last_free_quota_reset_at",
                table: "thinking_sequences");
        }
    }
}
