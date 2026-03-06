using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class MigrateE2eeLogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "ix_mls_device_memberships_chat_room_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "chat_room_id", "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_last_seen_epoch",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "last_seen_epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_chat_room_id",
                table: "mls_group_states",
                column: "chat_room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_mls_group_id_epoch",
                table: "mls_group_states",
                columns: new[] { "mls_group_id", "epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_key_packages_account_id_device_id_is_consumed",
                table: "mls_key_packages",
                columns: new[] { "account_id", "device_id", "is_consumed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "e2ee_key_bundles");
        }
    }
}
