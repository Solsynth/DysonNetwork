using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class CleanUnusedFitness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workout_exercises_workouts_workout_id",
                table: "workout_exercises");

            migrationBuilder.DropTable(
                name: "exercise_library");

            migrationBuilder.DropPrimaryKey(
                name: "pk_workout_exercises",
                table: "workout_exercises");

            migrationBuilder.RenameTable(
                name: "workout_exercises",
                newName: "sn_workout_exercise");

            migrationBuilder.RenameIndex(
                name: "ix_workout_exercises_workout_id",
                table: "sn_workout_exercise",
                newName: "ix_sn_workout_exercise_workout_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_workout_exercise",
                table: "sn_workout_exercise",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sn_workout_exercise_workouts_workout_id",
                table: "sn_workout_exercise",
                column: "workout_id",
                principalTable: "workouts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sn_workout_exercise_workouts_workout_id",
                table: "sn_workout_exercise");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_workout_exercise",
                table: "sn_workout_exercise");

            migrationBuilder.RenameTable(
                name: "sn_workout_exercise",
                newName: "workout_exercises");

            migrationBuilder.RenameIndex(
                name: "ix_sn_workout_exercise_workout_id",
                table: "workout_exercises",
                newName: "ix_workout_exercises_workout_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_workout_exercises",
                table: "workout_exercises",
                column: "id");

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

            migrationBuilder.AddForeignKey(
                name: "fk_workout_exercises_workouts_workout_id",
                table: "workout_exercises",
                column: "workout_id",
                principalTable: "workouts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
