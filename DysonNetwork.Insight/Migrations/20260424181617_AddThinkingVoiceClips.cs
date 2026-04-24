using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddThinkingVoiceClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "thinking_voice_clips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    access_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thinking_voice_clips", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_thinking_voice_clips_account_id_created_at",
                table: "thinking_voice_clips",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_thinking_voice_clips_expires_at",
                table: "thinking_voice_clips",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thinking_voice_clips");
        }
    }
}
