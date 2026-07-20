using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class EnsureUniqueAccountProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM account_profiles AS duplicate
                USING account_profiles AS retained
                WHERE duplicate.account_id = retained.account_id
                  AND (duplicate.updated_at, duplicate.created_at, duplicate.id)
                    < (retained.updated_at, retained.created_at, retained.id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles",
                column: "account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles");
        }
    }
}
