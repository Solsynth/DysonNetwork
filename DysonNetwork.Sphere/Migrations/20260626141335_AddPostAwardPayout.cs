using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostAwardPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payout_wallet_id",
                table: "publishers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "settled_at",
                table: "post_awards",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payout_wallet_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "settled_at",
                table: "post_awards");
        }
    }
}
