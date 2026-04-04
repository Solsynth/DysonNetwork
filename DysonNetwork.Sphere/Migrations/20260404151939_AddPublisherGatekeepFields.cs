using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherGatekeepFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "gatekept_follows",
                table: "publishers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "moderate_subscription",
                table: "publishers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "fediverse_relationships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_community",
                table: "fediverse_actors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "fediverse_actors",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE publishers
                SET moderate_subscription = true
                WHERE id IN (
                    SELECT publisher_id FROM publisher_features
                    WHERE flag = 'followRequiresApproval'
                    AND (expired_at IS NULL OR expired_at > NOW())
                );
            ");

            migrationBuilder.Sql(@"
                UPDATE publishers
                SET gatekept_follows = true
                WHERE id IN (
                    SELECT publisher_id FROM publisher_features
                    WHERE flag = 'postsRequireFollow'
                    AND (expired_at IS NULL OR expired_at > NOW())
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM publisher_features
                WHERE flag IN ('followRequiresApproval', 'postsRequireFollow');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gatekept_follows",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "moderate_subscription",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "fediverse_relationships");

            migrationBuilder.DropColumn(
                name: "is_community",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "fediverse_actors");
        }
    }
}
