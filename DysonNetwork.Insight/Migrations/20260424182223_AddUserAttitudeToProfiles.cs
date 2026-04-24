using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAttitudeToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "last_attitude_update_at",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_attitude_summary",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_attitude_trend",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "user_engagement",
                table: "user_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "user_respect",
                table: "user_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "user_warmth",
                table: "user_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_attitude_update_at",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "user_attitude_summary",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "user_attitude_trend",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "user_engagement",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "user_respect",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "user_warmth",
                table: "user_profiles");
        }
    }
}
