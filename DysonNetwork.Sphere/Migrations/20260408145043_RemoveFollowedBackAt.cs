using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFollowedBackAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "followed_back_at",
                table: "fediverse_relationships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "followed_back_at",
                table: "fediverse_relationships",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
