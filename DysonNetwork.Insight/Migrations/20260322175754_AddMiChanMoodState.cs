using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddMiChanMoodState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mood_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    energy_level = table.Column<float>(type: "real", nullable: false),
                    positivity_level = table.Column<float>(type: "real", nullable: false),
                    sociability_level = table.Column<float>(type: "real", nullable: false),
                    curiosity_level = table.Column<float>(type: "real", nullable: false),
                    current_mood_description = table.Column<string>(type: "text", nullable: false),
                    recent_emotional_events = table.Column<string>(type: "text", nullable: true),
                    last_mood_update = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    interactions_since_update = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mood_states", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mood_states");
        }
    }
}
