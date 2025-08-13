using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizeDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "auth_challenges",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "auth_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    device_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_clients", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_clients_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_challenges_client_id",
                table: "auth_challenges",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id",
                table: "auth_clients",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_device_id",
                table: "auth_clients",
                column: "device_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_auth_challenges_auth_clients_client_id",
                table: "auth_challenges",
                column: "client_id",
                principalTable: "auth_clients",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auth_challenges_auth_clients_client_id",
                table: "auth_challenges");

            migrationBuilder.DropTable(
                name: "auth_clients");

            migrationBuilder.DropIndex(
                name: "ix_auth_challenges_client_id",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "auth_challenges");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "auth_challenges",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
