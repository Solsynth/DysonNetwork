using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");

            migrationBuilder.CreateTable(
                name: "meets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    location_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    location_address = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    location = table.Column<Geometry>(type: "geometry (Geometry,4326)", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meet_participants",
                columns: table => new
                {
                    meet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meet_participants", x => new { x.meet_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_meet_participants_meets_meet_id",
                        column: x => x.meet_id,
                        principalTable: "meets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meets_host_id_status",
                table: "meets",
                columns: new[] { "host_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meet_participants");

            migrationBuilder.DropTable(
                name: "meets");
        }
    }
}
