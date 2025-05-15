using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardToCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reward_experience",
                table: "account_check_in_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reward_points",
                table: "account_check_in_results",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reward_experience",
                table: "account_check_in_results");

            migrationBuilder.DropColumn(
                name: "reward_points",
                table: "account_check_in_results");
        }
    }
}
