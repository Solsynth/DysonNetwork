using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTicketSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_files");

            migrationBuilder.DropColumn(
                name: "description",
                table: "tickets");

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "metadata",
                table: "tickets",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<List<SnCloudFileReferenceObject>>(
                name: "files",
                table: "ticket_messages",
                type: "jsonb",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metadata",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "files",
                table: "ticket_messages");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "tickets",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    file_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_files_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_files_ticket_id",
                table: "ticket_files",
                column: "ticket_id");
        }
    }
}
