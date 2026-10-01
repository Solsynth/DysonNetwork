using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class MergeFediverseActorIntoPublisher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_boosts_fediverse_actors_actor_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_keys_fediverse_actors_actor_id",
                table: "fediverse_keys");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_relationships_fediverse_actors_actor_id",
                table: "fediverse_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_relationships_fediverse_actors_target_actor_id",
                table: "fediverse_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_post_reactions_fediverse_actors_actor_id",
                table: "post_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_fediverse_actors_actor_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_authorizations_fediverse_actors_author_id",
                table: "quote_authorizations");

            migrationBuilder.DropTable(
                name: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_posts_actor_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_keys_actor_id",
                table: "fediverse_keys");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "fediverse_keys");

            migrationBuilder.RenameColumn(
                name: "author_id",
                table: "quote_authorizations",
                newName: "publisher_id");

            migrationBuilder.RenameIndex(
                name: "ix_quote_authorizations_author_id",
                table: "quote_authorizations",
                newName: "ix_quote_authorizations_publisher_id");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "post_reactions",
                newName: "publisher_id");

            migrationBuilder.RenameIndex(
                name: "ix_post_reactions_actor_id",
                table: "post_reactions",
                newName: "ix_post_reactions_publisher_id");

            migrationBuilder.RenameColumn(
                name: "target_actor_id",
                table: "fediverse_relationships",
                newName: "target_publisher_id");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "fediverse_relationships",
                newName: "publisher_id");

            migrationBuilder.RenameIndex(
                name: "ix_fediverse_relationships_target_actor_id",
                table: "fediverse_relationships",
                newName: "ix_fediverse_relationships_target_publisher_id");

            migrationBuilder.RenameIndex(
                name: "ix_fediverse_relationships_actor_id",
                table: "fediverse_relationships",
                newName: "ix_fediverse_relationships_publisher_id");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "boosts",
                newName: "publisher_id");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_actor_id",
                table: "boosts",
                newName: "ix_boosts_publisher_id");

            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                table: "publishers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "featured_uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "followers_uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "following_uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_url",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inbox_uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instance_domain",
                table: "publishers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "instance_id",
                table: "publishers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_bot",
                table: "publishers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_community",
                table: "publishers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_discoverable",
                table: "publishers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "publishers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "last_activity_at",
                table: "publishers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "last_fetched_at",
                table: "publishers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "metadata",
                table: "publishers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "outbox_fetched_at",
                table: "publishers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outbox_uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key",
                table: "publishers",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key_id",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uri",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "publishers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "publisher_id",
                table: "posts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishers_instance_id",
                table: "publishers",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_uri_deleted_at",
                table: "publishers",
                columns: new[] { "uri", "deleted_at" },
                unique: true,
                filter: "uri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_publisher_id",
                table: "fediverse_keys",
                column: "publisher_id");

            migrationBuilder.AddForeignKey(
                name: "fk_boosts_publishers_publisher_id",
                table: "boosts",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_keys_publishers_publisher_id",
                table: "fediverse_keys",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_relationships_publishers_publisher_id",
                table: "fediverse_relationships",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_relationships_publishers_target_publisher_id",
                table: "fediverse_relationships",
                column: "target_publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_post_reactions_publishers_publisher_id",
                table: "post_reactions",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_publishers_fediverse_instances_instance_id",
                table: "publishers",
                column: "instance_id",
                principalTable: "fediverse_instances",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_quote_authorizations_publishers_publisher_id",
                table: "quote_authorizations",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_boosts_publishers_publisher_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_keys_publishers_publisher_id",
                table: "fediverse_keys");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_relationships_publishers_publisher_id",
                table: "fediverse_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_relationships_publishers_target_publisher_id",
                table: "fediverse_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_post_reactions_publishers_publisher_id",
                table: "post_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_publishers_fediverse_instances_instance_id",
                table: "publishers");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_authorizations_publishers_publisher_id",
                table: "quote_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_publishers_instance_id",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_publishers_uri_deleted_at",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_keys_publisher_id",
                table: "fediverse_keys");

            migrationBuilder.DropColumn(
                name: "actor_type",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "featured_uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "followers_uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "following_uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "header_url",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "inbox_uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "instance_domain",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "instance_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "is_bot",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "is_community",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "is_discoverable",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "last_fetched_at",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "outbox_fetched_at",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "outbox_uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "public_key",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "public_key_id",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "uri",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "username",
                table: "publishers");

            migrationBuilder.RenameColumn(
                name: "publisher_id",
                table: "quote_authorizations",
                newName: "author_id");

            migrationBuilder.RenameIndex(
                name: "ix_quote_authorizations_publisher_id",
                table: "quote_authorizations",
                newName: "ix_quote_authorizations_author_id");

            migrationBuilder.RenameColumn(
                name: "publisher_id",
                table: "post_reactions",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "ix_post_reactions_publisher_id",
                table: "post_reactions",
                newName: "ix_post_reactions_actor_id");

            migrationBuilder.RenameColumn(
                name: "target_publisher_id",
                table: "fediverse_relationships",
                newName: "target_actor_id");

            migrationBuilder.RenameColumn(
                name: "publisher_id",
                table: "fediverse_relationships",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "ix_fediverse_relationships_target_publisher_id",
                table: "fediverse_relationships",
                newName: "ix_fediverse_relationships_target_actor_id");

            migrationBuilder.RenameIndex(
                name: "ix_fediverse_relationships_publisher_id",
                table: "fediverse_relationships",
                newName: "ix_fediverse_relationships_actor_id");

            migrationBuilder.RenameColumn(
                name: "publisher_id",
                table: "boosts",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_publisher_id",
                table: "boosts",
                newName: "ix_boosts_actor_id");

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

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "fediverse_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fediverse_actors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    display_name = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    featured_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    followers_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    following_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    header_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    inbox_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_bot = table.Column<bool>(type: "boolean", nullable: false),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    last_activity_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    outbox_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    outbox_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    public_key = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    public_key_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_posts_actor_id",
                table: "posts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_actor_id",
                table: "fediverse_keys",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_instance_id",
                table: "fediverse_actors",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_uri_deleted_at",
                table: "fediverse_actors",
                columns: new[] { "uri", "deleted_at" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_boosts_fediverse_actors_actor_id",
                table: "boosts",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_keys_fediverse_actors_actor_id",
                table: "fediverse_keys",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_relationships_fediverse_actors_actor_id",
                table: "fediverse_relationships",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_relationships_fediverse_actors_target_actor_id",
                table: "fediverse_relationships",
                column: "target_actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

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
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_authorizations_fediverse_actors_author_id",
                table: "quote_authorizations",
                column: "author_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
