using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class FreeTokensForBotInitiated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "free_tokens",
                table: "thinking_sequences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "free_tokens",
                table: "thinking_sequences");
        }
    }
}
