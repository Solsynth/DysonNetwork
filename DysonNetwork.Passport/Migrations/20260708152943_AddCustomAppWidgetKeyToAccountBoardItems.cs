using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomAppWidgetKeyToAccountBoardItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_app_widget_key",
                table: "account_board_items",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_board_items_account_id_custom_app_id_custom_app_wid",
                table: "account_board_items",
                columns: new[] { "account_id", "custom_app_id", "custom_app_widget_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_board_items_account_id_custom_app_id_custom_app_wid",
                table: "account_board_items");

            migrationBuilder.DropColumn(
                name: "custom_app_widget_key",
                table: "account_board_items");
        }
    }
}
