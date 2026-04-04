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
    public partial class UpdateFediverseSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_sn_quote_authorization_quote_authorization_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_quote_authorization_fediverse_actors_author_id",
                table: "sn_quote_authorization");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_quote_authorization_posts_quote_post_id",
                table: "sn_quote_authorization");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_quote_authorization_posts_target_post_id",
                table: "sn_quote_authorization");

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
                name: "pk_sn_quote_authorization",
                table: "sn_quote_authorization");

            migrationBuilder.RenameTable(
                name: "sn_quote_authorization",
                newName: "quote_authorizations");

            migrationBuilder.RenameIndex(
                name: "ix_sn_quote_authorization_target_post_id",
                table: "quote_authorizations",
                newName: "ix_quote_authorizations_target_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_quote_authorization_quote_post_id",
                table: "quote_authorizations",
                newName: "ix_quote_authorizations_quote_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_quote_authorization_author_id",
                table: "quote_authorizations",
                newName: "ix_quote_authorizations_author_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_quote_authorizations",
                table: "quote_authorizations",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_quote_authorizations_quote_authorization_id",
                table: "posts",
                column: "quote_authorization_id",
                principalTable: "quote_authorizations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_quote_authorizations_fediverse_actors_author_id",
                table: "quote_authorizations",
                column: "author_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_quote_authorizations_posts_quote_post_id",
                table: "quote_authorizations",
                column: "quote_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_quote_authorizations_posts_target_post_id",
                table: "quote_authorizations",
                column: "target_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_quote_authorizations_quote_authorization_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_authorizations_fediverse_actors_author_id",
                table: "quote_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_authorizations_posts_quote_post_id",
                table: "quote_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_authorizations_posts_target_post_id",
                table: "quote_authorizations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_quote_authorizations",
                table: "quote_authorizations");

            migrationBuilder.RenameTable(
                name: "quote_authorizations",
                newName: "sn_quote_authorization");

            migrationBuilder.RenameIndex(
                name: "ix_quote_authorizations_target_post_id",
                table: "sn_quote_authorization",
                newName: "ix_sn_quote_authorization_target_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_quote_authorizations_quote_post_id",
                table: "sn_quote_authorization",
                newName: "ix_sn_quote_authorization_quote_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_quote_authorizations_author_id",
                table: "sn_quote_authorization",
                newName: "ix_sn_quote_authorization_author_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_quote_authorization",
                table: "sn_quote_authorization",
                column: "id");

            migrationBuilder.CreateTable(
                name: "sn_account_auth_factor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    enabled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    secret = table.Column<string>(type: "character varying(8196)", maxLength: 8196, nullable: true),
                    trustworthy = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    caption = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    access_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    provided_identifier = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    provider = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_badge = table.Column<SnAccountBadgeRef>(type: "jsonb", nullable: true),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    birthday = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    experience = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    gender = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_seen_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    links = table.Column<List<SnProfileLink>>(type: "jsonb", nullable: true),
                    location = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    middle_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    pronouns = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    social_credits = table.Column<double>(type: "double precision", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    username_color = table.Column<UsernameColor>(type: "jsonb", nullable: true),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    cleared_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_automated = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    symbol = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audiences = table.Column<string>(type: "jsonb", nullable: false),
                    blacklist_factors = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    nonce = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    step_remain = table.Column<int>(type: "integer", nullable: false),
                    step_total = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    device_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    boost_points = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true)
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
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audiences = table.Column<string>(type: "jsonb", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
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
                    color = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                name: "fk_posts_sn_quote_authorization_quote_authorization_id",
                table: "posts",
                column: "quote_authorization_id",
                principalTable: "sn_quote_authorization",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_quote_authorization_fediverse_actors_author_id",
                table: "sn_quote_authorization",
                column: "author_id",
                principalTable: "fediverse_actors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_quote_authorization_posts_quote_post_id",
                table: "sn_quote_authorization",
                column: "quote_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_quote_authorization_posts_target_post_id",
                table: "sn_quote_authorization",
                column: "target_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
