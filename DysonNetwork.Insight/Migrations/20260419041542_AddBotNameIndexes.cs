using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddBotNameIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_account_id_bot_name",
                table: "user_profiles",
                columns: new[] { "account_id", "bot_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_thinking_sequences_bot_name",
                table: "thinking_sequences",
                column: "bot_name");

            migrationBuilder.CreateIndex(
                name: "ix_memory_records_bot_name",
                table: "memory_records",
                column: "bot_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_profiles_account_id_bot_name",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "ix_thinking_sequences_bot_name",
                table: "thinking_sequences");

            migrationBuilder.DropIndex(
                name: "ix_memory_records_bot_name",
                table: "memory_records");
        }
    }
}
