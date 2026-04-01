using System;
using System.Collections.Generic;
using System.Text.Json;
using DysonNetwork.Shared.Geometry;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nick = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    activated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_superuser = table.Column<bool>(type: "boolean", nullable: false),
                    automated_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_envelopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    client_message_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    header = table.Column<byte[]>(type: "bytea", nullable: true),
                    signature = table.Column<byte[]>(type: "bytea", nullable: true),
                    delivery_status = table.Column<int>(type: "integer", nullable: false),
                    delivered_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    acked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    legacy_account_scoped = table.Column<bool>(type: "boolean", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e2ee_envelopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    hint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e2ee_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mls_device_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    joined_epoch = table.Column<long>(type: "bigint", nullable: false),
                    last_seen_epoch = table.Column<long>(type: "bigint", nullable: true),
                    last_reshare_required_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reshare_completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_device_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mls_group_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    last_commit_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_group_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_auth_factors",
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
                    table.PrimaryKey("pk_account_auth_factors", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_auth_factors_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_connections",
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
                    table.PrimaryKey("pk_account_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_connections_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_contacts",
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
                    table.PrimaryKey("pk_account_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_contacts_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "action_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_action_logs_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_challenges",
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
                    table.PrimaryKey("pk_auth_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_challenges_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_clients",
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
                    table.PrimaryKey("pk_auth_clients", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_clients_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authorized_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    app_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_authorized_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorized_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_authorized_apps_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_bundle_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e2ee_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_e2ee_devices_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_key_bundles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    identity_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    signed_pre_key_id = table.Column<int>(type: "integer", nullable: true),
                    signed_pre_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    signed_pre_key_signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    signed_pre_key_expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e2ee_key_bundles", x => x.id);
                    table.ForeignKey(
                        name: "fk_e2ee_key_bundles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mls_key_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    key_package = table.Column<byte[]>(type: "bytea", nullable: false),
                    ciphersuite = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    consumed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    consumed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_key_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_mls_key_packages_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "punishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    blocked_permissions = table.Column<string>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punishments", x => x.id);
                    table.ForeignKey(
                        name: "fk_punishments_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permission_group_members",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_group_members", x => new { x.group_id, x.actor });
                    table.ForeignKey(
                        name: "fk_permission_group_members_permission_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permission_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    actor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    value = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_permission_nodes_permission_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "auth_sessions",
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
                    table.PrimaryKey("pk_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_auth_sessions_auth_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "auth_clients",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auth_sessions_auth_sessions_parent_session_id",
                        column: x => x.parent_session_id,
                        principalTable: "auth_sessions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "e2ee_one_time_pre_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_bundle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    key_id = table.Column<int>(type: "integer", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_claimed = table.Column<bool>(type: "boolean", nullable: false),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e2ee_one_time_pre_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_e2ee_one_time_pre_keys_e2ee_key_bundles_key_bundle_id",
                        column: x => x.key_bundle_id,
                        principalTable: "e2ee_key_bundles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_api_keys_auth_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "auth_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_factors_account_id",
                table: "account_auth_factors",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_connections_account_id",
                table: "account_connections",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_contacts_account_id",
                table: "account_contacts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_name",
                table: "accounts",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_action_logs_account_id",
                table: "action_logs",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_account_id",
                table: "api_keys",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_session_id",
                table: "api_keys",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_challenges_account_id",
                table: "auth_challenges",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id_device_id",
                table: "auth_clients",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_account_id",
                table: "auth_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_client_id",
                table: "auth_sessions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_parent_session_id",
                table: "auth_sessions",
                column: "parent_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_authorized_apps_account_id_app_id_type",
                table: "authorized_apps",
                columns: new[] { "account_id", "app_id", "type" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_devices_account_id_device_id",
                table: "e2ee_devices",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_del",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_account_id", "recipient_device_id", "delivery_status", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_account_id", "recipient_device_id", "sender_id", "sender_device_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_session_id",
                table: "e2ee_envelopes",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "e2ee_key_bundles",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_account_id_device_id_is_claimed",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "account_id", "device_id", "is_claimed" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_is_claimed",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "key_bundle_id", "is_claimed" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "key_bundle_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id",
                table: "e2ee_sessions",
                columns: new[] { "account_a_id", "account_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_last_seen_epoch",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "last_seen_epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_mls_group_id_epoch",
                table: "mls_group_states",
                columns: new[] { "mls_group_id", "epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_key_packages_account_id_device_id_is_consumed",
                table: "mls_key_packages",
                columns: new[] { "account_id", "device_id", "is_consumed" });

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_group_id",
                table: "permission_nodes",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_actor",
                table: "permission_nodes",
                columns: new[] { "key", "actor" });

            migrationBuilder.CreateIndex(
                name: "ix_punishments_account_id",
                table: "punishments",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_auth_factors");

            migrationBuilder.DropTable(
                name: "account_connections");

            migrationBuilder.DropTable(
                name: "account_contacts");

            migrationBuilder.DropTable(
                name: "action_logs");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "auth_challenges");

            migrationBuilder.DropTable(
                name: "authorized_apps");

            migrationBuilder.DropTable(
                name: "e2ee_devices");

            migrationBuilder.DropTable(
                name: "e2ee_envelopes");

            migrationBuilder.DropTable(
                name: "e2ee_one_time_pre_keys");

            migrationBuilder.DropTable(
                name: "e2ee_sessions");

            migrationBuilder.DropTable(
                name: "mls_device_memberships");

            migrationBuilder.DropTable(
                name: "mls_group_states");

            migrationBuilder.DropTable(
                name: "mls_key_packages");

            migrationBuilder.DropTable(
                name: "permission_group_members");

            migrationBuilder.DropTable(
                name: "permission_nodes");

            migrationBuilder.DropTable(
                name: "punishments");

            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropTable(
                name: "e2ee_key_bundles");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "auth_clients");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
