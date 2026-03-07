using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Geometry;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class CleanUpUnusedRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_sn_account_account_id",
                table: "account_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_account_relationships_sn_account_account_id",
                table: "account_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_account_relationships_sn_account_related_id",
                table: "account_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_badges_sn_account_sn_account_id",
                table: "badges");

            migrationBuilder.DropTable(
                name: "sn_account_auth_factor");

            migrationBuilder.DropTable(
                name: "sn_account_connection");

            migrationBuilder.DropTable(
                name: "sn_account_contact");

            migrationBuilder.DropTable(
                name: "sn_auth_challenge");

            migrationBuilder.DropTable(
                name: "sn_auth_session");

            migrationBuilder.DropTable(
                name: "sn_e2ee_device");

            migrationBuilder.DropTable(
                name: "sn_e2ee_envelope");

            migrationBuilder.DropTable(
                name: "sn_e2ee_one_time_pre_key");

            migrationBuilder.DropTable(
                name: "sn_e2ee_session");

            migrationBuilder.DropTable(
                name: "sn_mls_device_membership");

            migrationBuilder.DropTable(
                name: "sn_mls_group_state");

            migrationBuilder.DropTable(
                name: "sn_mls_key_package");

            migrationBuilder.DropTable(
                name: "sn_subscription_reference_object");

            migrationBuilder.DropTable(
                name: "sn_auth_client");

            migrationBuilder.DropTable(
                name: "sn_e2ee_key_bundle");

            migrationBuilder.DropTable(
                name: "sn_account");

            migrationBuilder.DropIndex(
                name: "ix_badges_sn_account_id",
                table: "badges");

            migrationBuilder.DropIndex(
                name: "ix_account_relationships_related_id",
                table: "account_relationships");

            migrationBuilder.DropIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "sn_account_id",
                table: "badges");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sn_account_id",
                table: "badges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sn_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    automated_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_superuser = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nick = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_e2ee_envelope",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    client_message_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    delivery_status = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    header = table.Column<byte[]>(type: "bytea", nullable: true),
                    legacy_account_scoped = table.Column<bool>(type: "boolean", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signature = table.Column<byte[]>(type: "bytea", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_e2ee_envelope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_e2ee_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    hint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    initiated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_e2ee_session", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_mls_device_membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    joined_epoch = table.Column<long>(type: "bigint", nullable: false),
                    last_reshare_completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reshare_required_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_seen_epoch = table.Column<long>(type: "bigint", nullable: true),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_mls_device_membership", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_mls_group_state",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    last_commit_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_mls_group_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sn_subscription_reference_object",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    final_price = table.Column<decimal>(type: "numeric", nullable: false),
                    identifier = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false),
                    renewal_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_subscription_reference_object", x => x.id);
                });

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
                    table.ForeignKey(
                        name: "fk_sn_account_auth_factor_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_account_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
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
                    table.ForeignKey(
                        name: "fk_sn_account_connection_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "fk_sn_account_contact_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "fk_sn_auth_challenge_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "fk_sn_auth_client_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_e2ee_device",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    last_bundle_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_e2ee_device", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_e2ee_device_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_e2ee_key_bundle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    identity_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    signed_pre_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    signed_pre_key_expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    signed_pre_key_id = table.Column<int>(type: "integer", nullable: true),
                    signed_pre_key_signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_e2ee_key_bundle", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_e2ee_key_bundle_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_mls_key_package",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciphersuite = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    consumed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    consumed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    key_package = table.Column<byte[]>(type: "bytea", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_mls_key_package", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_mls_key_package_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sn_auth_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_session_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_sn_auth_session_sn_account_account_id",
                        column: x => x.account_id,
                        principalTable: "sn_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "sn_e2ee_one_time_pre_key",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_bundle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_claimed = table.Column<bool>(type: "boolean", nullable: false),
                    key_id = table.Column<int>(type: "integer", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_e2ee_one_time_pre_key", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_e2ee_one_time_pre_key_sn_e2ee_key_bundle_key_bundle_id",
                        column: x => x.key_bundle_id,
                        principalTable: "sn_e2ee_key_bundle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_badges_sn_account_id",
                table: "badges",
                column: "sn_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_relationships_related_id",
                table: "account_relationships",
                column: "related_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_account_name",
                table: "sn_account",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_account_auth_factor_account_id",
                table: "sn_account_auth_factor",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_account_connection_account_id",
                table: "sn_account_connection",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_account_contact_account_id",
                table: "sn_account_contact",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_challenge_account_id",
                table: "sn_auth_challenge",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_client_account_id",
                table: "sn_auth_client",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_session_account_id",
                table: "sn_auth_session",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_session_client_id",
                table: "sn_auth_session",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_auth_session_parent_session_id",
                table: "sn_auth_session",
                column: "parent_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_device_account_id_device_id",
                table: "sn_e2ee_device",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_d",
                table: "sn_e2ee_envelope",
                columns: new[] { "recipient_account_id", "recipient_device_id", "delivery_status", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_s",
                table: "sn_e2ee_envelope",
                columns: new[] { "recipient_account_id", "recipient_device_id", "sender_id", "sender_device_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_envelope_session_id",
                table: "sn_e2ee_envelope",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_key_bundle_account_id_device_id",
                table: "sn_e2ee_key_bundle",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_one_time_pre_key_account_id_device_id_is_claimed",
                table: "sn_e2ee_one_time_pre_key",
                columns: new[] { "account_id", "device_id", "is_claimed" });

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_is_claimed",
                table: "sn_e2ee_one_time_pre_key",
                columns: new[] { "key_bundle_id", "is_claimed" });

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_key_id",
                table: "sn_e2ee_one_time_pre_key",
                columns: new[] { "key_bundle_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_e2ee_session_account_a_id_account_b_id",
                table: "sn_e2ee_session",
                columns: new[] { "account_a_id", "account_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_mls_device_membership_chat_room_id_account_id_device_id",
                table: "sn_mls_device_membership",
                columns: new[] { "chat_room_id", "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_mls_device_membership_mls_group_id_last_seen_epoch",
                table: "sn_mls_device_membership",
                columns: new[] { "mls_group_id", "last_seen_epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_sn_mls_group_state_chat_room_id",
                table: "sn_mls_group_state",
                column: "chat_room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sn_mls_group_state_mls_group_id_epoch",
                table: "sn_mls_group_state",
                columns: new[] { "mls_group_id", "epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_sn_mls_key_package_account_id_device_id_is_consumed",
                table: "sn_mls_key_package",
                columns: new[] { "account_id", "device_id", "is_consumed" });

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_sn_account_account_id",
                table: "account_profiles",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_relationships_sn_account_account_id",
                table: "account_relationships",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_relationships_sn_account_related_id",
                table: "account_relationships",
                column: "related_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_badges_sn_account_sn_account_id",
                table: "badges",
                column: "sn_account_id",
                principalTable: "sn_account",
                principalColumn: "id");
        }
    }
}
