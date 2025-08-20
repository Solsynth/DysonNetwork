using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelingBonusMultiplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bonus_multiplier",
                table: "experience_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bonus_multiplier",
                table: "experience_records");
        }
    }
}
