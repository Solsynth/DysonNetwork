using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class MergeFediverseDataClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts");

            migrationBuilder.DropTable(
                name: "fediverse_activities");

            migrationBuilder.DropTable(
                name: "fediverse_reactions");

            migrationBuilder.DropTable(
                name: "fediverse_contents");

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

            migrationBuilder.AddColumn<int>(
                name: "like_count",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
                principalColumn: "id");
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

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "boost_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_type",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "fediverse_uri",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "like_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "mentions",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "replies_count",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "post_reactions");

            migrationBuilder.DropColumn(
                name: "fediverse_uri",
                table: "post_reactions");

            migrationBuilder.DropColumn(
                name: "is_local",
                table: "post_reactions");

            migrationBuilder.RenameColumn(
                name: "metadata",
                table: "posts",
                newName: "meta");

            migrationBuilder.AlterColumn<Guid>(
                name: "publisher_id",
                table: "posts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "post_reactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "fediverse_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    announced_content_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    boost_count = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    content_html = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    in_reply_to = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    like_count = table.Column<int>(type: "integer", nullable: false),
                    local_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    reply_count = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
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
                name: "fediverse_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    local_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    object_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    raw_data = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    target_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
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
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    emoji = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    local_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    local_reaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
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
                name: "ix_fediverse_reactions_actor_id",
                table: "fediverse_reactions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_reactions_content_id",
                table: "fediverse_reactions",
                column: "content_id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_publishers_publisher_id",
                table: "posts",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
