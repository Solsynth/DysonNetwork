using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class ContinuedCleanUnusedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abuse_reports");

            migrationBuilder.DropTable(
                name: "punishments");

            migrationBuilder.AlterColumn<Dictionary<string, object>>(
                name: "meta",
                table: "sn_account_connection",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(Dictionary<string, object>),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Dictionary<string, object>>(
                name: "meta",
                table: "sn_account_connection",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(Dictionary<string, object>),
                oldType: "jsonb");

            migrationBuilder.CreateTable(
                name: "abuse_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    resolution = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    resolved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    resource_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abuse_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_abuse_reports_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "punishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_permissions = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punishments", x => x.id);
                    table.ForeignKey(
                        name: "fk_punishments_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_account_id",
                table: "abuse_reports",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_punishments_account_id",
                table: "punishments",
                column: "account_id");
        }
    }
}
