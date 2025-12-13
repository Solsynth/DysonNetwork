using DysonNetwork.Shared.Geometry;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class RefactorGeoIpPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE auth_challenges SET location = NULL;");
            migrationBuilder.Sql("UPDATE action_logs SET location = NULL;");

            migrationBuilder.DropColumn(
                name: "location",
                table: "auth_challenges");

            migrationBuilder.AddColumn<GeoPoint>(
                name: "location",
                table: "auth_challenges",
                type: "jsonb",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "location",
                table: "action_logs");

            migrationBuilder.AddColumn<GeoPoint>(
                name: "location",
                table: "action_logs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location",
                table: "auth_challenges");

            migrationBuilder.AddColumn<Point>(
                name: "location",
                table: "auth_challenges",
                type: "geometry",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "location",
                table: "action_logs");

            migrationBuilder.AddColumn<Point>(
                name: "location",
                table: "action_logs",
                type: "geometry",
                nullable: true);
        }
    }
}
