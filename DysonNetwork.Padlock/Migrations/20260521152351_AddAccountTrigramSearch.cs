using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_accounts_name_trgm
                ON accounts
                USING gin (name gin_trgm_ops)
                WHERE deleted_at IS NULL AND name IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_accounts_nick_trgm
                ON accounts
                USING gin (nick gin_trgm_ops)
                WHERE deleted_at IS NULL AND nick IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accounts_nick_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accounts_name_trgm;");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
