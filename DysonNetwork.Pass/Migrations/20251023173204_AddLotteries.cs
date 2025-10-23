using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddLotteries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lotteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_one_numbers = table.Column<List<int>>(type: "jsonb", nullable: false),
                    region_two_number = table.Column<int>(type: "integer", nullable: false),
                    multiplier = table.Column<int>(type: "integer", nullable: false),
                    draw_status = table.Column<int>(type: "integer", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotteries", x => x.id);
                    table.ForeignKey(
                        name: "fk_lotteries_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lottery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    winning_region_one_numbers = table.Column<List<int>>(type: "jsonb", nullable: false),
                    winning_region_two_number = table.Column<int>(type: "integer", nullable: false),
                    total_tickets = table.Column<int>(type: "integer", nullable: false),
                    total_prizes_awarded = table.Column<int>(type: "integer", nullable: false),
                    total_prize_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lottery_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lotteries_account_id",
                table: "lotteries",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotteries");

            migrationBuilder.DropTable(
                name: "lottery_records");
        }
    }
}
