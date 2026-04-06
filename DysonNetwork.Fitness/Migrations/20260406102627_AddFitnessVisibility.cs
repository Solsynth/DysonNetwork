using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class AddFitnessVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "workouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "fitness_metrics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "fitness_goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "visibility",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "fitness_metrics");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "fitness_goals");
        }
    }
}
