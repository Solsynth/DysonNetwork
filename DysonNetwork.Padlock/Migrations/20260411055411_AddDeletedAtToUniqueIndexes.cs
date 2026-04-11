using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id",
                table: "mls_device_memberships");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id",
                table: "e2ee_sessions");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "e2ee_key_bundles");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "e2ee_envelopes");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_devices_account_id_device_id",
                table: "e2ee_devices");

            migrationBuilder.DropIndex(
                name: "ix_auth_clients_account_id_device_id",
                table: "auth_clients");

            migrationBuilder.DropIndex(
                name: "ix_accounts_name",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id_de",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "account_id", "device_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id_deleted_at",
                table: "e2ee_sessions",
                columns: new[] { "account_a_id", "account_b_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id_deleted_at",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "key_bundle_id", "key_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id_deleted_at",
                table: "e2ee_key_bundles",
                columns: new[] { "account_id", "device_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_account_id", "recipient_device_id", "sender_id", "sender_device_id", "client_message_id", "deleted_at" },
                unique: true,
                filter: "client_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_devices_account_id_device_id_deleted_at",
                table: "e2ee_devices",
                columns: new[] { "account_id", "device_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id_device_id_deleted_at",
                table: "auth_clients",
                columns: new[] { "account_id", "device_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_name_deleted_at",
                table: "accounts",
                columns: new[] { "name", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id_de",
                table: "mls_device_memberships");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id_deleted_at",
                table: "e2ee_sessions");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id_deleted_at",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id_deleted_at",
                table: "e2ee_key_bundles");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "e2ee_envelopes");

            migrationBuilder.DropIndex(
                name: "ix_e2ee_devices_account_id_device_id_deleted_at",
                table: "e2ee_devices");

            migrationBuilder.DropIndex(
                name: "ix_auth_clients_account_id_device_id_deleted_at",
                table: "auth_clients");

            migrationBuilder.DropIndex(
                name: "ix_accounts_name_deleted_at",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id",
                table: "e2ee_sessions",
                columns: new[] { "account_a_id", "account_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id",
                table: "e2ee_one_time_pre_keys",
                columns: new[] { "key_bundle_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "e2ee_key_bundles",
                columns: new[] { "account_id", "device_id" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_auth_clients_account_id_device_id",
                table: "auth_clients",
                columns: new[] { "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_name",
                table: "accounts",
                column: "name",
                unique: true);
        }
    }
}
