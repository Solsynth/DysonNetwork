using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "rating",
                table: "publishers",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "post_tags",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "event_ends_at",
                table: "post_tags",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_event",
                table: "post_tags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_protected",
                table: "post_tags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_publisher_id",
                table: "post_tags",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "publisher_rating_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    delta = table.Column<double>(type: "double precision", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_rating_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_publisher_rating_records_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_tags_owner_publisher_id",
                table: "post_tags",
                column: "owner_publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_rating_records_publisher_id",
                table: "publisher_rating_records",
                column: "publisher_id");

            migrationBuilder.AddForeignKey(
                name: "fk_post_tags_publishers_owner_publisher_id",
                table: "post_tags",
                column: "owner_publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_post_tags_publishers_owner_publisher_id",
                table: "post_tags");

            migrationBuilder.DropTable(
                name: "publisher_rating_records");

            migrationBuilder.DropIndex(
                name: "ix_post_tags_owner_publisher_id",
                table: "post_tags");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "description",
                table: "post_tags");

            migrationBuilder.DropColumn(
                name: "event_ends_at",
                table: "post_tags");

            migrationBuilder.DropColumn(
                name: "is_event",
                table: "post_tags");

            migrationBuilder.DropColumn(
                name: "is_protected",
                table: "post_tags");

            migrationBuilder.DropColumn(
                name: "owner_publisher_id",
                table: "post_tags");
        }
    }
}
