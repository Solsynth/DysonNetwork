using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class ChangePresenceTypeToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE presence_activities
                    ALTER COLUMN type DROP DEFAULT,
                    ALTER COLUMN type TYPE text
                    USING CASE
                        WHEN type = 0 THEN 'unknown'
                        WHEN type = 1 THEN 'gaming'
                        WHEN type = 2 THEN 'music'
                        WHEN type = 3 THEN 'workout'
                        ELSE 'unknown'
                    END,
                    ALTER COLUMN type DROP NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE presence_activities
                    ALTER COLUMN type TYPE integer
                    USING CASE
                        WHEN type = 'unknown' THEN 0
                        WHEN type = 'gaming' THEN 1
                        WHEN type = 'music' THEN 2
                        WHEN type = 'workout' THEN 3
                        ELSE 0
                    END,
                    ALTER COLUMN type SET NOT NULL,
                    ALTER COLUMN type SET DEFAULT 0;
                """);
        }
    }
}
