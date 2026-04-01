using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bundles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    passcode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bundles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_objects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    mime_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    has_compression = table.Column<bool>(type: "boolean", nullable: false),
                    has_thumbnail = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<string>(type: "text", nullable: false),
                    subject_type = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    permission = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    storage_config = table.Column<RemoteStorageConfig>(type: "jsonb", nullable: false),
                    billing_config = table.Column<BillingConfig>(type: "jsonb", nullable: false),
                    policy_config = table.Column<PolicyConfig>(type: "jsonb", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pools", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quota_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    quota = table.Column<long>(type: "bigint", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quota_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress = table.Column<double>(type: "double precision", nullable: false),
                    parameters = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    results = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    started_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_activity = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    estimated_duration_seconds = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    user_meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    sensitive_marks = table.Column<string>(type: "jsonb", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    uploaded_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    object_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    bundle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_marked_recycle = table.Column<bool>(type: "boolean", nullable: false),
                    storage_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    storage_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_files_bundles_bundle_id",
                        column: x => x.bundle_id,
                        principalTable: "bundles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_files_file_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "file_objects",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "file_replicas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_replicas", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_replicas_file_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "file_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_file_replicas_pools_pool_id",
                        column: x => x.pool_id,
                        principalTable: "pools",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "file_indexes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    file_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_indexes", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_indexes_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bundles_slug",
                table: "bundles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_indexes_file_id",
                table: "file_indexes",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_indexes_path_account_id",
                table: "file_indexes",
                columns: new[] { "path", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_file_replicas_object_id",
                table: "file_replicas",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_replicas_pool_id",
                table: "file_replicas",
                column: "pool_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_bundle_id",
                table: "files",
                column: "bundle_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_object_id",
                table: "files",
                column: "object_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_indexes");

            migrationBuilder.DropTable(
                name: "file_permissions");

            migrationBuilder.DropTable(
                name: "file_replicas");

            migrationBuilder.DropTable(
                name: "quota_records");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "pools");

            migrationBuilder.DropTable(
                name: "bundles");

            migrationBuilder.DropTable(
                name: "file_objects");
        }
    }
}
