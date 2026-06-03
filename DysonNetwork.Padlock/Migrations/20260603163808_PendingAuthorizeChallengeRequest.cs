using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class PendingAuthorizeChallengeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "approved_at",
                table: "auth_challenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_session_id",
                table: "auth_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "declined_at",
                table: "auth_challenges",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "approved_by_session_id",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "declined_at",
                table: "auth_challenges");
        }
    }
}
