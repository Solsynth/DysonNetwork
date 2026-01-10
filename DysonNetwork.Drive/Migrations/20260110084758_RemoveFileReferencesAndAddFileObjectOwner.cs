using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFileReferencesAndAddFileObjectOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_references");

            migrationBuilder.AddColumn<string>(
                name: "object_id",
                table: "files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "file_objects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "file_replicas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_object_id",
                table: "files",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_replicas_object_id",
                table: "file_replicas",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_replicas_pool_id",
                table: "file_replicas",
                column: "pool_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_file_objects_object_id",
                table: "files",
                column: "object_id",
                principalTable: "file_objects",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_file_objects_object_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "file_permissions");

            migrationBuilder.DropTable(
                name: "file_replicas");

            migrationBuilder.DropTable(
                name: "file_objects");

            migrationBuilder.DropIndex(
                name: "ix_files_object_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "object_id",
                table: "files");

            migrationBuilder.CreateTable(
                name: "file_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    resource_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    usage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_references", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_references_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_references_file_id",
                table: "file_references",
                column: "file_id");
        }
    }
}
