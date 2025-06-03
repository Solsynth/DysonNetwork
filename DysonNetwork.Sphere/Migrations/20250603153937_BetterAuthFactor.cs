using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class BetterAuthFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "config",
                table: "account_auth_factors",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "enabled_at",
                table: "account_auth_factors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "expired_at",
                table: "account_auth_factors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "trustworthy",
                table: "account_auth_factors",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "config",
                table: "account_auth_factors");

            migrationBuilder.DropColumn(
                name: "enabled_at",
                table: "account_auth_factors");

            migrationBuilder.DropColumn(
                name: "expired_at",
                table: "account_auth_factors");

            migrationBuilder.DropColumn(
                name: "trustworthy",
                table: "account_auth_factors");
        }
    }
}
