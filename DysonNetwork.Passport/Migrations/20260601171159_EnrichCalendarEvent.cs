using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class EnrichCalendarEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "background",
                table: "account_statuses",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "icon",
                table: "account_statuses",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background",
                table: "account_statuses");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "account_statuses");
        }
    }
}
