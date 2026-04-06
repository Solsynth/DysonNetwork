using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class AddRepeatableGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_repetition",
                table: "fitness_goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_goal_id",
                table: "fitness_goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "repeat_count",
                table: "fitness_goals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "repeat_interval",
                table: "fitness_goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "repeat_type",
                table: "fitness_goals",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_repetition",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "parent_goal_id",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "repeat_count",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "repeat_interval",
                table: "fitness_goals");

            migrationBuilder.DropColumn(
                name: "repeat_type",
                table: "fitness_goals");
        }
    }
}
