using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Fitness.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "workouts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "fitness_metrics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_id",
                table: "workouts");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "fitness_metrics");
        }
    }
}
