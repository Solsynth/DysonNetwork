using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexForAuthClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_auth_clients_account_id",
                table: "auth_clients");

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id_device_id",
                table: "auth_clients",
                columns: new[] { "account_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_auth_clients_account_id_device_id",
                table: "auth_clients");

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id",
                table: "auth_clients",
                column: "account_id");
        }
    }
}
