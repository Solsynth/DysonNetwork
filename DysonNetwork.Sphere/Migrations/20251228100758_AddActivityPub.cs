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
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                name: "fediverse_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    content_html = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    in_reply_to = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    announced_content_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    // attachments = table.Column<List<ContentAttachment>>(type: "jsonb", nullable: true),
                    // mentions = table.Column<List<ContentMention>>(type: "jsonb", nullable: true),
                    // tags = table.Column<List<ContentTag>>(type: "jsonb", nullable: true),
                    // emojis = table.Column<List<ContentEmoji>>(type: "jsonb", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reply_count = table.Column<int>(type: "integer", nullable: false),
                    boost_count = table.Column<int>(type: "integer", nullable: false),
                    like_count = table.Column<int>(type: "integer", nullable: false),
                    local_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_contents_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fediverse_contents_fediverse_instances_instance_id",
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
                    is_following = table.Column<bool>(type: "boolean", nullable: false),
                    is_followed_by = table.Column<bool>(type: "boolean", nullable: false),
                    is_muting = table.Column<bool>(type: "boolean", nullable: false),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false),
                    followed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    followed_back_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_local_actor = table.Column<bool>(type: "boolean", nullable: false),
                    local_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                name: "fediverse_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    object_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    target_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    raw_data = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_activities_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fediverse_activities_fediverse_actors_target_actor_id",
                        column: x => x.target_actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_fediverse_activities_fediverse_contents_content_id",
                        column: x => x.content_id,
                        principalTable: "fediverse_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fediverse_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    emoji = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_reaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_reactions_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fediverse_reactions_fediverse_contents_content_id",
                        column: x => x.content_id,
                        principalTable: "fediverse_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_activities_actor_id",
                table: "fediverse_activities",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_activities_content_id",
                table: "fediverse_activities",
                column: "content_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_activities_target_actor_id",
                table: "fediverse_activities",
                column: "target_actor_id");

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
                name: "ix_fediverse_contents_actor_id",
                table: "fediverse_contents",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_contents_instance_id",
                table: "fediverse_contents",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_contents_uri",
                table: "fediverse_contents",
                column: "uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_instances_domain",
                table: "fediverse_instances",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_reactions_actor_id",
                table: "fediverse_reactions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_reactions_content_id",
                table: "fediverse_reactions",
                column: "content_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_actor_id",
                table: "fediverse_relationships",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_relationships_target_actor_id",
                table: "fediverse_relationships",
                column: "target_actor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fediverse_activities");

            migrationBuilder.DropTable(
                name: "fediverse_reactions");

            migrationBuilder.DropTable(
                name: "fediverse_relationships");

            migrationBuilder.DropTable(
                name: "fediverse_contents");

            migrationBuilder.DropTable(
                name: "fediverse_actors");

            migrationBuilder.DropTable(
                name: "fediverse_instances");

            migrationBuilder.DropColumn(
                name: "meta",
                table: "publishers");
        }
    }
}
