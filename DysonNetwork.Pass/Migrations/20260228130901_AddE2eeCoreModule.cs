using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddE2eeCoreModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "e2ee_envelopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "e2ee_one_time_pre_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_bundle_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "ix_e2ee_envelopes_recipient_id_client_message_id",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_id_delivery_status_sequence",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "delivery_status", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_session_id",
                table: "e2ee_envelopes",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id",
                table: "e2ee_key_bundles",
                column: "account_id",
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e2ee_envelopes");

            migrationBuilder.DropTable(
                name: "e2ee_one_time_pre_keys");

            migrationBuilder.DropTable(
                name: "e2ee_sessions");

            migrationBuilder.DropTable(
                name: "e2ee_key_bundles");
        }
    }
}
