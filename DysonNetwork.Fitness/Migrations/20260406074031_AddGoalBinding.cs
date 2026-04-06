using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_update_progress",
                table: "fitness_goals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "bound_metric_type",
                table: "fitness_goals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bound_workout_type",
                table: "fitness_goals",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_update_progress",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "bound_metric_type",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "bound_workout_type",
                table: "fitness_goals");
        }
    }
}
