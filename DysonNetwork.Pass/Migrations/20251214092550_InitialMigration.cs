using System;
using System.Collections.Generic;
using System.Text.Json;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
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
                name: "lottery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    winning_region_one_numbers = table.Column<List<int>>(type: "jsonb", nullable: false),
                    winning_region_two_number = table.Column<int>(type: "integer", nullable: false),
                    total_tickets = table.Column<int>(type: "integer", nullable: false),
                    total_prizes_awarded = table.Column<int>(type: "integer", nullable: false),
                    total_prize_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lottery_records", x => x.id);
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
                name: "realms",
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
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    code = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    discount_rate = table.Column<double>(type: "double precision", nullable: true),
                    max_usage = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "abuse_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    resolved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abuse_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_abuse_reports_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "account_check_in_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    reward_points = table.Column<decimal>(type: "numeric", nullable: true),
                    reward_experience = table.Column<int>(type: "integer", nullable: true),
                    tips = table.Column<ICollection<CheckInFortuneTip>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    backdated_from = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_check_in_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_check_in_results_accounts_account_id",
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
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
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
                name: "account_profiles",
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
                    table.PrimaryKey("pk_account_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_profiles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_relationships",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_relationships", x => new { x.account_id, x.related_id });
                    table.ForeignKey(
                        name: "fk_account_relationships_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_account_relationships_accounts_related_id",
                        column: x => x.related_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    is_invisible = table.Column<bool>(type: "boolean", nullable: false),
                    is_not_disturb = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("pk_account_statuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_statuses_accounts_account_id",
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
                name: "affiliation_spells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spell = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affiliation_spells", x => x.id);
                    table.ForeignKey(
                        name: "fk_affiliation_spells_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
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
                    blacklist_factors = table.Column<List<Guid>>(type: "jsonb", nullable: false),
                    audiences = table.Column<List<string>>(type: "jsonb", nullable: false),
                    scopes = table.Column<List<string>>(type: "jsonb", nullable: false),
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
                name: "badges",
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
                    table.PrimaryKey("pk_badges", x => x.id);
                    table.ForeignKey(
                        name: "fk_badges_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experience_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    bonus_multiplier = table.Column<double>(type: "double precision", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_experience_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_experience_records_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lotteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_one_numbers = table.Column<List<int>>(type: "jsonb", nullable: false),
                    region_two_number = table.Column<int>(type: "integer", nullable: false),
                    multiplier = table.Column<int>(type: "integer", nullable: false),
                    draw_status = table.Column<int>(type: "integer", nullable: false),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    matched_region_one_numbers = table.Column<List<int>>(type: "jsonb", nullable: true),
                    matched_region_two_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotteries", x => x.id);
                    table.ForeignKey(
                        name: "fk_lotteries_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "magic_spells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spell = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_magic_spells", x => x.id);
                    table.ForeignKey(
                        name: "fk_magic_spells_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "presence_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    manual_id = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    title = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    subtitle = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    caption = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    large_image = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    small_image = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    title_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    subtitle_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    lease_minutes = table.Column<int>(type: "integer", nullable: false),
                    lease_expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_presence_activities_accounts_account_id",
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
                    blocked_permissions = table.Column<List<string>>(type: "jsonb", nullable: true),
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
                name: "social_credit_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    delta = table.Column<double>(type: "double precision", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_credit_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_social_credit_records_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    amount_of_splits = table.Column<int>(type: "integer", nullable: false),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_funds", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_funds_accounts_creator_account_id",
                        column: x => x.creator_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallets_accounts_account_id",
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
                name: "realm_members",
                columns: table => new
                {
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    leave_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_members", x => new { x.realm_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_realm_members_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    renewal_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_subscriptions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_subscriptions_wallet_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "wallet_coupons",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "affiliation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_identifier = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    spell_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affiliation_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_affiliation_results_affiliation_spells_spell_id",
                        column: x => x.spell_id,
                        principalTable: "affiliation_spells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    last_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    audiences = table.Column<List<string>>(type: "jsonb", nullable: false),
                    scopes = table.Column<List<string>>(type: "jsonb", nullable: false),
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
                name: "wallet_fund_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    is_received = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_fund_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_fund_recipients_accounts_recipient_account_id",
                        column: x => x.recipient_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    payer_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                    gifter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gift_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    subscription_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    redeemed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    redeemer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    is_open_gift = table.Column<bool>(type: "boolean", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    payment_details = table.Column<SnPaymentDetails>(type: "jsonb", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_gifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_gifter_id",
                        column: x => x.gifter_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_wallet_gifts_accounts_redeemer_id",
                        column: x => x.redeemer_id,
                        principalTable: "accounts",
                        principalColumn: "id");
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
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    app_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    product_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    payee_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
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
                name: "ix_abuse_reports_account_id",
                table: "abuse_reports",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_factors_account_id",
                table: "account_auth_factors",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_check_in_results_account_id",
                table: "account_check_in_results",
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
                name: "ix_account_profiles_account_id",
                table: "account_profiles",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_relationships_related_id",
                table: "account_relationships",
                column: "related_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_statuses_account_id",
                table: "account_statuses",
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
                name: "ix_affiliation_results_spell_id",
                table: "affiliation_results",
                column: "spell_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_account_id",
                table: "affiliation_spells",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_spell",
                table: "affiliation_spells",
                column: "spell",
                unique: true);

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
                name: "ix_auth_clients_account_id",
                table: "auth_clients",
                column: "account_id");

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
                name: "ix_badges_account_id",
                table: "badges",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_experience_records_account_id",
                table: "experience_records",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_lotteries_account_id",
                table: "lotteries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_account_id",
                table: "magic_spells",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_spell",
                table: "magic_spells",
                column: "spell",
                unique: true);

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
                name: "ix_permission_nodes_group_id",
                table: "permission_nodes",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_actor",
                table: "permission_nodes",
                columns: new[] { "key", "actor" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_activities_account_id",
                table: "presence_activities",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_punishments_account_id",
                table: "punishments",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_realms_slug",
                table: "realms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_social_credit_records_account_id",
                table: "social_credit_records",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_fund_id",
                table: "wallet_fund_recipients",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_fund_recipients_recipient_account_id",
                table: "wallet_fund_recipients",
                column: "recipient_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_funds_creator_account_id",
                table: "wallet_funds",
                column: "creator_account_id");

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
                name: "ix_wallet_gifts_redeemer_id",
                table: "wallet_gifts",
                column: "redeemer_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_wallets_account_id",
                table: "wallets",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abuse_reports");

            migrationBuilder.DropTable(
                name: "account_auth_factors");

            migrationBuilder.DropTable(
                name: "account_check_in_results");

            migrationBuilder.DropTable(
                name: "account_connections");

            migrationBuilder.DropTable(
                name: "account_contacts");

            migrationBuilder.DropTable(
                name: "account_profiles");

            migrationBuilder.DropTable(
                name: "account_relationships");

            migrationBuilder.DropTable(
                name: "account_statuses");

            migrationBuilder.DropTable(
                name: "action_logs");

            migrationBuilder.DropTable(
                name: "affiliation_results");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "auth_challenges");

            migrationBuilder.DropTable(
                name: "badges");

            migrationBuilder.DropTable(
                name: "experience_records");

            migrationBuilder.DropTable(
                name: "lotteries");

            migrationBuilder.DropTable(
                name: "lottery_records");

            migrationBuilder.DropTable(
                name: "magic_spells");

            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "permission_group_members");

            migrationBuilder.DropTable(
                name: "permission_nodes");

            migrationBuilder.DropTable(
                name: "presence_activities");

            migrationBuilder.DropTable(
                name: "punishments");

            migrationBuilder.DropTable(
                name: "realm_members");

            migrationBuilder.DropTable(
                name: "social_credit_records");

            migrationBuilder.DropTable(
                name: "wallet_fund_recipients");

            migrationBuilder.DropTable(
                name: "wallet_gifts");

            migrationBuilder.DropTable(
                name: "wallet_pockets");

            migrationBuilder.DropTable(
                name: "affiliation_spells");

            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "realms");

            migrationBuilder.DropTable(
                name: "wallet_funds");

            migrationBuilder.DropTable(
                name: "wallet_subscriptions");

            migrationBuilder.DropTable(
                name: "auth_clients");

            migrationBuilder.DropTable(
                name: "wallets");

            migrationBuilder.DropTable(
                name: "wallet_coupons");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
