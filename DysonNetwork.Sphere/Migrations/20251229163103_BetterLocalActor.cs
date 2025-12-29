using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class BetterLocalActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_local_actor",
                table: "fediverse_relationships");

            migrationBuilder.DropColumn(
                name: "local_account_id",
                table: "fediverse_relationships");

            migrationBuilder.DropColumn(
                name: "local_publisher_id",
                table: "fediverse_relationships");

            migrationBuilder.AddColumn<Guid>(
                name: "publisher_id",
                table: "fediverse_actors",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "publisher_id",
                table: "fediverse_actors");

            migrationBuilder.AddColumn<bool>(
                name: "is_local_actor",
                table: "fediverse_relationships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "local_account_id",
                table: "fediverse_relationships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "local_publisher_id",
                table: "fediverse_relationships",
                type: "uuid",
                nullable: true);
        }
    }
}
