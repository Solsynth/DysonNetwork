using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class CleanUnusedFitnessTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_library");

            migrationBuilder.DropTable(
                name: "workout_exercises");

            migrationBuilder.AddColumn<int>(
                name: "average_heart_rate",
                table: "workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "average_speed",
                table: "workouts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "distance",
                table: "workouts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "distance_unit",
                table: "workouts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "elevation_gain",
                table: "workouts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_heart_rate",
                table: "workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_speed",
                table: "workouts",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "average_heart_rate",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "average_speed",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "distance",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "distance_unit",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "elevation_gain",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "max_heart_rate",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "max_speed",
                table: "workouts");

            migrationBuilder.CreateTable(
                name: "exercise_library",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    difficulty = table.Column<int>(type: "integer", nullable: false),
                    equipment = table.Column<string>(type: "jsonb", nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    muscle_groups = table.Column<string>(type: "jsonb", nullable: true),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_library", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workout_exercises",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    duration = table.Column<Duration>(type: "interval", nullable: true),
                    exercise_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    notes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: true),
                    sets = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workout_exercises", x => x.id);
                    table.ForeignKey(
                        name: "fk_workout_exercises_workouts_workout_id",
                        column: x => x.workout_id,
                        principalTable: "workouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workout_exercises_workout_id",
                table: "workout_exercises",
                column: "workout_id");
        }
    }
}
