using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class EnrichAccountProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "birthday",
                table: "account_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "account_profiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "last_seen_at",
                table: "account_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pronouns",
                table: "account_profiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "birthday",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "pronouns",
                table: "account_profiles");
        }
    }
}
