using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_publishers_name_trgm
                ON publishers
                USING gin (name gin_trgm_ops)
                WHERE deleted_at IS NULL AND name IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_publishers_nick_trgm
                ON publishers
                USING gin (nick gin_trgm_ops)
                WHERE deleted_at IS NULL AND nick IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_publishers_bio_trgm
                ON publishers
                USING gin (bio gin_trgm_ops)
                WHERE deleted_at IS NULL AND bio IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_publishers_bio_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_publishers_nick_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_publishers_name_trgm;");
        }
    }
}
