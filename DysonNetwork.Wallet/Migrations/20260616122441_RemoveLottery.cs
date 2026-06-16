using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLottery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotteries");

            migrationBuilder.DropTable(
                name: "lottery_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lotteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_status = table.Column<int>(type: "integer", nullable: false),
                    matched_region_one_numbers = table.Column<string>(type: "jsonb", nullable: true),
                    matched_region_two_number = table.Column<int>(type: "integer", nullable: true),
                    multiplier = table.Column<int>(type: "integer", nullable: false),
                    region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    region_two_number = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotteries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lottery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    total_prize_amount = table.Column<long>(type: "bigint", nullable: false),
                    total_prizes_awarded = table.Column<int>(type: "integer", nullable: false),
                    total_tickets = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    winning_region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    winning_region_two_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lottery_records", x => x.id);
                });
        }
    }
}
