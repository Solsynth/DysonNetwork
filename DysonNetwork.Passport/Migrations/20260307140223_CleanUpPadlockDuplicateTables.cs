using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class CleanUpPadlockDuplicateTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_abuse_reports_accounts_account_id",
                table: "abuse_reports");

            migrationBuilder.DropForeignKey(
                name: "fk_account_auth_factors_accounts_account_id",
                table: "account_auth_factors");

            migrationBuilder.DropForeignKey(
                name: "fk_account_check_in_results_accounts_account_id",
                table: "account_check_in_results");

            migrationBuilder.DropForeignKey(
                name: "fk_account_connections_accounts_account_id",
                table: "account_connections");

            migrationBuilder.DropForeignKey(
                name: "fk_account_contacts_accounts_account_id",
                table: "account_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_accounts_account_id",
                table: "account_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_account_relationships_accounts_account_id",
                table: "account_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_account_relationships_accounts_related_id",
                table: "account_relationships");

            migrationBuilder.DropForeignKey(
                name: "fk_account_statuses_accounts_account_id",
                table: "account_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_affiliation_spells_accounts_account_id",
                table: "affiliation_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_challenges_accounts_account_id",
                table: "auth_challenges");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_clients_accounts_account_id",
                table: "auth_clients");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_accounts_account_id",
                table: "auth_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_auth_clients_client_id",
                table: "auth_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_auth_sessions_parent_session_id",
                table: "auth_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_badges_accounts_account_id",
                table: "badges");

            migrationBuilder.DropForeignKey(
                name: "fk_experience_records_accounts_account_id",
                table: "experience_records");

            migrationBuilder.DropForeignKey(
                name: "fk_magic_spells_accounts_account_id",
                table: "magic_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_activities_accounts_account_id",
                table: "presence_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_punishments_accounts_account_id",
                table: "punishments");

            migrationBuilder.DropForeignKey(
                name: "fk_rewind_points_accounts_account_id",
                table: "rewind_points");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_device_accounts_account_id",
                table: "sn_e2ee_device");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_key_bundle_accounts_account_id",
                table: "sn_e2ee_key_bundle");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_mls_key_package_accounts_account_id",
                table: "sn_mls_key_package");

            migrationBuilder.DropForeignKey(
                name: "fk_social_credit_records_accounts_account_id",
                table: "social_credit_records");

            migrationBuilder.DropForeignKey(
                name: "fk_ticket_messages_accounts_sender_id",
                table: "ticket_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_accounts_assignee_id",
                table: "tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_accounts_creator_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auth_sessions",
                table: "auth_sessions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auth_clients",
                table: "auth_clients");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auth_challenges",
                table: "auth_challenges");

            migrationBuilder.DropPrimaryKey(
                name: "pk_accounts",
                table: "accounts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_account_contacts",
                table: "account_contacts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_account_connections",
                table: "account_connections");

            migrationBuilder.DropPrimaryKey(
                name: "pk_account_auth_factors",
                table: "account_auth_factors");

            migrationBuilder.RenameTable(
                name: "auth_sessions",
                newName: "sn_auth_session");

            migrationBuilder.RenameTable(
                name: "auth_clients",
                newName: "sn_auth_client");

            migrationBuilder.RenameTable(
                name: "auth_challenges",
                newName: "sn_auth_challenge");

            migrationBuilder.RenameTable(
                name: "accounts",
                newName: "sn_account");

            migrationBuilder.RenameTable(
                name: "account_contacts",
                newName: "sn_account_contact");

            migrationBuilder.RenameTable(
                name: "account_connections",
                newName: "sn_account_connection");

            migrationBuilder.RenameTable(
                name: "account_auth_factors",
                newName: "sn_account_auth_factor");

            migrationBuilder.RenameIndex(
                name: "ix_auth_sessions_parent_session_id",
                table: "sn_auth_session",
                newName: "ix_sn_auth_session_parent_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_auth_sessions_client_id",
                table: "sn_auth_session",
                newName: "ix_sn_auth_session_client_id");

            migrationBuilder.RenameIndex(
                name: "ix_auth_sessions_account_id",
                table: "sn_auth_session",
                newName: "ix_sn_auth_session_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_auth_clients_account_id",
                table: "sn_auth_client",
                newName: "ix_sn_auth_client_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_auth_challenges_account_id",
                table: "sn_auth_challenge",
                newName: "ix_sn_auth_challenge_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_name",
                table: "sn_account",
                newName: "ix_sn_account_name");

            migrationBuilder.RenameIndex(
                name: "ix_account_contacts_account_id",
                table: "sn_account_contact",
                newName: "ix_sn_account_contact_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_account_connections_account_id",
                table: "sn_account_connection",
                newName: "ix_sn_account_connection_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_account_auth_factors_account_id",
                table: "sn_account_auth_factor",
                newName: "ix_sn_account_auth_factor_account_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_auth_session",
                table: "sn_auth_session",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_auth_client",
                table: "sn_auth_client",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_auth_challenge",
                table: "sn_auth_challenge",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_account",
                table: "sn_account",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_account_contact",
                table: "sn_account_contact",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_account_connection",
                table: "sn_account_connection",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sn_account_auth_factor",
                table: "sn_account_auth_factor",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_abuse_reports_sn_account_account_id",
                table: "abuse_reports",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_check_in_results_sn_account_account_id",
                table: "account_check_in_results",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

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
                name: "fk_account_statuses_sn_account_account_id",
                table: "account_statuses",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_affiliation_spells_sn_account_account_id",
                table: "affiliation_spells",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_badges_sn_account_account_id",
                table: "badges",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_experience_records_sn_account_account_id",
                table: "experience_records",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_magic_spells_sn_account_account_id",
                table: "magic_spells",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_activities_sn_account_account_id",
                table: "presence_activities",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_punishments_sn_account_account_id",
                table: "punishments",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rewind_points_sn_account_account_id",
                table: "rewind_points",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_account_auth_factor_sn_account_account_id",
                table: "sn_account_auth_factor",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_account_connection_sn_account_account_id",
                table: "sn_account_connection",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_account_contact_sn_account_account_id",
                table: "sn_account_contact",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_auth_challenge_sn_account_account_id",
                table: "sn_auth_challenge",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_auth_client_sn_account_account_id",
                table: "sn_auth_client",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_auth_session_sn_account_account_id",
                table: "sn_auth_session",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_auth_session_sn_auth_client_client_id",
                table: "sn_auth_session",
                column: "client_id",
                principalTable: "sn_auth_client",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sn_auth_session_sn_auth_session_parent_session_id",
                table: "sn_auth_session",
                column: "parent_session_id",
                principalTable: "sn_auth_session",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sn_e2ee_device_sn_account_account_id",
                table: "sn_e2ee_device",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_e2ee_key_bundle_sn_account_account_id",
                table: "sn_e2ee_key_bundle",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sn_mls_key_package_sn_account_account_id",
                table: "sn_mls_key_package",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_social_credit_records_sn_account_account_id",
                table: "social_credit_records",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_ticket_messages_sn_account_sender_id",
                table: "ticket_messages",
                column: "sender_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sn_account_assignee_id",
                table: "tickets",
                column: "assignee_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sn_account_creator_id",
                table: "tickets",
                column: "creator_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_abuse_reports_sn_account_account_id",
                table: "abuse_reports");

            migrationBuilder.DropForeignKey(
                name: "fk_account_check_in_results_sn_account_account_id",
                table: "account_check_in_results");

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
                name: "fk_account_statuses_sn_account_account_id",
                table: "account_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_affiliation_spells_sn_account_account_id",
                table: "affiliation_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_badges_sn_account_account_id",
                table: "badges");

            migrationBuilder.DropForeignKey(
                name: "fk_experience_records_sn_account_account_id",
                table: "experience_records");

            migrationBuilder.DropForeignKey(
                name: "fk_magic_spells_sn_account_account_id",
                table: "magic_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_activities_sn_account_account_id",
                table: "presence_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_punishments_sn_account_account_id",
                table: "punishments");

            migrationBuilder.DropForeignKey(
                name: "fk_rewind_points_sn_account_account_id",
                table: "rewind_points");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_account_auth_factor_sn_account_account_id",
                table: "sn_account_auth_factor");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_account_connection_sn_account_account_id",
                table: "sn_account_connection");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_account_contact_sn_account_account_id",
                table: "sn_account_contact");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_auth_challenge_sn_account_account_id",
                table: "sn_auth_challenge");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_auth_client_sn_account_account_id",
                table: "sn_auth_client");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_auth_session_sn_account_account_id",
                table: "sn_auth_session");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_auth_session_sn_auth_client_client_id",
                table: "sn_auth_session");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_auth_session_sn_auth_session_parent_session_id",
                table: "sn_auth_session");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_device_sn_account_account_id",
                table: "sn_e2ee_device");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_e2ee_key_bundle_sn_account_account_id",
                table: "sn_e2ee_key_bundle");

            migrationBuilder.DropForeignKey(
                name: "fk_sn_mls_key_package_sn_account_account_id",
                table: "sn_mls_key_package");

            migrationBuilder.DropForeignKey(
                name: "fk_social_credit_records_sn_account_account_id",
                table: "social_credit_records");

            migrationBuilder.DropForeignKey(
                name: "fk_ticket_messages_sn_account_sender_id",
                table: "ticket_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sn_account_assignee_id",
                table: "tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sn_account_creator_id",
                table: "tickets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_auth_session",
                table: "sn_auth_session");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_auth_client",
                table: "sn_auth_client");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_auth_challenge",
                table: "sn_auth_challenge");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_account_contact",
                table: "sn_account_contact");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_account_connection",
                table: "sn_account_connection");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_account_auth_factor",
                table: "sn_account_auth_factor");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sn_account",
                table: "sn_account");

            migrationBuilder.RenameTable(
                name: "sn_auth_session",
                newName: "auth_sessions");

            migrationBuilder.RenameTable(
                name: "sn_auth_client",
                newName: "auth_clients");

            migrationBuilder.RenameTable(
                name: "sn_auth_challenge",
                newName: "auth_challenges");

            migrationBuilder.RenameTable(
                name: "sn_account_contact",
                newName: "account_contacts");

            migrationBuilder.RenameTable(
                name: "sn_account_connection",
                newName: "account_connections");

            migrationBuilder.RenameTable(
                name: "sn_account_auth_factor",
                newName: "account_auth_factors");

            migrationBuilder.RenameTable(
                name: "sn_account",
                newName: "accounts");

            migrationBuilder.RenameIndex(
                name: "ix_sn_auth_session_parent_session_id",
                table: "auth_sessions",
                newName: "ix_auth_sessions_parent_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_auth_session_client_id",
                table: "auth_sessions",
                newName: "ix_auth_sessions_client_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_auth_session_account_id",
                table: "auth_sessions",
                newName: "ix_auth_sessions_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_auth_client_account_id",
                table: "auth_clients",
                newName: "ix_auth_clients_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_auth_challenge_account_id",
                table: "auth_challenges",
                newName: "ix_auth_challenges_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_account_contact_account_id",
                table: "account_contacts",
                newName: "ix_account_contacts_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_account_connection_account_id",
                table: "account_connections",
                newName: "ix_account_connections_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_account_auth_factor_account_id",
                table: "account_auth_factors",
                newName: "ix_account_auth_factors_account_id");

            migrationBuilder.RenameIndex(
                name: "ix_sn_account_name",
                table: "accounts",
                newName: "ix_accounts_name");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auth_sessions",
                table: "auth_sessions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auth_clients",
                table: "auth_clients",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auth_challenges",
                table: "auth_challenges",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_account_contacts",
                table: "account_contacts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_account_connections",
                table: "account_connections",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_account_auth_factors",
                table: "account_auth_factors",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_accounts",
                table: "accounts",
                column: "id");

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                name: "ix_api_keys_account_id",
                table: "api_keys",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_session_id",
                table: "api_keys",
                column: "session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_abuse_reports_accounts_account_id",
                table: "abuse_reports",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_auth_factors_accounts_account_id",
                table: "account_auth_factors",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_check_in_results_accounts_account_id",
                table: "account_check_in_results",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_connections_accounts_account_id",
                table: "account_connections",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_contacts_accounts_account_id",
                table: "account_contacts",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_accounts_account_id",
                table: "account_profiles",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_relationships_accounts_account_id",
                table: "account_relationships",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_relationships_accounts_related_id",
                table: "account_relationships",
                column: "related_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_statuses_accounts_account_id",
                table: "account_statuses",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_affiliation_spells_accounts_account_id",
                table: "affiliation_spells",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_challenges_accounts_account_id",
                table: "auth_challenges",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_auth_clients_accounts_account_id",
                table: "auth_clients",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_accounts_account_id",
                table: "auth_sessions",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_auth_clients_client_id",
                table: "auth_sessions",
                column: "client_id",
                principalTable: "auth_clients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_auth_sessions_parent_session_id",
                table: "auth_sessions",
                column: "parent_session_id",
                principalTable: "auth_sessions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_badges_accounts_account_id",
                table: "badges",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_experience_records_accounts_account_id",
                table: "experience_records",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_magic_spells_accounts_account_id",
                table: "magic_spells",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_activities_accounts_account_id",
                table: "presence_activities",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_punishments_accounts_account_id",
                table: "punishments",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rewind_points_accounts_account_id",
                table: "rewind_points",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

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
                name: "fk_sn_mls_key_package_accounts_account_id",
                table: "sn_mls_key_package",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_social_credit_records_accounts_account_id",
                table: "social_credit_records",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_ticket_messages_accounts_sender_id",
                table: "ticket_messages",
                column: "sender_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_accounts_assignee_id",
                table: "tickets",
                column: "assignee_id",
                principalTable: "accounts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_accounts_creator_id",
                table: "tickets",
                column: "creator_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
