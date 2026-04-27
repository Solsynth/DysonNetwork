using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFileIndexWithHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "indexed",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_folder",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "parent_id",
                table: "files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(@"
CREATE TEMP TABLE tmp_file_index_canonical AS
SELECT DISTINCT ON (fi.file_id, fi.account_id)
    fi.file_id,
    fi.account_id,
    fi.path,
    fi.updated_at,
    fi.created_at
FROM file_indexes fi
ORDER BY fi.file_id, fi.account_id, fi.updated_at DESC, fi.created_at DESC;

WITH canonical_paths AS (
    SELECT DISTINCT
        account_id,
        CASE
            WHEN path IS NULL OR path = '' THEN '/'
            WHEN LEFT(path, 1) <> '/' THEN '/' || path
            ELSE path
        END AS normalized_path
    FROM tmp_file_index_canonical
),
path_tokens AS (
    SELECT
        cp.account_id,
        cp.normalized_path,
        NULLIF(TRIM(BOTH '/' FROM cp.normalized_path), '') AS trimmed_path
    FROM canonical_paths cp
),
folder_parts AS (
    SELECT
        pt.account_id,
        pt.normalized_path,
        parts.segment,
        parts.ordinality,
        ARRAY_TO_STRING((STRING_TO_ARRAY(pt.trimmed_path, '/'))[1:parts.ordinality], '/') AS partial_path
    FROM path_tokens pt
    CROSS JOIN LATERAL UNNEST(STRING_TO_ARRAY(pt.trimmed_path, '/')) WITH ORDINALITY AS parts(segment, ordinality)
    WHERE pt.trimmed_path IS NOT NULL
),
folder_rows AS (
    SELECT DISTINCT
        fp.account_id,
        '/' || fp.partial_path || '/' AS full_path,
        fp.segment AS name,
        CASE
            WHEN fp.ordinality = 1 THEN NULL
            ELSE '/' || ARRAY_TO_STRING((STRING_TO_ARRAY(fp.partial_path, '/'))[1:fp.ordinality - 1], '/') || '/'
        END AS parent_path
    FROM folder_parts fp
)
INSERT INTO files (
    id,
    name,
    description,
    user_meta,
    sensitive_marks,
    expired_at,
    uploaded_at,
    object_id,
    bundle_id,
    is_marked_recycle,
    storage_id,
    storage_url,
    account_id,
    created_at,
    updated_at,
    deleted_at,
    indexed,
    is_folder,
    parent_id
)
SELECT
    MD5(fr.account_id::text || ':' || fr.full_path),
    fr.name,
    NULL,
    '{}'::jsonb,
    '[]'::jsonb,
    NULL,
    NULL,
    NULL,
    NULL,
    FALSE,
    NULL,
    NULL,
    fr.account_id,
    NOW() AT TIME ZONE 'UTC',
    NOW() AT TIME ZONE 'UTC',
    NULL,
    TRUE,
    TRUE,
    CASE
        WHEN fr.parent_path IS NULL THEN NULL
        ELSE MD5(fr.account_id::text || ':' || fr.parent_path)
    END
FROM folder_rows fr
ON CONFLICT (id) DO NOTHING;

UPDATE files f
SET
    indexed = TRUE,
    parent_id = CASE
        WHEN TRIM(BOTH '/' FROM COALESCE(c.path, '/')) = '' THEN NULL
        ELSE MD5(
            c.account_id::text || ':' ||
            CASE
                WHEN LEFT(c.path, 1) <> '/' THEN '/' || TRIM(BOTH '/' FROM c.path) || '/'
                ELSE '/' || TRIM(BOTH '/' FROM c.path) || '/'
            END
        )
    END
FROM tmp_file_index_canonical c
WHERE f.id = c.file_id
  AND f.account_id = c.account_id;

DROP TABLE tmp_file_index_canonical;
");

            migrationBuilder.CreateIndex(
                name: "ix_files_account_id_indexed_is_marked_recycle_deleted_at",
                table: "files",
                columns: new[] { "account_id", "indexed", "is_marked_recycle", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_account_id_parent_id_indexed_deleted_at",
                table: "files",
                columns: new[] { "account_id", "parent_id", "indexed", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_parent_id",
                table: "files",
                column: "parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_files_parent_id",
                table: "files",
                column: "parent_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(
                name: "file_indexes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_files_parent_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_account_id_indexed_is_marked_recycle_deleted_at",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_account_id_parent_id_indexed_deleted_at",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_parent_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "indexed",
                table: "files");

            migrationBuilder.DropColumn(
                name: "is_folder",
                table: "files");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "files");

            migrationBuilder.CreateTable(
                name: "file_indexes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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
                name: "ix_file_indexes_file_id",
                table: "file_indexes",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_indexes_path_account_id",
                table: "file_indexes",
                columns: new[] { "path", "account_id" });
        }
    }
}
