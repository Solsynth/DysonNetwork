using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherSubscriberManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "end_reason",
                table: "publisher_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "ended_at",
                table: "publisher_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ended_by_account_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify",
                table: "publisher_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_reason",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "ended_at",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "ended_by_account_id",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "notify",
                table: "publisher_subscriptions");
        }
    }
}
