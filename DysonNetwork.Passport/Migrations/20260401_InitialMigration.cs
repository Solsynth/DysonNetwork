using System;
using System.Collections.Generic;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "account_achievements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_count = table.Column<int>(type: "integer", nullable: false),
                    current_streak = table.Column<int>(type: "integer", nullable: false),
                    best_streak = table.Column<int>(type: "integer", nullable: false),
                    last_progress_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_achievements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_check_in_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    reward_points = table.Column<decimal>(type: "numeric", nullable: true),
                    reward_experience = table.Column<int>(type: "integer", nullable: true),
                    tips = table.Column<List<CheckInFortuneTip>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    backdated_from = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_check_in_results", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "account_quest_progresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    progress_count = table.Column<int>(type: "integer", nullable: false),
                    repeat_iteration_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_quest_progresses", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "account_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    symbol = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
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
                });

            migrationBuilder.CreateTable(
                name: "achievement_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_order = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_seed_managed = table.Column<bool>(type: "boolean", nullable: false),
                    is_progress_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    available_from = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    available_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    target_count = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<SnProgressTriggerDefinition>(type: "jsonb", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_achievement_definitions", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "meets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    location_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    location_address = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    image = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    location = table.Column<Geometry>(type: "geometry (Geometry,4326)", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nearby_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    friend_only = table.Column<bool>(type: "boolean", nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    last_heartbeat_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_token_issued_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nearby_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nfc_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    locked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    sun_key = table.Column<byte[]>(type: "bytea", nullable: true),
                    counter = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nfc_tags", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "progress_event_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    definition_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    period_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_event_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_reward_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    definition_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reward_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    period_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    badge_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    experience_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    source_points_granted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    notification_sent_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_reward_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quest_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    series_order = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_seed_managed = table.Column<bool>(type: "boolean", nullable: false),
                    is_progress_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    available_from = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    available_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    target_count = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<SnProgressTriggerDefinition>(type: "jsonb", nullable: false),
                    schedule = table.Column<SnQuestScheduleConfig>(type: "jsonb", nullable: false),
                    reward = table.Column<SnProgressRewardDefinition>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realm_boost_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_boost_contributions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realm_experience_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_experience_records", x => x.id);
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
                    boost_points = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rewind_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    sharable_code = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    data = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewind_points", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
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
                name: "meet_participants",
                columns: table => new
                {
                    meet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meet_participants", x => new { x.meet_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_meet_participants_meets_meet_id",
                        column: x => x.meet_id,
                        principalTable: "meets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nearby_presence_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    valid_from = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    friend_only = table.Column<bool>(type: "boolean", nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nearby_presence_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_nearby_presence_tokens_nearby_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "nearby_devices",
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
                name: "realm_labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    color = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_labels", x => x.id);
                    table.ForeignKey(
                        name: "fk_realm_labels_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    files = table.Column<List<SnCloudFileReferenceObject>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_messages_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "realm_members",
                columns: table => new
                {
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nick = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    label_id = table.Column<Guid>(type: "uuid", nullable: true),
                    experience = table.Column<int>(type: "integer", nullable: false),
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
                        name: "fk_realm_members_realm_labels_label_id",
                        column: x => x.label_id,
                        principalTable: "realm_labels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_realm_members_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id",
                table: "account_achievements",
                columns: new[] { "account_id", "achievement_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses",
                columns: new[] { "account_id", "quest_definition_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_definitions_identifier",
                table: "achievement_definitions",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_results_spell_id",
                table: "affiliation_results",
                column: "spell_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_spell",
                table: "affiliation_spells",
                column: "spell",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_spell",
                table: "magic_spells",
                column: "spell",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meets_host_id_status",
                table: "meets",
                columns: new[] { "host_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_nearby_devices_user_id_device_id",
                table: "nearby_devices",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_device_id_slot",
                table: "nearby_presence_tokens",
                columns: new[] { "device_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_token_hash",
                table: "nearby_presence_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_nfc_tags_uid",
                table: "nfc_tags",
                column: "uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nfc_tags_user_id",
                table: "nfc_tags",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_group_id",
                table: "permission_nodes",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_actor",
                table: "permission_nodes",
                columns: new[] { "key", "actor" });

            migrationBuilder.CreateIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts",
                columns: new[] { "event_id", "definition_type", "definition_identifier", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_reward_grants_reward_token",
                table: "progress_reward_grants",
                column: "reward_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quest_definitions_identifier",
                table: "quest_definitions",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_realm_labels_realm_id",
                table: "realm_labels",
                column: "realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_realm_members_label_id",
                table: "realm_members",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "ix_realms_slug",
                table: "realms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_messages_ticket_id",
                table: "ticket_messages",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_achievements");

            migrationBuilder.DropTable(
                name: "account_check_in_results");

            migrationBuilder.DropTable(
                name: "account_profiles");

            migrationBuilder.DropTable(
                name: "account_quest_progresses");

            migrationBuilder.DropTable(
                name: "account_relationships");

            migrationBuilder.DropTable(
                name: "account_statuses");

            migrationBuilder.DropTable(
                name: "achievement_definitions");

            migrationBuilder.DropTable(
                name: "affiliation_results");

            migrationBuilder.DropTable(
                name: "badges");

            migrationBuilder.DropTable(
                name: "experience_records");

            migrationBuilder.DropTable(
                name: "magic_spells");

            migrationBuilder.DropTable(
                name: "meet_participants");

            migrationBuilder.DropTable(
                name: "nearby_presence_tokens");

            migrationBuilder.DropTable(
                name: "nfc_tags");

            migrationBuilder.DropTable(
                name: "permission_group_members");

            migrationBuilder.DropTable(
                name: "permission_nodes");

            migrationBuilder.DropTable(
                name: "presence_activities");

            migrationBuilder.DropTable(
                name: "progress_event_receipts");

            migrationBuilder.DropTable(
                name: "progress_reward_grants");

            migrationBuilder.DropTable(
                name: "quest_definitions");

            migrationBuilder.DropTable(
                name: "realm_boost_contributions");

            migrationBuilder.DropTable(
                name: "realm_experience_records");

            migrationBuilder.DropTable(
                name: "realm_members");

            migrationBuilder.DropTable(
                name: "rewind_points");

            migrationBuilder.DropTable(
                name: "social_credit_records");

            migrationBuilder.DropTable(
                name: "ticket_messages");

            migrationBuilder.DropTable(
                name: "affiliation_spells");

            migrationBuilder.DropTable(
                name: "meets");

            migrationBuilder.DropTable(
                name: "nearby_devices");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "realm_labels");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropTable(
                name: "realms");
        }
    }
}
