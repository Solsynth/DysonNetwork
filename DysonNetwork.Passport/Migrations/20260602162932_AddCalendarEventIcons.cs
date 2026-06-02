using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "background",
                table: "user_calendar_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "icon",
                table: "user_calendar_events",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background",
                table: "user_calendar_events");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "user_calendar_events");
        }
    }
}
