using System;
using System.Collections.Generic;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class SafetyAbuseReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<ContentSensitiveMark>>(
                name: "sensitive_marks",
                table: "posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "abuse_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    resolved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abuse_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_abuse_reports_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_account_id",
                table: "abuse_reports",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abuse_reports");

            migrationBuilder.DropColumn(
                name: "sensitive_marks",
                table: "posts");
        }
    }
}
