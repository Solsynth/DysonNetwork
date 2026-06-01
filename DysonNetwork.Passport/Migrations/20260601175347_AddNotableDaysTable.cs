using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddNotableDaysTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notable_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    local_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    localizable_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    start_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false),
                    region = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false),
                    recurrence_pattern = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_period = table.Column<bool>(type: "boolean", nullable: false),
                    holiday_days = table.Column<string>(type: "jsonb", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notable_days", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notable_days");
        }
    }
}
