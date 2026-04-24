using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeThinkingPartsAndSequenceSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "thinking_sequences",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "summary_embedding",
                table: "thinking_sequences",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "summary_last_at",
                table: "thinking_sequences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "thinking_thought_parts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thought_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    files = table.Column<List<SnCloudFileReferenceObject>>(type: "jsonb", nullable: true),
                    function_call = table.Column<SnFunctionCall>(type: "jsonb", nullable: true),
                    function_result = table.Column<SnFunctionResult>(type: "jsonb", nullable: true),
                    reasoning = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thinking_thought_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_thinking_thought_parts_thinking_sequences_sequence_id",
                        column: x => x.sequence_id,
                        principalTable: "thinking_sequences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_thinking_thought_parts_thinking_thoughts_thought_id",
                        column: x => x.thought_id,
                        principalTable: "thinking_thoughts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_thinking_sequences_summary_last_at",
                table: "thinking_sequences",
                column: "summary_last_at");

            migrationBuilder.CreateIndex(
                name: "ix_thinking_thought_parts_sequence_id_created_at",
                table: "thinking_thought_parts",
                columns: new[] { "sequence_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_thinking_thought_parts_thought_id_part_index",
                table: "thinking_thought_parts",
                columns: new[] { "thought_id", "part_index" });

            migrationBuilder.Sql(@"
                INSERT INTO thinking_thought_parts
                (id, thought_id, sequence_id, part_index, type, text, metadata, files, function_call, function_result, reasoning, created_at, updated_at, deleted_at)
                SELECT
                    gen_random_uuid(),
                    t.id,
                    t.sequence_id,
                    p.ordinality - 1,
                    CASE COALESCE(p.part ->> 'type', 'Text')
                        WHEN 'Text' THEN 0
                        WHEN 'FunctionCall' THEN 1
                        WHEN 'FunctionResult' THEN 2
                        WHEN 'Reasoning' THEN 3
                        ELSE 0
                    END,
                    p.part ->> 'text',
                    p.part -> 'metadata',
                    p.part -> 'files',
                    p.part -> 'functionCall',
                    p.part -> 'functionResult',
                    p.part ->> 'reasoning',
                    t.created_at,
                    t.updated_at,
                    t.deleted_at
                FROM thinking_thoughts t
                CROSS JOIN LATERAL jsonb_array_elements(to_jsonb(t.parts)) WITH ORDINALITY AS p(part, ordinality)
                WHERE jsonb_array_length(to_jsonb(t.parts)) > 0;
            ");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_thinking_thought_parts_text_trgm ON thinking_thought_parts USING gin (text gin_trgm_ops) WHERE text IS NOT NULL;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_thinking_sequences_summary_trgm ON thinking_sequences USING gin (summary gin_trgm_ops) WHERE summary IS NOT NULL;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_thinking_sequences_summary_embedding_hnsw ON thinking_sequences USING hnsw (summary_embedding vector_cosine_ops) WHERE summary_embedding IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thinking_thought_parts");

            migrationBuilder.DropIndex(
                name: "ix_thinking_sequences_summary_last_at",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "summary_embedding",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "summary_last_at",
                table: "thinking_sequences");
        }
    }
}
