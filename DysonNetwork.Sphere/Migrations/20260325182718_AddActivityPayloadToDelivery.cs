using System;
using System.Collections.Generic;
using System.Text.Json;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPayloadToDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_boosts_SnFediverseActor_actor_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "FK_boosts_SnPost_post_id",
                table: "boosts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_boosts",
                table: "boosts");

            migrationBuilder.DropIndex(
                name: "IX_boosts_actor_id_post_id",
                table: "boosts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SnPost_TempId1",
                table: "SnPost");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SnFediverseActor_TempId1",
                table: "SnFediverseActor");

            migrationBuilder.RenameTable(
                name: "SnPost",
                newName: "posts");

            migrationBuilder.RenameTable(
                name: "SnFediverseActor",
                newName: "fediverse_actors");

            migrationBuilder.RenameIndex(
                name: "IX_boosts_post_id",
                table: "boosts",
                newName: "ix_boosts_post_id");

            migrationBuilder.RenameIndex(
                name: "IX_boosts_actor_id",
                table: "boosts",
                newName: "ix_boosts_actor_id");

            migrationBuilder.RenameColumn(
                name: "TempId1",
                table: "posts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TempId1",
                table: "fediverse_actors",
                newName: "instance_id");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<SnCloudFileReferenceObject>>(
                name: "attachments",
                table: "posts",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<decimal>(
                name: "awarded_score",
                table: "posts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "boost_count",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "content_type",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "created_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

            migrationBuilder.AddColumn<Instant>(
                name: "deleted_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "posts",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "downvotes",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "drafted_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "edited_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<PostEmbedView>(
                name: "embed_view",
                table: "posts",
                type: "jsonb",
                nullable: true);

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

            migrationBuilder.AddColumn<bool>(
                name: "forwarded_gone",
                table: "posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "forwarded_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "posts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "locked_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<ContentMention>>(
                name: "mentions",
                table: "posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "metadata",
                table: "posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pin_mode",
                table: "posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "published_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "publisher_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reaction_score",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "realm_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "replied_gone",
                table: "posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "replied_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replies_count",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "sensitive_marks",
                table: "posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shadowban_reason",
                table: "posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "shadowbanned_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "posts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "posts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "updated_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

            migrationBuilder.AddColumn<int>(
                name: "upvotes",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "views_total",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "views_unique",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "fediverse_actors",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "fediverse_actors",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "created_at",
                table: "fediverse_actors",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

            migrationBuilder.AddColumn<Instant>(
                name: "deleted_at",
                table: "fediverse_actors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "featured_uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "followers_uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "following_uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_url",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inbox_uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_bot",
                table: "fediverse_actors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_discoverable",
                table: "fediverse_actors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "fediverse_actors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "last_activity_at",
                table: "fediverse_actors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "last_fetched_at",
                table: "fediverse_actors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "metadata",
                table: "fediverse_actors",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outbox_uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key",
                table: "fediverse_actors",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key_id",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "publisher_id",
                table: "fediverse_actors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Instant>(
                name: "updated_at",
                table: "fediverse_actors",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

            migrationBuilder.AddColumn<string>(
                name: "uri",
                table: "fediverse_actors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "fediverse_actors",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "pk_boosts",
                table: "boosts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_posts",
                table: "posts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_fediverse_actors",
                table: "fediverse_actors",
                column: "id");

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
                    activity_payload = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_pub_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "automod_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    default_action = table.Column<int>(type: "integer", nullable: false),
                    pattern = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_regex = table.Column<bool>(type: "boolean", nullable: false),
                    derank_weight = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automod_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discovery_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    applied_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_preferences", x => x.id);
                });

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
                    icon_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    contact_account_username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    active_users = table.Column<int>(type: "integer", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    is_silenced = table.Column<bool>(type: "boolean", nullable: false),
                    block_reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    last_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    metadata_fetched_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_instances", x => x.id);
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
                name: "post_awards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_awards", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_awards_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_featured_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    featured_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    social_credits = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_featured_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_featured_records_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_interest_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    interaction_count = table.Column<int>(type: "integer", nullable: false),
                    last_interacted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_signal_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_interest_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fediverse_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_reactions_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_post_reactions_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publishers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nick = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    private_key_pem = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    public_key_pem = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shadowban_reason = table.Column<int>(type: "integer", nullable: true),
                    shadowbanned_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publishers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_auth_factor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    secret = table.Column<string>(type: "character varying(8196)", maxLength: 8196, nullable: true),
                    config = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    trustworthy = table.Column<int>(type: "integer", nullable: false),
                    enabled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_auth_factor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_badge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    caption = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    activated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_badge", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    provided_identifier = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    access_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    refresh_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_contact",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    content = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_contact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    middle_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    gender = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    pronouns = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    location = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    links = table.Column<List<SnProfileLink>>(type: "jsonb", nullable: true),
                    username_color = table.Column<UsernameColor>(type: "jsonb", nullable: true),
                    birthday = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true),
                    active_badge = table.Column<SnAccountBadgeRef>(type: "jsonb", nullable: true),
                    experience = table.Column<int>(type: "integer", nullable: false),
                    social_credits = table.Column<double>(type: "double precision", nullable: false),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_status",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    symbol = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    cleared_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    app_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_automated = table.Column<bool>(type: "boolean", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_auth_challenge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    step_remain = table.Column<int>(type: "integer", nullable: false),
                    step_total = table.Column<int>(type: "integer", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    blacklist_factors = table.Column<string>(type: "jsonb", nullable: false),
                    audiences = table.Column<string>(type: "jsonb", nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    nonce = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_auth_challenge", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_auth_client",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    device_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    device_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_auth_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_realm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boost_points = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_realm", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_category_links",
                columns: table => new
                {
                    categories_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posts_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_category_links", x => new { x.categories_id, x.posts_id });
                    table.ForeignKey(
                        name: "fk_post_category_links_post_categories_categories_id",
                        column: x => x.categories_id,
                        principalTable: "post_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_category_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_category_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_category_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_category_subscriptions_post_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "post_categories",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_post_category_subscriptions_post_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "post_tags",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "post_tag_links",
                columns: table => new
                {
                    posts_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tags_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_tag_links", x => new { x.posts_id, x.tags_id });
                    table.ForeignKey(
                        name: "fk_post_tag_links_post_tags_tags_id",
                        column: x => x.tags_id,
                        principalTable: "post_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_tag_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_streams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    room_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ingress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ingress_stream_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    egress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    hls_egress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    hls_playlist_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    hls_started_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    total_duration_seconds = table.Column<long>(type: "bigint", nullable: false),
                    viewer_count = table.Column<int>(type: "integer", nullable: false),
                    peak_viewer_count = table.Column<int>(type: "integer", nullable: false),
                    total_award_score = table.Column<decimal>(type: "numeric", nullable: false),
                    distributed_award_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    thumbnail = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_streams", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_streams_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "polls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polls", x => x.id);
                    table.ForeignKey(
                        name: "fk_polls_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_collections", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_collections_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publisher_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_publisher_features_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publisher_members",
                columns: table => new
                {
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_members", x => new { x.publisher_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_publisher_members_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publisher_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_publisher_subscriptions_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sticker_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    icon = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    prefix = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sticker_packs", x => x.id);
                    table.ForeignKey(
                        name: "fk_sticker_packs_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_auth_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    last_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    audiences = table.Column<string>(type: "jsonb", nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    app_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_auth_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_auth_session_sn_auth_client_client_id",
                        column: x => x.client_id,
                        principalTable: "sn_auth_client",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sn_auth_session_sn_auth_session_parent_session_id",
                        column: x => x.parent_session_id,
                        principalTable: "sn_auth_session",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sn_realm_label",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    color = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_realm_label", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_realm_label_sn_realm_realm_id",
                        column: x => x.realm_id,
                        principalTable: "sn_realm",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_stream_awards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    live_stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_stream_awards", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_stream_awards_live_streams_live_stream_id",
                        column: x => x.live_stream_id,
                        principalTable: "live_streams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_stream_chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    live_stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    timeout_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_stream_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_stream_chat_messages_live_streams_live_stream_id",
                        column: x => x.live_stream_id,
                        principalTable: "live_streams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    answer = table.Column<Dictionary<string, JsonElement>>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_answers_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    options = table.Column<List<SnPollOption>>(type: "jsonb", nullable: true),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_questions_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_collection_links",
                columns: table => new
                {
                    collections_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posts_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_collection_links", x => new { x.collections_id, x.posts_id });
                    table.ForeignKey(
                        name: "fk_post_collection_links_post_collections_collections_id",
                        column: x => x.collections_id,
                        principalTable: "post_collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_collection_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sticker_pack_ownerships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sticker_pack_ownerships", x => x.id);
                    table.ForeignKey(
                        name: "fk_sticker_pack_ownerships_sticker_packs_pack_id",
                        column: x => x.pack_id,
                        principalTable: "sticker_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stickers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    image = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: false),
                    pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stickers", x => x.id);
                    table.ForeignKey(
                        name: "fk_stickers_sticker_packs_pack_id",
                        column: x => x.pack_id,
                        principalTable: "sticker_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posts_actor_id",
                table: "posts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_forwarded_post_id",
                table: "posts",
                column: "forwarded_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_publisher_id",
                table: "posts",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_replied_post_id",
                table: "posts",
                column: "replied_post_id");

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
                name: "ix_automod_rules_name",
                table: "automod_rules",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovery_preferences_account_id_kind_reference_id",
                table: "discovery_preferences",
                columns: new[] { "account_id", "kind", "reference_id" },
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
                name: "ix_live_stream_awards_live_stream_id",
                table: "live_stream_awards",
                column: "live_stream_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_stream_chat_messages_live_stream_id",
                table: "live_stream_chat_messages",
                column: "live_stream_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_streams_publisher_id",
                table: "live_streams",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_answers_poll_id",
                table: "poll_answers",
                column: "poll_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_questions_poll_id",
                table: "poll_questions",
                column: "poll_id");

            migrationBuilder.CreateIndex(
                name: "ix_polls_publisher_id",
                table: "polls",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_awards_post_id",
                table: "post_awards",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_category_links_posts_id",
                table: "post_category_links",
                column: "posts_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_category_subscriptions_category_id",
                table: "post_category_subscriptions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_category_subscriptions_tag_id",
                table: "post_category_subscriptions",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_links_posts_id",
                table: "post_collection_links",
                column: "posts_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_collections_publisher_id",
                table: "post_collections",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_featured_records_post_id",
                table: "post_featured_records",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_interest_profiles_account_id_kind_reference_id",
                table: "post_interest_profiles",
                columns: new[] { "account_id", "kind", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_actor_id",
                table: "post_reactions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_post_id",
                table: "post_reactions",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_tag_links_tags_id",
                table: "post_tag_links",
                column: "tags_id");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_features_publisher_id",
                table: "publisher_features",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_subscriptions_publisher_id",
                table: "publisher_subscriptions",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_name",
                table: "publishers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_session_client_id",
                table: "sn_auth_session",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_session_parent_session_id",
                table: "sn_auth_session",
                column: "parent_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_realm_slug",
                table: "sn_realm",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_realm_label_realm_id",
                table: "sn_realm_label",
                column: "realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_pack_ownerships_pack_id",
                table: "sticker_pack_ownerships",
                column: "pack_id");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_packs_prefix",
                table: "sticker_packs",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sticker_packs_publisher_id",
                table: "sticker_packs",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_stickers_pack_id",
                table: "stickers",
                column: "pack_id");

            migrationBuilder.CreateIndex(
                name: "ix_stickers_slug",
                table: "stickers",
                column: "slug");

            migrationBuilder.AddForeignKey(
                name: "fk_boosts_fediverse_actors_actor_id",
                table: "boosts",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_boosts_posts_post_id",
                table: "boosts",
                column: "post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fediverse_actors_fediverse_instances_instance_id",
                table: "fediverse_actors",
                column: "instance_id",
                principalTable: "fediverse_instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_posts_fediverse_actors_actor_id",
                table: "posts",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_posts_forwarded_post_id",
                table: "posts",
                column: "forwarded_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_posts_posts_replied_post_id",
                table: "posts",
                column: "replied_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_boosts_fediverse_actors_actor_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "fk_boosts_posts_post_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "fk_fediverse_actors_fediverse_instances_instance_id",
                table: "fediverse_actors");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_fediverse_actors_actor_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_posts_forwarded_post_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_posts_replied_post_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts");

            migrationBuilder.DropTable(
                name: "activity_pub_deliveries");

            migrationBuilder.DropTable(
                name: "automod_rules");

            migrationBuilder.DropTable(
                name: "discovery_preferences");

            migrationBuilder.DropTable(
                name: "fediverse_instances");

            migrationBuilder.DropTable(
                name: "fediverse_relationships");

            migrationBuilder.DropTable(
                name: "live_stream_awards");

            migrationBuilder.DropTable(
                name: "live_stream_chat_messages");

            migrationBuilder.DropTable(
                name: "poll_answers");

            migrationBuilder.DropTable(
                name: "poll_questions");

            migrationBuilder.DropTable(
                name: "post_awards");

            migrationBuilder.DropTable(
                name: "post_category_links");

            migrationBuilder.DropTable(
                name: "post_category_subscriptions");

            migrationBuilder.DropTable(
                name: "post_collection_links");

            migrationBuilder.DropTable(
                name: "post_featured_records");

            migrationBuilder.DropTable(
                name: "post_interest_profiles");

            migrationBuilder.DropTable(
                name: "post_reactions");

            migrationBuilder.DropTable(
                name: "post_tag_links");

            migrationBuilder.DropTable(
                name: "publisher_features");

            migrationBuilder.DropTable(
                name: "publisher_members");

            migrationBuilder.DropTable(
                name: "publisher_subscriptions");

            migrationBuilder.DropTable(
                name: "sn_account_auth_factor");

            migrationBuilder.DropTable(
                name: "sn_account_badge");

            migrationBuilder.DropTable(
                name: "sn_account_connection");

            migrationBuilder.DropTable(
                name: "sn_account_contact");

            migrationBuilder.DropTable(
                name: "sn_account_profile");

            migrationBuilder.DropTable(
                name: "sn_account_status");

            migrationBuilder.DropTable(
                name: "sn_auth_challenge");

            migrationBuilder.DropTable(
                name: "sn_auth_session");

            migrationBuilder.DropTable(
                name: "sn_realm_label");

            migrationBuilder.DropTable(
                name: "sticker_pack_ownerships");

            migrationBuilder.DropTable(
                name: "stickers");

            migrationBuilder.DropTable(
                name: "live_streams");

            migrationBuilder.DropTable(
                name: "polls");

            migrationBuilder.DropTable(
                name: "post_categories");

            migrationBuilder.DropTable(
                name: "post_collections");

            migrationBuilder.DropTable(
                name: "post_tags");

            migrationBuilder.DropTable(
                name: "sn_auth_client");

            migrationBuilder.DropTable(
                name: "sn_realm");

            migrationBuilder.DropTable(
                name: "sticker_packs");

            migrationBuilder.DropTable(
                name: "publishers");

            migrationBuilder.DropPrimaryKey(
                name: "pk_boosts",
                table: "boosts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_posts",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_actor_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_forwarded_post_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_publisher_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_replied_post_id",
                table: "posts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_fediverse_actors",
                table: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_instance_id",
                table: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "awarded_score",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "boost_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "description",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "downvotes",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "drafted_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "embed_view",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_uri",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "forwarded_gone",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "forwarded_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "locked_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "mentions",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "pin_mode",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "publisher_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "reaction_score",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "realm_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "replied_gone",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "replied_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "replies_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "sensitive_marks",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "shadowban_reason",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "shadowbanned_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "title",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "upvotes",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "views_total",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "views_unique",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "id",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "featured_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "followers_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "following_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "header_url",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "inbox_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "is_bot",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "is_discoverable",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "last_fetched_at",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "outbox_uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "public_key",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "public_key_id",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "publisher_id",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "type",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "uri",
                table: "fediverse_actors");

            migrationBuilder.DropColumn(
                name: "username",
                table: "fediverse_actors");

            migrationBuilder.RenameTable(
                name: "posts",
                newName: "SnPost");

            migrationBuilder.RenameTable(
                name: "fediverse_actors",
                newName: "SnFediverseActor");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_post_id",
                table: "boosts",
                newName: "IX_boosts_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_actor_id",
                table: "boosts",
                newName: "IX_boosts_actor_id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SnPost",
                newName: "TempId1");

            migrationBuilder.RenameColumn(
                name: "instance_id",
                table: "SnFediverseActor",
                newName: "TempId1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_boosts",
                table: "boosts",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SnPost_TempId1",
                table: "SnPost",
                column: "TempId1");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SnFediverseActor_TempId1",
                table: "SnFediverseActor",
                column: "TempId1");

            migrationBuilder.CreateIndex(
                name: "IX_boosts_actor_id_post_id",
                table: "boosts",
                columns: new[] { "actor_id", "post_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_boosts_SnFediverseActor_actor_id",
                table: "boosts",
                column: "actor_id",
                principalTable: "SnFediverseActor",
                principalColumn: "TempId1",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_boosts_SnPost_post_id",
                table: "boosts",
                column: "post_id",
                principalTable: "SnPost",
                principalColumn: "TempId1",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
