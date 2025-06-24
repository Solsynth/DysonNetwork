using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class DropActionLogSessionFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_action_logs_auth_sessions_session_id",
                table: "action_logs");

            migrationBuilder.DropIndex(
                name: "ix_action_logs_session_id",
                table: "action_logs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_action_logs_session_id",
                table: "action_logs",
                column: "session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_action_logs_auth_sessions_session_id",
                table: "action_logs",
                column: "session_id",
                principalTable: "auth_sessions",
                principalColumn: "id");
        }
    }
}
