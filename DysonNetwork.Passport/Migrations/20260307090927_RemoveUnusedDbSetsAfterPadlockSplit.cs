using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedDbSetsAfterPadlockSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_e2ee_devices_accounts_account_id",
                table: "e2ee_devices");

            migrationBuilder.DropForeignKey(
                name: "fk_e2ee_key_bundles_accounts_account_id",
                table: "e2ee_key_bundles");

            migrationBuilder.DropForeignKey(
                name: "fk_e2ee_one_time_pre_keys_e2ee_key_bundles_key_bundle_id",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropForeignKey(
                name: "fk_mls_key_packages_accounts_account_id",
                table: "mls_key_packages");

            migrationBuilder.DropTable(
                name: "lottery_records");

            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "wallet_fund_recipients");

            migrationBuilder.DropTable(
                name: "wallet_gifts");

            migrationBuilder.DropTable(
                name: "wallet_pockets");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "wallet_funds");

            migrationBuilder.DropTable(
                name: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "wallets");

            migrationBuilder.DropTable(
                name: "wallet_coupons");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mls_key_packages",
                table: "mls_key_packages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mls_group_states",
                table: "mls_group_states");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mls_device_memberships",
                table: "mls_device_memberships");

            migrationBuilder.DropPrimaryKey(
                name: "pk_e2ee_sessions",
                table: "e2ee_sessions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_e2ee_one_time_pre_keys",
                table: "e2ee_one_time_pre_keys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_e2ee_key_bundles",
                table: "e2ee_key_bundles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_e2ee_envelopes",
                table: "e2ee_envelopes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_e2ee_devices",
                table: "e2ee_devices");

            migrationBuilder.RenameTable(
                name: "mls_key_packages",
                newName: "sn_mls_key_package");

            migrationBuilder.RenameTable(
                name: "mls_group_states",
                newName: "sn_mls_group_state");

            migrationBuilder.RenameTable(
                name: "mls_device_memberships",
                newName: "sn_mls_device_membership");

            migrationBuilder.RenameTable(
                name: "e2ee_sessions",
                newName: "sn_e2ee_session");

            migrationBuilder.RenameTable(
                name: "e2ee_one_time_pre_keys",
                newName: "sn_e2ee_one_time_pre_key");

            migrationBuilder.RenameTable(
                name: "e2ee_key_bundles",
                newName: "sn_e2ee_key_bundle");

            migrationBuilder.RenameTable(
                name: "e2ee_envelopes",
                newName: "sn_e2ee_envelope");

            migrationBuilder.RenameTable(
                name: "e2ee_devices",
                newName: "sn_e2ee_device");

            migrationBuilder.RenameIndex(
                name: "ix_mls_key_packages_account_id_device_id_is_consumed",
                table: "sn_mls_key_package",
                newName: "ix_sn_mls_key_package_account_id_device_id_is_consumed");

            migrationBuilder.RenameIndex(
                name: "ix_mls_group_states_mls_group_id_epoch",
                table: "sn_mls_group_state",
                newName: "ix_sn_mls_group_state_mls_group_id_epoch");

            migrationBuilder.RenameIndex(
                name: "ix_mls_group_states_chat_room_id",
                table: "sn_mls_group_state",
                newName: "ix_sn_mls_group_state_chat_room_id");

            migrationBuilder.RenameIndex(
                name: "ix_mls_device_memberships_mls_group_id_last_seen_epoch",
                table: "sn_mls_device_membership",
                newName: "ix_sn_mls_device_membership_mls_group_id_last_seen_epoch");

            migrationBuilder.RenameIndex(
                name: "ix_mls_device_memberships_chat_room_id_account_id_device_id",
                table: "sn_mls_device_membership",
                newName: "ix_sn_mls_device_membership_chat_room_id_account_id_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_sessions_account_a_id_account_b_id",
                table: "sn_e2ee_session",
                newName: "ix_sn_e2ee_session_account_a_id_account_b_id");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id",
                table: "sn_e2ee_one_time_pre_key",
                newName: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_key_id");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_one_time_pre_keys_key_bundle_id_is_claimed",
                table: "sn_e2ee_one_time_pre_key",
                newName: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_is_claimed");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_one_time_pre_keys_account_id_device_id_is_claimed",
                table: "sn_e2ee_one_time_pre_key",
                newName: "ix_sn_e2ee_one_time_pre_key_account_id_device_id_is_claimed");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_key_bundles_account_id_device_id",
                table: "sn_e2ee_key_bundle",
                newName: "ix_sn_e2ee_key_bundle_account_id_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_envelopes_session_id",
                table: "sn_e2ee_envelope",
                newName: "ix_sn_e2ee_envelope_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen",
                table: "sn_e2ee_envelope",
                newName: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_s");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_del",
                table: "sn_e2ee_envelope",
                newName: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_d");

            migrationBuilder.RenameIndex(
                name: "ix_e2ee_devices_account_id_device_id",
                table: "sn_e2ee_device",
                newName: "ix_sn_e2ee_device_account_id_device_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_mls_key_package",
                table: "sn_mls_key_package",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_mls_group_state",
                table: "sn_mls_group_state",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_mls_device_membership",
                table: "sn_mls_device_membership",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_e2ee_session",
                table: "sn_e2ee_session",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_e2ee_one_time_pre_key",
                table: "sn_e2ee_one_time_pre_key",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_e2ee_key_bundle",
                table: "sn_e2ee_key_bundle",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_e2ee_envelope",
                table: "sn_e2ee_envelope",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_e2ee_device",
                table: "sn_e2ee_device",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sn_e2ee_device_accounts_account_id",
                table: "sn_e2ee_device",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_e2ee_key_bundle_accounts_account_id",
                table: "sn_e2ee_key_bundle",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_e2ee_one_time_pre_key_sn_e2ee_key_bundle_key_bundle_id",
                table: "sn_e2ee_one_time_pre_key",
                column: "key_bundle_id",
                principalTable: "sn_e2ee_key_bundle",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_mls_key_package_accounts_account_id",
                table: "sn_mls_key_package",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_device_accounts_account_id",
                table: "sn_e2ee_device");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_key_bundle_accounts_account_id",
                table: "sn_e2ee_key_bundle");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_one_time_pre_key_sn_e2ee_key_bundle_key_bundle_id",
                table: "sn_e2ee_one_time_pre_key");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_mls_key_package_accounts_account_id",
                table: "sn_mls_key_package");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_mls_key_package",
                table: "sn_mls_key_package");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_mls_group_state",
                table: "sn_mls_group_state");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_mls_device_membership",
                table: "sn_mls_device_membership");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_e2ee_session",
                table: "sn_e2ee_session");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_e2ee_one_time_pre_key",
                table: "sn_e2ee_one_time_pre_key");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_e2ee_key_bundle",
                table: "sn_e2ee_key_bundle");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_e2ee_envelope",
                table: "sn_e2ee_envelope");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_e2ee_device",
                table: "sn_e2ee_device");

            migrationBuilder.RenameTable(
                name: "sn_mls_key_package",
                newName: "mls_key_packages");

            migrationBuilder.RenameTable(
                name: "sn_mls_group_state",
                newName: "mls_group_states");

            migrationBuilder.RenameTable(
                name: "sn_mls_device_membership",
                newName: "mls_device_memberships");

            migrationBuilder.RenameTable(
                name: "sn_e2ee_session",
                newName: "e2ee_sessions");

            migrationBuilder.RenameTable(
                name: "sn_e2ee_one_time_pre_key",
                newName: "e2ee_one_time_pre_keys");

            migrationBuilder.RenameTable(
                name: "sn_e2ee_key_bundle",
                newName: "e2ee_key_bundles");

            migrationBuilder.RenameTable(
                name: "sn_e2ee_envelope",
                newName: "e2ee_envelopes");

            migrationBuilder.RenameTable(
                name: "sn_e2ee_device",
                newName: "e2ee_devices");

            migrationBuilder.RenameIndex(
                name: "ix_sn_mls_key_package_account_id_device_id_is_consumed",
                table: "mls_key_packages",
                newName: "ix_mls_key_packages_account_id_device_id_is_consumed");

            migrationBuilder.RenameIndex(
                name: "ix_sn_mls_group_state_mls_group_id_epoch",
                table: "mls_group_states",
                newName: "ix_mls_group_states_mls_group_id_epoch");

            migrationBuilder.RenameIndex(
                name: "ix_sn_mls_group_state_chat_room_id",
                table: "mls_group_states",
                newName: "ix_mls_group_states_chat_room_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_mls_device_membership_mls_group_id_last_seen_epoch",
                table: "mls_device_memberships",
                newName: "ix_mls_device_memberships_mls_group_id_last_seen_epoch");

            migrationBuilder.RenameIndex(
                name: "ix_sn_mls_device_membership_chat_room_id_account_id_device_id",
                table: "mls_device_memberships",
                newName: "ix_mls_device_memberships_chat_room_id_account_id_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_session_account_a_id_account_b_id",
                table: "e2ee_sessions",
                newName: "ix_e2ee_sessions_account_a_id_account_b_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_key_id",
                table: "e2ee_one_time_pre_keys",
                newName: "ix_e2ee_one_time_pre_keys_key_bundle_id_key_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_one_time_pre_key_key_bundle_id_is_claimed",
                table: "e2ee_one_time_pre_keys",
                newName: "ix_e2ee_one_time_pre_keys_key_bundle_id_is_claimed");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_one_time_pre_key_account_id_device_id_is_claimed",
                table: "e2ee_one_time_pre_keys",
                newName: "ix_e2ee_one_time_pre_keys_account_id_device_id_is_claimed");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_key_bundle_account_id_device_id",
                table: "e2ee_key_bundles",
                newName: "ix_e2ee_key_bundles_account_id_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_envelope_session_id",
                table: "e2ee_envelopes",
                newName: "ix_e2ee_envelopes_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_s",
                table: "e2ee_envelopes",
                newName: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_sen");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_envelope_recipient_account_id_recipient_device_id_d",
                table: "e2ee_envelopes",
                newName: "ix_e2ee_envelopes_recipient_account_id_recipient_device_id_del");

            migrationBuilder.RenameIndex(
                name: "ix_sn_e2ee_device_account_id_device_id",
                table: "e2ee_devices",
                newName: "ix_e2ee_devices_account_id_device_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mls_key_packages",
                table: "mls_key_packages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mls_group_states",
                table: "mls_group_states",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mls_device_memberships",
                table: "mls_device_memberships",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_e2ee_sessions",
                table: "e2ee_sessions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_e2ee_one_time_pre_keys",
                table: "e2ee_one_time_pre_keys",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_e2ee_key_bundles",
                table: "e2ee_key_bundles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_e2ee_envelopes",
                table: "e2ee_envelopes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_e2ee_devices",
                table: "e2ee_devices",
                column: "id");

            migrationBuilder.CreateTable(
                name: "lottery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    total_prize_amount = table.Column<long>(type: "bigint", nullable: false),
                    total_prizes_awarded = table.Column<int>(type: "integer", nullable: false),
                    total_tickets = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    winning_region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    winning_region_two_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lottery_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    code = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    discount_rate = table.Column<double>(type: "double precision", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    max_usage = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_of_splits = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    remaining_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    renewal_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_subscriptions_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "wallet_fund_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_received = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_fund_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_fund_recipients_wallet_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "wallet_funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payer_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_transactions_wallets_payee_wallet_id",
                        column: x => x.payee_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_transactions_wallets_payer_wallet_id",
                        column: x => x.payer_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "wallet_pockets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_pockets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_pockets_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_gifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric", nullable: false),
                    gift_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    gifter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_open_gift = table.Column<bool>(type: "boolean", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    redeemed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    redeemer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    subscription_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_gifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_gifts_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_gifts_wallet_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "wallet_subscriptions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    app_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    product_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_orders_payment_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_orders_wallets_payee_wallet_id",
                        column: x => x.payee_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_payee_wallet_id",
                table: "payment_orders",
                column: "payee_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_transaction_id",
                table: "payment_orders",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payee_wallet_id",
                table: "payment_transactions",
                column: "payee_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payer_wallet_id",
                table: "payment_transactions",
                column: "payer_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_fund_id",
                table: "wallet_fund_recipients",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_coupon_id",
                table: "wallet_gifts",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_gift_code",
                table: "wallet_gifts",
                column: "gift_code");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_gifter_id",
                table: "wallet_gifts",
                column: "gifter_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_recipient_id",
                table: "wallet_gifts",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_gifts_subscription_id",
                table: "wallet_gifts",
                column: "subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_pockets_wallet_id",
                table: "wallet_pockets",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id",
                table: "wallet_subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id_identifier",
                table: "wallet_subscriptions",
                columns: new[] { "account_id", "identifier" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_account_id_is_active",
                table: "wallet_subscriptions",
                columns: new[] { "account_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_coupon_id",
                table: "wallet_subscriptions",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_identifier",
                table: "wallet_subscriptions",
                column: "identifier");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_subscriptions_status",
                table: "wallet_subscriptions",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_e2ee_devices_accounts_account_id",
                table: "e2ee_devices",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_e2ee_key_bundles_accounts_account_id",
                table: "e2ee_key_bundles",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_e2ee_one_time_pre_keys_e2ee_key_bundles_key_bundle_id",
                table: "e2ee_one_time_pre_keys",
                column: "key_bundle_id",
                principalTable: "e2ee_key_bundles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mls_key_packages_accounts_account_id",
                table: "mls_key_packages",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
