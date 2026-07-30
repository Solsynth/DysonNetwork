using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddTestQuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_test_questions_tests_test_id",
                table: "test_questions");

            migrationBuilder.RenameColumn(
                name: "test_id",
                table: "test_questions",
                newName: "question_group_id");

            migrationBuilder.RenameIndex(
                name: "ix_test_questions_test_id_sort_order",
                table: "test_questions",
                newName: "ix_test_questions_question_group_id_sort_order");

            migrationBuilder.CreateTable(
                name: "test_question_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    config = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_question_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_question_group_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_question_group_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_question_group_assignments_test_question_groups_questi",
                        column: x => x.question_group_id,
                        principalTable: "test_question_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_test_question_group_assignments_tests_test_id",
                        column: x => x.test_id,
                        principalTable: "tests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_question_group_assignments_question_group_id",
                table: "test_question_group_assignments",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_question_group_assignments_test_id_question_group_id",
                table: "test_question_group_assignments",
                columns: new[] { "test_id", "question_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_question_group_assignments_test_id_sort_order",
                table: "test_question_group_assignments",
                columns: new[] { "test_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_test_question_groups_key",
                table: "test_question_groups",
                column: "key",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO test_question_groups (id, key, title, description, config, created_at, updated_at, deleted_at)
                SELECT
                    (substring(md5(id::text || ':question-group'), 1, 8) || '-' || substring(md5(id::text || ':question-group'), 9, 4) || '-' || substring(md5(id::text || ':question-group'), 13, 4) || '-' || substring(md5(id::text || ':question-group'), 17, 4) || '-' || substring(md5(id::text || ':question-group'), 21, 12))::uuid,
                    'legacy-' || id::text,
                    title || ' questions',
                    'Migrated question group for ' || key,
                    '{}'::jsonb,
                    created_at,
                    updated_at,
                    deleted_at
                FROM tests;

                INSERT INTO test_question_group_assignments (id, test_id, question_group_id, sort_order, created_at, updated_at, deleted_at)
                SELECT
                    (substring(md5(id::text || ':question-group-assignment'), 1, 8) || '-' || substring(md5(id::text || ':question-group-assignment'), 9, 4) || '-' || substring(md5(id::text || ':question-group-assignment'), 13, 4) || '-' || substring(md5(id::text || ':question-group-assignment'), 17, 4) || '-' || substring(md5(id::text || ':question-group-assignment'), 21, 12))::uuid,
                    id,
                    (substring(md5(id::text || ':question-group'), 1, 8) || '-' || substring(md5(id::text || ':question-group'), 9, 4) || '-' || substring(md5(id::text || ':question-group'), 13, 4) || '-' || substring(md5(id::text || ':question-group'), 17, 4) || '-' || substring(md5(id::text || ':question-group'), 21, 12))::uuid,
                    0,
                    created_at,
                    updated_at,
                    deleted_at
                FROM tests;

                UPDATE test_questions
                SET question_group_id = (
                    substring(md5(question_group_id::text || ':question-group'), 1, 8) || '-' || substring(md5(question_group_id::text || ':question-group'), 9, 4) || '-' || substring(md5(question_group_id::text || ':question-group'), 13, 4) || '-' || substring(md5(question_group_id::text || ':question-group'), 17, 4) || '-' || substring(md5(question_group_id::text || ':question-group'), 21, 12)
                )::uuid;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_test_questions_test_question_groups_question_group_id",
                table: "test_questions",
                column: "question_group_id",
                principalTable: "test_question_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_test_questions_test_question_groups_question_group_id",
                table: "test_questions");

            migrationBuilder.DropTable(
                name: "test_question_group_assignments");

            migrationBuilder.DropTable(
                name: "test_question_groups");

            migrationBuilder.RenameColumn(
                name: "question_group_id",
                table: "test_questions",
                newName: "test_id");

            migrationBuilder.RenameIndex(
                name: "ix_test_questions_question_group_id_sort_order",
                table: "test_questions",
                newName: "ix_test_questions_test_id_sort_order");

            migrationBuilder.AddForeignKey(
                name: "fk_test_questions_tests_test_id",
                table: "test_questions",
                column: "test_id",
                principalTable: "tests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
