using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailLotteriesStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "matched_region_one_numbers",
                table: "lotteries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "matched_region_two_number",
                table: "lotteries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "matched_region_one_numbers",
                table: "lotteries");

            migrationBuilder.DropColumn(
                name: "matched_region_two_number",
                table: "lotteries");
        }
    }
}
