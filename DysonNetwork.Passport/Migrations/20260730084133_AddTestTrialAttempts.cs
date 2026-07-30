using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddTestTrialAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_trial",
                table: "test_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_trial",
                table: "test_attempts");
        }
    }
}
