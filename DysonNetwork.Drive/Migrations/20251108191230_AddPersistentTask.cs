using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    discriminator = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    chunk_size = table.Column<long>(type: "bigint", nullable: true),
                    chunks_count = table.Column<int>(type: "integer", nullable: true),
                    chunks_uploaded = table.Column<int>(type: "integer", nullable: true),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bundle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    encrypt_password = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    hash = table.Column<string>(type: "text", nullable: true),
                    uploaded_chunks = table.Column<List<int>>(type: "integer[]", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
