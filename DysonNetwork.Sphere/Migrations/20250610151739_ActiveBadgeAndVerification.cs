using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class ActiveBadgeAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verified_as",
                table: "realms");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "realms");

            migrationBuilder.AddColumn<VerificationMark>(
                name: "verification",
                table: "realms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<VerificationMark>(
                name: "verification",
                table: "publishers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "activated_at",
                table: "badges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<BadgeReferenceObject>(
                name: "active_badge",
                table: "account_profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<VerificationMark>(
                name: "verification",
                table: "account_profiles",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verification",
                table: "realms");

            migrationBuilder.DropColumn(
                name: "verification",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "activated_at",
                table: "badges");

            migrationBuilder.DropColumn(
                name: "active_badge",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "verification",
                table: "account_profiles");

            migrationBuilder.AddColumn<string>(
                name: "verified_as",
                table: "realms",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "verified_at",
                table: "realms",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
