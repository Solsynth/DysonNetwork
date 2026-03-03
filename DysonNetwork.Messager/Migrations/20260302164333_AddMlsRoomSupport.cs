using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddMlsRoomSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mls_group_id",
                table: "chat_rooms",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE chat_rooms SET encryption_mode = 3 WHERE encryption_mode IN (1, 2);");
            migrationBuilder.Sql(
                "UPDATE chat_rooms SET mls_group_id = CONCAT('chat:', id::text) WHERE encryption_mode = 3 AND mls_group_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mls_group_id",
                table: "chat_rooms");
        }
    }
}
