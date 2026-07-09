using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardWidgetTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "board_widgets",
                table: "custom_apps");

            migrationBuilder.CreateTable(
                name: "board_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    renderer_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    field_types = table.Column<List<SnBoardFieldType>>(type: "jsonb", nullable: false),
                    required_fields = table.Column<string>(type: "jsonb", nullable: false),
                    max_payload_bytes = table.Column<int>(type: "integer", nullable: true),
                    allow_multiple = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_board_widgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_board_widgets_custom_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "custom_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_board_widgets_app_id_key",
                table: "board_widgets",
                columns: new[] { "app_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_widgets");

            migrationBuilder.AddColumn<List<SnBoardWidgetManifest>>(
                name: "board_widgets",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);
        }
    }
}
