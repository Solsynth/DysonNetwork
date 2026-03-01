using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddE2eeDeviceScopedV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_e2ee_key_bundles_account_id",
                table: "e2ee_key_bundles");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_id_delivery_status_sequence",
                table: "e2ee_envelopes");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_id_sender_id_client_message_id",
                table: "e2ee_envelopes");

            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "e2ee_one_time_pre_keys",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "device_id",
                table: "e2ee_one_time_pre_keys",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "device_id",
                table: "e2ee_key_bundles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "legacy_account_scoped",
                table: "e2ee_envelopes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_account_id",
                table: "e2ee_envelopes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "recipient_device_id",
                table: "e2ee_envelopes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sender_device_id",
                table: "e2ee_envelopes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

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

            // Backfill existing v1/account-scoped data into legacy device scope.
            migrationBuilder.Sql("""
                UPDATE e2ee_key_bundles
                SET device_id = 'legacy-account'
                WHERE device_id = '';
            """);
            migrationBuilder.Sql("""
                UPDATE e2ee_one_time_pre_keys otp
                SET account_id = kb.account_id,
                    device_id = kb.device_id
                FROM e2ee_key_bundles kb
                WHERE otp.key_bundle_id = kb.id;
            """);
            migrationBuilder.Sql("""
                UPDATE e2ee_envelopes
                SET recipient_account_id = recipient_id,
                    legacy_account_scoped = TRUE,
                    sender_device_id = 'legacy-account'
                WHERE recipient_account_id = '00000000-0000-0000-0000-000000000000';
            """);
            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_account_id_device_id_is_claimed",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "account_id", "device_id", "is_claimed" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "e2ee_key_bundles",
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
                name: "ix_e2ee_devices_account_id_device_id",
                table: "e2ee_devices",
                columns: new[] { "account_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e2ee_devices");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_one_time_pre_keys_account_id_device_id_is_claimed",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "e2ee_key_bundles");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_del",
                table: "e2ee_envelopes");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "e2ee_envelopes");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "e2ee_key_bundles");

            migrationBuilder.DropColumn(
                name: "legacy_account_scoped",
                table: "e2ee_envelopes");

            migrationBuilder.DropColumn(
                name: "recipient_account_id",
                table: "e2ee_envelopes");

            migrationBuilder.DropColumn(
                name: "recipient_device_id",
                table: "e2ee_envelopes");

            migrationBuilder.DropColumn(
                name: "sender_device_id",
                table: "e2ee_envelopes");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id",
                table: "e2ee_key_bundles",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_id_delivery_status_sequence",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "delivery_status", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_id_sender_id_client_message_id",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "sender_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");
        }
    }
}
