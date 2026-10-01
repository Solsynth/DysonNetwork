using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class UnifyPublisherRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fediverse_relationships");

            migrationBuilder.DropTable(
                name: "publisher_follow_requests");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Instant>(
                name: "followed_at",
                table: "publisher_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "follower_publisher_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_blocking",
                table: "publisher_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_muting",
                table: "publisher_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reject_reason",
                table: "publisher_subscriptions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "reviewed_at",
                table: "publisher_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_account_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "state",
                table: "publisher_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE publisher_subscriptions SET state = 1 WHERE state = 0;");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_subscriptions_follower_publisher_id_publisher_id_",
                table: "publisher_subscriptions",
                columns: new[] { "follower_publisher_id", "publisher_id", "state", "ended_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_publisher_subscriptions_publishers_follower_publisher_id",
                table: "publisher_subscriptions",
                column: "follower_publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_publisher_subscriptions_publishers_follower_publisher_id",
                table: "publisher_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_publisher_subscriptions_follower_publisher_id_publisher_id_",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "followed_at",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "follower_publisher_id",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "is_blocking",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "is_muting",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "reject_reason",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "reviewed_by_account_id",
                table: "publisher_subscriptions");

            migrationBuilder.DropColumn(
                name: "state",
                table: "publisher_subscriptions");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "publisher_subscriptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "fediverse_relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    followed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false),
                    is_muting = table.Column<bool>(type: "boolean", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_relationships", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_relationships_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fediverse_relationships_publishers_target_publisher_id",
                        column: x => x.target_publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publisher_follow_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    reviewed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_follow_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_publisher_follow_requests_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_publisher_id",
                table: "fediverse_relationships",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_target_publisher_id",
                table: "fediverse_relationships",
                column: "target_publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_follow_requests_publisher_id",
                table: "publisher_follow_requests",
                column: "publisher_id");
        }
    }
}
