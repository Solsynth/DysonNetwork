using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBotWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhooks",
                table: "bot_chat_configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore SnBotWebhook column - type has been removed.
            // To rollback, recreate the column manually:
            //   ALTER TABLE bot_chat_configs ADD webhooks jsonb NOT NULL DEFAULT '[]'::jsonb;
        }
    }
}
