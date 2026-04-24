using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class RepairThoughtPartBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM thinking_thought_parts p
                USING (
                    SELECT tp.thought_id
                    FROM thinking_thought_parts tp
                    GROUP BY tp.thought_id
                    HAVING bool_and(
                        COALESCE(NULLIF(BTRIM(tp.text), ''), '') = ''
                        AND COALESCE(NULLIF(BTRIM(tp.reasoning), ''), '') = ''
                        AND tp.function_call IS NULL
                        AND tp.function_result IS NULL
                        AND (tp.metadata IS NULL OR tp.metadata = '{}'::jsonb)
                        AND (tp.files IS NULL OR tp.files = '[]'::jsonb)
                    )
                ) bad
                WHERE p.thought_id = bad.thought_id;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO thinking_thought_parts
                (id, thought_id, sequence_id, part_index, type, text, metadata, files, function_call, function_result, reasoning, created_at, updated_at, deleted_at)
                SELECT
                    gen_random_uuid(),
                    t.id,
                    t.sequence_id,
                    p.ordinality - 1,
                    CASE
                        WHEN COALESCE(p.part ->> 'Type', p.part ->> 'type') IN ('0', 'Text', 'text') THEN 0
                        WHEN COALESCE(p.part ->> 'Type', p.part ->> 'type') IN ('1', 'FunctionCall', 'functionCall', 'function_call') THEN 1
                        WHEN COALESCE(p.part ->> 'Type', p.part ->> 'type') IN ('2', 'FunctionResult', 'functionResult', 'function_result') THEN 2
                        WHEN COALESCE(p.part ->> 'Type', p.part ->> 'type') IN ('3', 'Reasoning', 'reasoning') THEN 3
                        ELSE 0
                    END,
                    COALESCE(p.part ->> 'Text', p.part ->> 'text'),
                    COALESCE(p.part -> 'Metadata', p.part -> 'metadata'),
                    COALESCE(p.part -> 'Files', p.part -> 'files'),
                    COALESCE(p.part -> 'FunctionCall', p.part -> 'functionCall', p.part -> 'function_call'),
                    COALESCE(p.part -> 'FunctionResult', p.part -> 'functionResult', p.part -> 'function_result'),
                    COALESCE(p.part ->> 'Reasoning', p.part ->> 'reasoning'),
                    t.created_at,
                    t.updated_at,
                    t.deleted_at
                FROM thinking_thoughts t
                CROSS JOIN LATERAL jsonb_array_elements(t.parts) WITH ORDINALITY AS p(part, ordinality)
                WHERE jsonb_array_length(t.parts) > 0
                  AND NOT EXISTS (
                      SELECT 1
                      FROM thinking_thought_parts existing
                      WHERE existing.thought_id = t.id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
