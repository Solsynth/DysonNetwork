using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class DropPublisherAvatarHeaderUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fediverse actors used to keep their external avatar/header URL in dedicated
            // columns while local publishers kept the same image in the jsonb file reference.
            // Fold the remote images into that jsonb reference (addressed by Url) so a single
            // shape covers both, then drop the columns.
            //
            // The jsonb payload must use the property names System.Text.Json writes for the
            // dynamic-json columns (PascalCase, no naming policy).
            migrationBuilder.Sql(
                """
                UPDATE publishers
                SET picture = jsonb_build_object(
                        'Id', '', 'Name', '',
                        'FileMeta', '{}'::jsonb, 'UserMeta', '{}'::jsonb,
                        'SensitiveMarks', '[]'::jsonb,
                        'MimeType', NULL, 'Hash', NULL, 'Size', 0,
                        'HasCompression', false, 'HasThumbnail', false, 'Status', 0,
                        'Url', avatar_url,
                        'Width', NULL, 'Height', NULL, 'Blurhash', NULL,
                        'Usage', NULL, 'ApplicationType', NULL,
                        'CreatedAt', '{}'::jsonb, 'UpdatedAt', '{}'::jsonb
                    )
                WHERE avatar_url IS NOT NULL AND picture IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE publishers
                SET background = jsonb_build_object(
                        'Id', '', 'Name', '',
                        'FileMeta', '{}'::jsonb, 'UserMeta', '{}'::jsonb,
                        'SensitiveMarks', '[]'::jsonb,
                        'MimeType', NULL, 'Hash', NULL, 'Size', 0,
                        'HasCompression', false, 'HasThumbnail', false, 'Status', 0,
                        'Url', header_url,
                        'Width', NULL, 'Height', NULL, 'Blurhash', NULL,
                        'Usage', NULL, 'ApplicationType', NULL,
                        'CreatedAt', '{}'::jsonb, 'UpdatedAt', '{}'::jsonb
                    )
                WHERE header_url IS NOT NULL AND background IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "header_url",
                table: "publishers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_url",
                table: "publishers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE publishers
                SET avatar_url = picture ->> 'Url'
                WHERE picture -> 'Url' IS NOT NULL AND picture ->> 'Url' <> '';
                """);

            migrationBuilder.Sql(
                """
                UPDATE publishers
                SET header_url = background ->> 'Url'
                WHERE background -> 'Url' IS NOT NULL AND background ->> 'Url' <> '';
                """);
        }
    }
}
