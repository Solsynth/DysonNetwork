using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class FixRelationshipIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_boosts_fediverse_actors_actor_id",
                table: "boosts");

            migrationBuilder.DropForeignKey(
                name: "FK_boosts_posts_post_id",
                table: "boosts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_boosts",
                table: "boosts");

            migrationBuilder.DropIndex(
                name: "IX_boosts_actor_id_post_id",
                table: "boosts");

            migrationBuilder.RenameIndex(
                name: "IX_boosts_post_id",
                table: "boosts",
                newName: "ix_boosts_post_id");

            migrationBuilder.RenameIndex(
                name: "IX_boosts_actor_id",
                table: "boosts",
                newName: "ix_boosts_actor_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_boosts",
                table: "boosts",
                column: "id");

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
                name: "sn_auth_client");

            migrationBuilder.DropTable(
                name: "sn_realm");

            migrationBuilder.DropPrimaryKey(
                name: "pk_boosts",
                table: "boosts");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_post_id",
                table: "boosts",
                newName: "IX_boosts_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_boosts_actor_id",
                table: "boosts",
                newName: "IX_boosts_actor_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_boosts",
                table: "boosts",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_boosts_actor_id_post_id",
                table: "boosts",
                columns: new[] { "actor_id", "post_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_boosts_fediverse_actors_actor_id",
                table: "boosts",
                column: "actor_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_boosts_posts_post_id",
                table: "boosts",
                column: "post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
