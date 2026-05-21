using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_posts_title_trgm
                ON posts
                USING gin (title gin_trgm_ops)
                WHERE deleted_at IS NULL AND fediverse_uri IS NULL AND title IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_posts_description_trgm
                ON posts
                USING gin (description gin_trgm_ops)
                WHERE deleted_at IS NULL AND fediverse_uri IS NULL AND description IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_posts_content_trgm
                ON posts
                USING gin (content gin_trgm_ops)
                WHERE deleted_at IS NULL AND fediverse_uri IS NULL AND content IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_posts_content_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_posts_description_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_posts_title_trgm;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
