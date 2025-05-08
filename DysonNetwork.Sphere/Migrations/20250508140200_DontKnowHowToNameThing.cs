using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class DontKnowHowToNameThing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "meta",
                table: "activities",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<ICollection<long>>(
                name: "users_visible",
                table: "activities",
                type: "jsonb",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "meta",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "users_visible",
                table: "activities");
        }
    }
}
