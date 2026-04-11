using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class LocationPinUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "meet_id",
                table: "location_pins",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_pins_meet_id_status",
                table: "location_pins",
                columns: new[] { "meet_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_location_pins_meet_id_status",
                table: "location_pins");

            migrationBuilder.DropColumn(
                name: "meet_id",
                table: "location_pins");
        }
    }
}
