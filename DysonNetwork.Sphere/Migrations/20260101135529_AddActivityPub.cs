using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "meta",
                table: "publishers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "private_key_pem",
                table: "publishers",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key_pem",
                table: "publishers",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fediverse_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    software = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    version = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    is_silenced = table.Column<bool>(type: "boolean", nullable: false),
                    block_reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    last_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    active_users = table.Column<int>(type: "integer", nullable: true),
                    contact_account_username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    icon_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    metadata_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fediverse_actors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    inbox_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    outbox_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    followers_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    following_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    featured_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    public_key_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    public_key = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    header_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_bot = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    is_discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    type = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: "Person"),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_actors", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_actors_fediverse_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "fediverse_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fediverse_relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    is_muting = table.Column<bool>(type: "boolean", nullable: false),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false),
                    followed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    followed_back_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_relationships", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_relationships_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fediverse_relationships_fediverse_actors_target_actor_id",
                        column: x => x.target_actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_pub_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    inbox_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    actor_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    last_attempt_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    next_retry_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    response_status_code = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_pub_deliveries", x => x.id);
                });

            migrationBuilder.RenameColumn(
                name: "meta",
                table: "posts",
                newName: "metadata");

            migrationBuilder.AlterColumn<Guid>(
                name: "publisher_id",
                table: "posts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "boost_count",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "content_type",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fediverse_type",
                table: "posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fediverse_uri",
                table: "posts",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "posts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<List<ContentMention>>(
                name: "mentions",
                table: "posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replies_count",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "post_reactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "post_reactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fediverse_uri",
                table: "post_reactions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_local",
                table: "post_reactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_instance_id",
                table: "fediverse_actors",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_uri",
                table: "fediverse_actors",
                column: "uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_instances_domain",
                table: "fediverse_instances",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_actor_id",
                table: "fediverse_relationships",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_target_actor_id",
                table: "fediverse_relationships",
                column: "target_actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_actor_id",
                table: "posts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_actor_id",
                table: "post_reactions",
                column: "actor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_post_reactions_fediverse_actors_actor_id",
                table: "post_reactions",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_fediverse_actors_actor_id",
                table: "posts",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_post_reactions_fediverse_actors_actor_id",
                table: "post_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_fediverse_actors_actor_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_actor_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_post_reactions_actor_id",
                table: "post_reactions");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_relationships_target_actor_id",
                table: "fediverse_relationships");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_relationships_actor_id",
                table: "fediverse_relationships");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_instances_domain",
                table: "fediverse_instances");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_uri",
                table: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_instance_id",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "is_local",
                table: "post_reactions");

            migrationBuilder.DropColumn(
                name: "fediverse_uri",
                table: "post_reactions");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "post_reactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "post_reactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "replies_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "mentions",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_uri",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "boost_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "posts");

            migrationBuilder.AlterColumn<Guid>(
                name: "publisher_id",
                table: "posts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "metadata",
                table: "posts",
                newName: "meta");

            migrationBuilder.DropTable(
                name: "activity_pub_deliveries");

            migrationBuilder.DropTable(
                name: "fediverse_relationships");

            migrationBuilder.DropTable(
                name: "fediverse_actors");

            migrationBuilder.DropTable(
                name: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "public_key_pem",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "private_key_pem",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "meta",
                table: "publishers");
        }
    }
}
