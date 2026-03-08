using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizedAppsAndApiKeyAppScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "app_id",
                table: "api_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "authorized_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    app_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_authorized_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorized_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_authorized_apps_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authorized_apps_account_id_app_id_type",
                table: "authorized_apps",
                columns: new[] { "account_id", "app_id", "type" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorized_apps");

            migrationBuilder.DropColumn(
                name: "app_id",
                table: "api_keys");
        }
    }
}
